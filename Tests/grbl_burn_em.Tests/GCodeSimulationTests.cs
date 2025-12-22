/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using Xunit;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;
using grbl_burn_em.Tests.Utilities;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System;

namespace grbl_burn_em.Tests;

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
    public void TestPathSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var path = new LaserPath
        {
            LayerId = layerId,
            IsEnabled = true
        };
        path.Points.Add(new PointF(10, 10));
        path.Points.Add(new PointF(20, 10));
        path.Points.Add(new PointF(20, 20));
        path.UpdateBounds();

        var gcode = _generator.Generate(new[] { path }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        Assert.NotEmpty(simulator.Paths);
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        Assert.Contains(allPoints, p => Math.Abs(p.X - 10) < 0.1 && Math.Abs(p.Y - 10) < 0.1);
        Assert.Contains(allPoints, p => Math.Abs(p.X - 20) < 0.1 && Math.Abs(p.Y - 10) < 0.1);
        Assert.Contains(allPoints, p => Math.Abs(p.X - 20) < 0.1 && Math.Abs(p.Y - 20) < 0.1);
    }

    [Fact]
    public void TestCircleSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var circle = new LaserCircle
        {
            LayerId = layerId,
            Position = new PointF(10, 10),
            Size = new SizeF(20, 20),
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { circle }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        Assert.NotEmpty(simulator.Paths);
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        // Linearized circle should range from 10 to 30 on both axes
        Assert.Equal(10, allPoints.Min(p => p.X), 1);
        Assert.Equal(30, allPoints.Max(p => p.X), 1);
        Assert.Equal(10, allPoints.Min(p => p.Y), 1);
        Assert.Equal(30, allPoints.Max(p => p.Y), 1);
    }

    [Fact]
    public void TestBezierSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var bezier = new LaserBezier
        {
            LayerId = layerId,
            IsEnabled = true
        };
        // Simple curve from (0,0) to (10,0) with control points at (3,5) and (7,5)
        bezier.Points.Add(new PointF(0, 0));
        bezier.Points.Add(new PointF(3, 5));
        bezier.Points.Add(new PointF(7, 5));
        bezier.Points.Add(new PointF(10, 0));
        bezier.UpdateBounds();

        var gcode = _generator.Generate(new[] { bezier }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        Assert.NotEmpty(simulator.Paths);
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        Assert.Equal(0, allPoints.Min(p => p.X), 1);
        Assert.Equal(10, allPoints.Max(p => p.X), 1);
        Assert.True(allPoints.Max(p => p.Y) > 2); // Should have some height
    }

    [Fact]
    public void TestStandardTextSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var text = new LaserText
        {
            LayerId = layerId,
            Text = "ABC",
            FontName = "Arial",
            FontSize = 10,
            Position = new PointF(10, 10),
            Size = new SizeF(20, 10), // Approximate size
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { text }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        Assert.NotEmpty(simulator.Paths);
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        // Text should be around the position
        Assert.All(allPoints, p => Assert.True(p.X >= 5 && p.X <= 40));
    }

    [Fact]
    public void TestImageSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId, LayerMode.Fill);

        using var bmp = new Bitmap(10, 10);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.FillRectangle(Brushes.Black, 2, 2, 6, 6);
        }

        var image = new LaserImage
        {
            LayerId = layerId,
            Image = bmp,
            Position = new PointF(10, 10),
            Size = new SizeF(20, 20),
            IsEnabled = true
        };

        AppConfiguration.Instance.RasterLineInterval = 1.0f;

        var gcode = _generator.Generate(new[] { image }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        Assert.NotEmpty(simulator.Paths);
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        // Image at (10,10) size 20x20. Black square 60% in middle (20% padding)
        // Expected black area approx (14,14) to (26,26)
        Assert.InRange(allPoints.Min(p => p.X), 13, 15);
        Assert.InRange(allPoints.Max(p => p.X), 25, 27);
    }

    [Fact]
    public void TestGroupSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var rect1 = new LaserRectangle { Position = new PointF(0, 0), Size = new SizeF(10, 10), LayerId = layerId };
        var rect2 = new LaserRectangle { Position = new PointF(20, 20), Size = new SizeF(10, 10), LayerId = layerId };

        var group = new LaserGroup
        {
            LayerId = layerId,
            IsEnabled = true
        };
        group.Children.Add(rect1);
        group.Children.Add(rect2);

        var gcode = _generator.Generate(new[] { group }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        // Should have at least 2 distinct path groups (one for each rect)
        // But the simulator concatenates into Paths based on power transitions.
        // Each rect has 4 segments + closure.
        Assert.True(simulator.Paths.Count >= 2);
        
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        Assert.Contains(allPoints, p => Math.Abs(p.X - 0) < 0.1 && Math.Abs(p.Y - 0) < 0.1);
        Assert.Contains(allPoints, p => Math.Abs(p.X - 30) < 0.1 && Math.Abs(p.Y - 30) < 0.1);
    }

    [Fact]
    public void TestCircleFillSimulation()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId, LayerMode.Fill);

        var circle = new LaserCircle
        {
            LayerId = layerId,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            IsEnabled = true
        };

        AppConfiguration.Instance.RasterLineInterval = 0.5f;

        var gcode = _generator.Generate(new[] { circle }).ToList();
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);

        // Circle fill should have many scan lines
        Assert.True(simulator.Paths.Count > 10);
        
        var allPoints = simulator.Paths.SelectMany(p => p.Points).ToList();
        
        // Check if points are roughly inside the circle bounds
        Assert.True(allPoints.All(p => p.X >= -1 && p.X <= 11 && p.Y >= -1 && p.Y <= 11));
        
        // Check "middle" line (Y=5)
        var middlePoints = allPoints.Where(p => Math.Abs(p.Y - 5) < 0.1).ToList();
        Assert.NotEmpty(middlePoints);
        Assert.Equal(0, middlePoints.Min(p => p.X), 0.5);
        Assert.Equal(10, middlePoints.Max(p => p.X), 0.5);
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
