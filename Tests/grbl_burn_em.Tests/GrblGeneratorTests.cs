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
        AppConfiguration.Reset();
        AppConfiguration.Instance.RasterLineInterval = 1.0f; // Coarse interval for easier testing
        
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
        // We look for a line that traverses the hole (Y approx 10).
        // On that line, we should see Burn -> Travel (or S0) -> Burn.
        
        
        foreach(var line in gcode)
        {
            // Parse typical G1 X... Y... S...
            if (!line.StartsWith("G1") && !line.StartsWith("X")) continue;

            // Simple parsing to check if we are in the hole Y area
            // Note: This is fragile if format changes, but sufficient for now.
            // Expected format: G1 X10.000 Y10.000 S1000 or X15.000 S0
            
            // We really just want to scan the file for a sequence that indicates we skipped the middle.
            // Let's look for travel moves (S0 or simple Move) inside the X range 5-15 while Y is ~10.
            // But G-code is stateful.
        }

        // Better Approach:
        // Use a state machine to track current Y.
        // If Y is between 6 and 14, we check X moves.
        
        float currentY = -1;
        float currentX = 0;
        bool inHoleY = false;
        bool foundGapInHole = false;

        foreach(var l in gcode)
        {
            // Parse Y
            if (l.Contains("Y"))
            {
                var parts = l.Split(' ');
                foreach(var p in parts)
                {
                    if (p.StartsWith("Y"))
                    {
                        if (float.TryParse(p.Substring(1), out float yVal))
                        {
                            currentY = yVal;
                            inHoleY = (currentY > 6 && currentY < 14);
                        }
                    }
                    if (p.StartsWith("X"))
                    {
                         if (float.TryParse(p.Substring(1), out float xVal))
                        {
                            currentX = xVal;
                        }
                    }
                }
            }

            if (inHoleY)
            {
                // We are in the Y range of the hole.
                // Check if we have a Travel or S0 command that spans the hole interval (5 to 15).
                
                bool isTravel = l.Contains("S0") || l.StartsWith("G0");
                bool isBurn = l.Contains("S") && !l.Contains("S0") && !l.StartsWith("G0");
                
                // If we see a Travel/Off move that ends > 5 and < 15, that might be entering or inside the hole?
                // Actually, the GAP is characterized by NOT burning between X=5 and X=15.
                // So reliable check:
                // If we burn, we must be <= 5 or >= 15.
                // If we find a burn segment that crosses 5..15, then we FAILED.
                // If we traverse 5..15 without burning, GOOD.
                
                // BUT, parsing "traverse without burning" is hard from just lines.
                // EASIER: Check if we find a G0/Travel G1 S0 to somewhere inside the hole or crossing it?
                // Actually, rasterizer probably skips the hole.
                // So we should see:
                // G1 X5 S1000
                // G1 X15 S0 (Travel to other side)
                // G1 X20 S1000
                
                // OR
                // X5 S1000
                // X15 S0
                // X20 S1000
                
                if (l.Contains("S0") && l.Contains("X"))
                {
                    // Travel move. Check X destination.
                    var parts = l.Split(' ');
                    foreach(var p in parts)
                    {
                        if (p.StartsWith("X"))
                        {
                            if (float.TryParse(p.Substring(1), out float xDest))
                            {
                                // If we traveled to X=15 (right side of hole)
                                if (Math.Abs(xDest - 15) < 0.1 || Math.Abs(xDest - 5) < 0.1)
                                {
                                    // This is likely the skip
                                    foundGapInHole = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        Assert.True(foundGapInHole, "Should find a gap (travel move) crossing the hole.");
    }
}
