using Xunit;
using laser_gui_test.Data;
using laser_gui_test.Data.Generators;
using laser_gui_test.Tests.Utilities;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System;

namespace laser_gui_test.Tests;

public class GCodeSimulationTests
{
    private readonly GrblGenerator _generator = new();

    private void SetupDefaultLayer(Guid id, LayerMode mode = LayerMode.Cut)
    {
        ProjectState.Instance.Layers.Clear();
        ProjectState.Instance.Layers.Add(new Layer("Default", Color.Black)
        {
            Id = id,
            Power = 100,
            Speed = 1000,
            Mode = mode
        });
    }

    [Fact]
    public void TestRectangleSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);
        
        var rect = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(10, 10),
            Size = new SizeF(50, 30),
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { rect });
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        // Verify we have paths
        Assert.NotEmpty(simulator.Paths);
        
        // Combine all points from all paths
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        // Verify bounding box of the simulated paths
        float minX = allPoints.Min(p => p.X);
        float maxX = allPoints.Max(p => p.X);
        float minY = allPoints.Min(p => p.Y);
        float maxY = allPoints.Max(p => p.Y);

        Assert.Equal(10, minX, 1);
        Assert.Equal(60, maxX, 1);
        Assert.Equal(10, minY, 1);
        Assert.Equal(40, maxY, 1);
    }

    [Fact]
    public void TestCurvedTextOnPathSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId, LayerMode.Cut);

        // Path (Backbone)
        var path = new LaserPath { Id = Guid.NewGuid() };
        path.Points.Add(new PointF(0, 0));
        path.Points.Add(new PointF(100, 0));
        ProjectState.Instance.Objects.Clear();
        ProjectState.Instance.Objects.Add(path);

        var text = new LaserText
        {
            LayerId = layerId,
            Text = "HELLO",
            FontName = "Arial",
            FontSize = 20,
            PathId = path.Id,
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { text });
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        Assert.NotEmpty(simulator.Paths);
        
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        // Since it's on a horizontal path Y=0, and text is 20pt (~7mm)
        // the points should be around Y=0 (depending on VerticalOffset)
        // By default, text is above path in GDI+ but we flip it in GrblGenerator.
        // Let's check if points exist and are within reasonable bounds.
        
        Assert.True(allPoints.Count > 50); // Text should have many points
        Assert.All(allPoints, p => Assert.True(p.X >= -10 && p.X <= 110)); // Allow some spill for character widths
    }

    [Fact]
    public void TestRasterFillSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId, LayerMode.Fill);

        var rect = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            IsEnabled = true
        };

        // Rasterizer depends on AppConfiguration
        AppConfiguration.Instance.RasterLineInterval = 0.5f;

        var gcode = _generator.Generate(new[] { rect }).ToList();

        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        // Rasterized G-code should have many paths (one per line or segments)
        Assert.True(simulator.Paths.Count >= 20); // 10mm / 0.5mm = 20 lines

        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        float minX = allPoints.Min(p => p.X);
        float maxX = allPoints.Max(p => p.X);
        float minY = allPoints.Min(p => p.Y);
        float maxY = allPoints.Max(p => p.Y);

        // Check if it fills the 10x10 area
        Assert.InRange(minX, -1, 1);
        Assert.InRange(maxX, 9, 11);
        Assert.InRange(minY, -1, 1);
        Assert.InRange(maxY, 9, 11);
    }
}
