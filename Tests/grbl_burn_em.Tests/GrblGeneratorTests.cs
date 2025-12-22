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

    [Fact]
    public void TestGroupHoleFilling()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);
        
        // Ensure Fill Mode for this layer/test
         var layer = ProjectState.Instance.Layers.First(l => l.Id == layerId);
         layer.Mode = LayerMode.Fill;

        var group = new LaserGroup
        {
            LayerId = layerId,
            IsEnabled = true,
            Mode = LayerMode.Fill // Force Fill
        };

        // Outer Box 20x20
        var outer = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(0, 0),
            Size = new SizeF(20, 20),
            IsEnabled = true
        };

        // Inner Box 10x10 (Hole)
        var inner = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(5, 5),
            Size = new SizeF(10, 10),
            IsEnabled = true
        };

        group.Children.Add(outer);
        group.Children.Add(inner);

        // Generate
        var gcode = _generator.Generate(new[] { group }).ToList();

        // Verification Logic
        bool foundHole = false;
        
        // Iterate through gcode, look for patterns
        for(int i=0; i<gcode.Count - 2; i++)
        {
            string l1 = gcode[i];
            string l2 = gcode[i+1];
            string l3 = gcode[i+2];

            // Example sequence:
            // ... S1000 (Burn)
            // ... S0 (Off/Travel) OR G0 ... (Travel)
            // ... S1000 (Burn)
            
            bool isBurn1 = l1.Contains("S1000") || (l1.StartsWith("G1") && !l1.Contains("S0"));
            bool isOff = l2.Contains("S0") || l2.StartsWith("G0");
            bool isBurn2 = l3.Contains("S1000") || (l3.StartsWith("G1") && !l3.Contains("S0"));
            
            if (isBurn1 && isOff && isBurn2)
            {
                foundHole = true;
                break;
            }
        }
        
        Assert.True(foundHole, "Should find a gap (hole) in the raster lines.");
    }
}
