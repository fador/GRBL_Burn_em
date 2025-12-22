/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using Xunit;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;

namespace grbl_burn_em.Tests;

public class GrblGeneratorTests
{
    private readonly GrblGenerator _generator = new();

    private void SetupDefaultLayer(Guid id)
    {
        ProjectState.Instance.Layers.Clear();
        ProjectState.Instance.Layers.Add(new Layer("Default", Color.Black)
        {
            Id = id,
            Power = 100,
            Speed = 1000,
            Mode = LayerMode.Cut
        });
    }

    [Fact]
    public void TestStartupAndShutdown()
    {
        SetupDefaultLayer(Guid.NewGuid());
        var objects = new List<LaserObject>();
        var gcode = _generator.Generate(objects).ToList();

        Assert.Contains("G21", gcode);
        Assert.Contains("G90", gcode);
        Assert.Contains("M4 S0", gcode);
        Assert.Contains("M5", gcode);
        Assert.Contains("G0 X0 Y0", gcode);
    }

    [Fact]
    public void TestRectangleGCode()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);
        
        var rect = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(10, 10),
            Size = new SizeF(20, 20),
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { rect }).ToList();

        // Should have a G0 to start position
        // Since it's a rectangle, it should have 4 G1 moves + closure
        Assert.Contains(gcode, s => s.StartsWith("G0 X10.000 Y10.000") || s.Contains("X10.000 Y10.000"));
        Assert.Contains(gcode, s => s.StartsWith("G1") && s.Contains("S1000")); // Power 100 * 10 = 1000
        Assert.Contains(gcode, s => s.Contains("F1000")); // Speed 1000
        
        // Check for 4 sides
        int cutMoves = gcode.Count(s => s.StartsWith("G1 X") && s.Contains("S1000"));
        Assert.True(cutMoves >= 4);
    }

    [Fact]
    public void TestCircleGCode()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var circle = new LaserCircle
        {
            LayerId = layerId,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { circle }).ToList();

        // Circles are linearized, should have many points
        int cutMoves = gcode.Count(s => s.StartsWith("G1 X") && s.Contains("S1000"));
        Assert.True(cutMoves > 10);
    }

    [Fact]
    public void TestPathGCode()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var path = new LaserPath
        {
            LayerId = layerId,
            IsEnabled = true
        };
        path.Points.Add(new PointF(5, 5));
        path.Points.Add(new PointF(15, 5));

        var gcode = _generator.Generate(new[] { path }).ToList();

        Assert.Contains("G0 X5.000 Y5.000", gcode);
        Assert.Contains("G1 X15.000 Y5.000 S1000", gcode);
    }

    [Fact]
    public void TestDisabledObject()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var rect = new LaserRectangle
        {
            LayerId = layerId,
            IsEnabled = false
        };

        var gcode = _generator.Generate(new[] { rect }).ToList();
        
        // Should only have startup/shutdown
        Assert.DoesNotContain(gcode, s => s.Contains("X") && !s.Contains("X0 Y0"));
    }
}
