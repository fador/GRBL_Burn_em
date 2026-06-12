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

public class MarlinGeneratorTests
{
    private readonly MarlinGenerator _generator = new();

    private void Setup(string onCmd = "M3", string offCmd = "M5", bool pwm = true)
    {
        AppConfiguration.Reset();
        AppConfiguration.Instance.ActiveProfile.ToolOnCommand = onCmd;
        AppConfiguration.Instance.ActiveProfile.ToolOffCommand = offCmd;
        AppConfiguration.Instance.ActiveProfile.EnablePWM = pwm;
        AppConfiguration.Instance.ActiveProfile.DefaultTravelSpeed = 3000;
        
        ProjectState.Instance.Layers.Clear();
        ProjectState.Instance.Layers.Add(new Layer("Default", Color.Black)
        {
            Id = Guid.NewGuid(),
            Power = 50, // 50% = S500
            Speed = 1000,
            Mode = LayerMode.Cut
        });
    }

    [Fact]
    public void TestStartupAndShutdown()
    {
        Setup("M280 P0 S90", "M280 P0 S0", false); // Pen Plotter style
        var objects = new List<LaserObject>();
        var gcode = _generator.Generate(objects).ToList();

        // Check Startup
        Assert.Contains("G21", gcode);
        Assert.Contains("G90", gcode);
        
        // Initial Tool Off
        Assert.Contains("M280 P0 S0", gcode);
        
        // Check Shutdown
        Assert.Contains("G0 X0 Y0", gcode);
        // Should end with Tool Off
        Assert.Equal("M280 P0 S0", gcode[gcode.Count - 2]); // Last is Home G0 X0 Y0 usually
    }

    [Fact]
    public void TestMultilineCommands()
    {
        Setup("M3\nG4 P0.5", "M5\nG4 P0.2", false);
        var layerId = ProjectState.Instance.Layers[0].Id;
        var rect = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { rect }).ToList();

        // Check if we see multiple lines for On
        Assert.Contains("M3", gcode);
        Assert.Contains("G4 P0.5", gcode);
        
        // Sequence check
        int idxM3 = gcode.IndexOf("M3");
        int idxDwell = gcode.IndexOf("G4 P0.5");
        Assert.Equal(idxM3 + 1, idxDwell);

        // Check Off
        Assert.Contains("M5", gcode);
        Assert.Contains("G4 P0.2", gcode);
    }

    [Fact]
    public void TestCustomPwmCommand()
    {
        Setup("M3", "M5", true);
        AppConfiguration.Instance.ActiveProfile.PwmCommand = "P";
        
        var layerId = ProjectState.Instance.Layers[0].Id;
        var rect = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { rect }).ToList();

        // Should see P500 instead of S500
        Assert.Contains(gcode, l => l.Contains("P500"));
        Assert.DoesNotContain(gcode, l => l.Contains("S500")); // Unless part of M3 S... wait. 
        // We removed S-appending to Tool On. So M3 is just M3.
        
        // Ensure standard S is not used for power on move
        Assert.DoesNotContain(gcode, l => l.StartsWith("G1") && l.Contains("S500"));
    }

    [Fact]
    public void TestVectorWithPWM()
    {
        Setup("M3", "M5", true); // PWM Enabled
        
        var layerId = ProjectState.Instance.Layers[0].Id;
        var rect = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            IsEnabled = true
        };

        var gcode = _generator.Generate(new[] { rect }).ToList();

        // We CHANGED LOGIC: ToolOn command is output as-is.
        // S-value is appended to G1 moves.
        
        // Tool On line
        Assert.Contains("M3", gcode);
        
        // G1 moves should have S500
        Assert.Contains(gcode, l => l.StartsWith("G1") && l.Contains("S500"));
    }
}

