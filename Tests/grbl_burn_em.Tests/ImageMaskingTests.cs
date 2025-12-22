using Xunit;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;
using grbl_burn_em.Tests.Utilities;
using System.Drawing;
using System.Linq;
using System;

namespace grbl_burn_em.Tests;

public class ImageMaskingTests
{
    private readonly GrblGenerator _generator = new();

    private void SetupEnvironment()
    {
        ProjectState.Instance.Objects.Clear();
        ProjectState.Instance.Layers.Clear();
        var layerId = Guid.NewGuid();
        ProjectState.Instance.Layers.Add(new Layer("Default", Color.Black)
        {
            Id = layerId,
            Mode = LayerMode.Fill,
            Power = 100,
            Speed = 1000
        });
    }

    [Fact]
    public void TestImageMaskedByRectangle()
    {
        SetupEnvironment();
        var layerId = ProjectState.Instance.Layers[0].Id;

        // Create a 100x100 image at (0,0)
        using var bmp = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black); // All black image
        }

        var image = new LaserImage
        {
            LayerId = layerId,
            Image = bmp,
            Position = new PointF(0, 0),
            Size = new SizeF(100, 100),
            IsEnabled = true,
            Name = "TestImage"
        };

        // Create a 50x50 mask at (25,25)
        var mask = new LaserRectangle
        {
            Id = Guid.NewGuid(),
            LayerId = layerId,
            Position = new PointF(25, 25),
            Size = new SizeF(50, 50),
            IsEnabled = true,
            Name = "MaskRect"
        };

        image.MaskId = mask.Id;

        ProjectState.Instance.Objects.Add(image);
        ProjectState.Instance.Objects.Add(mask);

        AppConfiguration.Instance.RasterLineInterval = 2.0f; // Low resolution for fast test

        var gcode = _generator.Generate(new[] { image }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        // Verification
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        Assert.NotEmpty(allPoints);

        // All burning points should be within the mask bounds (25,25) to (75,75)
        // Buffer should be at least one pixel width (RasterLineInterval = 2.0)
        float buffer = 2.5f; 
        Assert.All(allPoints, p => 
        {
            Assert.True(p.X >= 25 - buffer && p.X <= 75 + buffer, $"X point {p.X} is outside mask (25-75)");
            Assert.True(p.Y >= 25 - buffer && p.Y <= 75 + buffer, $"Y point {p.Y} is outside mask (25-75)");
        });
    }

    [Fact]
    public void TestImageMaskedByCircle()
    {
        SetupEnvironment();
        var layerId = ProjectState.Instance.Layers[0].Id;

        using var bmp = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
        }

        var image = new LaserImage
        {
            LayerId = layerId,
            Image = bmp,
            Position = new PointF(0, 0),
            Size = new SizeF(100, 100),
            IsEnabled = true
        };

        // Create a circle mask at (50,50) with radius 25
        var mask = new LaserCircle
        {
            Id = Guid.NewGuid(),
            LayerId = layerId,
            Position = new PointF(25, 25), // Bottom-left of circle AABB is (25,25)
            Size = new SizeF(50, 50),
            IsEnabled = true
        };

        image.MaskId = mask.Id;

        ProjectState.Instance.Objects.Add(image);
        ProjectState.Instance.Objects.Add(mask);

        AppConfiguration.Instance.RasterLineInterval = 1.0f; // Finer resolution for circle

        var gcode = _generator.Generate(new[] { image }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        Assert.NotEmpty(allPoints);

        // Center is at 50,50. Radius is 25.
        // Distance from 50,50 should be <= 25 (+ buffer)
        PointF center = new PointF(50, 50);
        float radius = 25f;
        float buffer = 2.0f; // Account for pixel width (1.0) and interpolation

        Assert.All(allPoints, p =>
        {
            float dist = (float)Math.Sqrt(Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2));
            Assert.True(dist <= radius + buffer, $"Point {p} is outside circular mask (dist {dist} > {radius})");
        });
    }

    [Fact]
    public void TestUnmaskDataEffect()
    {
        SetupEnvironment();
        var layerId = ProjectState.Instance.Layers[0].Id;

        using var bmp = new Bitmap(10, 10);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); }

        var mask = new LaserRectangle
        {
            Id = Guid.NewGuid(),
            Position = new PointF(4, 4),
            Size = new SizeF(2, 2)
        };
        ProjectState.Instance.Objects.Add(mask);

        var image = new LaserImage
        {
            LayerId = layerId,
            Image = bmp,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            IsEnabled = true,
            MaskId = mask.Id
        };
        ProjectState.Instance.Objects.Add(image);

        AppConfiguration.Instance.RasterLineInterval = 1.0f;

        // Generate with mask
        var gcodeMasked = _generator.Generate(new[] { image }).ToList();
        var simulatorMasked = new GCodeSimulator();
        simulatorMasked.Simulate(gcodeMasked);
        
        // Assert masked area (approx 4,4 to 6,6)
        var pointsMasked = simulatorMasked.Paths.SelectMany(p => p.Points).ToList();
        Assert.All(pointsMasked, p => {
            Assert.InRange(p.X, 3.5f, 6.5f);
            Assert.InRange(p.Y, 3.5f, 6.5f);
        });

        // Unmask
        image.MaskId = Guid.Empty;

        // Generate again
        var gcodeUnmasked = _generator.Generate(new[] { image }).ToList();
        var simulatorUnmasked = new GCodeSimulator();
        simulatorUnmasked.Simulate(gcodeUnmasked);
        
        // Assert full area (approx 0,0 to 10,10)
        var pointsUnmasked = simulatorUnmasked.Paths.SelectMany(p => p.Points).ToList();
        Assert.Contains(pointsUnmasked, p => p.X < 2); // Should have points near the edge
        Assert.Contains(pointsUnmasked, p => p.X > 8);
    }
}
