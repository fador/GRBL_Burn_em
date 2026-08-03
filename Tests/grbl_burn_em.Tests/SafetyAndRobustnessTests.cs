/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using grbl_burn_em.Data;
using grbl_burn_em.Data.GCode;
using grbl_burn_em.Data.Generators;
using grbl_burn_em.Tests.Utilities;
using grbl_burn_em_emulator;

namespace grbl_burn_em.Tests;

// Serializes all tests that drive the shared emulator singletons
// (TcpServer, EmulatorLogic, SerialInterface) so they cannot interfere.
[CollectionDefinition("Emulator", DisableParallelization = false)]
public class EmulatorCollection { }

[Collection("Emulator")]
public class SafetyAndRobustnessTests
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

    // ================================================================
    // Raster laser safety
    // ================================================================

    [Fact]
    public void Raster_G0TravelMoves_ArePrecededByLaserOff()
    {
        // A solid black 30x30 image rasterized at 10mm lines -> 3 rows, each fully burning.
        using var bmp = new Bitmap(30, 30);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
        }

        var img = new LaserImage
        {
            Image = bmp,
            Position = new PointF(0, 0),
            Size = new SizeF(30, 30),
            Power = 100,
            Speed = 1000
        };

        var gcode = Rasterizer.Rasterize(img, maxPower: 1000, speed: 1000,
            lineInterval: 10, minSegmentLength: 0.2f,
            enableBicubic: false, enableDithering: false).ToList();

        // Every inter-row travel move (G0 with X/Y) must be preceded by an S0 line
        // (S is modal in GRBL - without it the laser would stay on during the rapid).
        for (int i = 0; i < gcode.Count; i++)
        {
            if (gcode[i].StartsWith("G0 X"))
            {
                Assert.True(i > 0 && gcode[i - 1] == "S0",
                    $"G0 travel at line {i} is not preceded by S0: ...{gcode[i - 1]} -> {gcode[i]}");
            }
        }

        // Simulate the real GRBL modal semantics and verify no burn path crosses rows.
        var simulator = new GCodeSimulator();
        simulator.Simulate(gcode);
        Assert.Equal(3, simulator.Paths.Count); // one burn path per raster row
    }

    [Fact]
    public void Raster_FirstRowG0_IsAlsoLaserOff()
    {
        using var bmp = new Bitmap(10, 10);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(Color.Black);

        var img = new LaserImage
        {
            Image = bmp,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            Power = 100,
            Speed = 1000
        };

        var gcode = Rasterizer.Rasterize(img, 1000, 1000, 10, 0.2f, false, false).ToList();
        Assert.Equal("S0", gcode[1]);
        Assert.StartsWith("G0 X", gcode[2]);
    }

    // ================================================================
    // Culture-invariant G-code
    // ================================================================

    [Fact]
    public void GCodeGeneration_IsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI"); // comma decimal separator
            CultureInfo.CurrentUICulture = new CultureInfo("fi-FI");

            var layerId = Guid.NewGuid();
            SetupDefaultLayer(layerId);

            var rect = new LaserRectangle
            {
                LayerId = layerId,
                Position = new PointF(10, 10),
                Size = new SizeF(20.5f, 20.5f),
                IsEnabled = true
            };

            var gcode = _generator.Generate(new[] { rect }).ToList();

            // All coordinate/power numbers must use '.' even under fi-FI.
            Assert.Contains(gcode, l => l.Contains("X10.000"));
            Assert.DoesNotContain(gcode, l => l.Contains(','));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = original;
        }
    }

    // ================================================================
    // GCodeParser modal semantics
    // ================================================================

    [Fact]
    public void GCodeParser_G0DoesNotResetPower()
    {
        // Real GRBL: S is modal - a G0 block keeps the current laser power value.
        var parsed = GCodeParser.Parse("M3 S1000\nG1 X10 Y10\nG0 X20 Y20\nG1 X30 S500");
        var g0 = parsed[2];
        Assert.Equal(CommandType.Travel, g0.Type);
        Assert.Equal(1000f, g0.Power); // S1000 still active on the G0 block
        Assert.Equal(20f, g0.End.X);

        var cut = parsed[3];
        Assert.Equal(CommandType.Cut, cut.Type);
        Assert.Equal(500f, cut.Power);
    }

    [Fact]
    public void GCodeParser_ExactCodeMatching()
    {
        // "G10" must not be treated as G1 (cut), "M30" must not be treated as M3.
        var parsed = GCodeParser.Parse("G10 L2 P1 X5\nM30\nM3 S500\nG1 X10");
        Assert.NotEqual(CommandType.Cut, parsed[0].Type); // G10 is not a cut move
        Assert.False(parsed[1].Power > 0); // M30 turns the laser off
        Assert.Equal(CommandType.Cut, parsed[3].Type); // G1 after M3 S500 is a cut
        Assert.Equal(500f, parsed[3].Power);
    }

    // ================================================================
    // Group rotation
    // ================================================================

    [Fact]
    public void LaserGroup_Rotation_IsAppliedInGeneratedGCode()
    {
        var layerId = Guid.NewGuid();
        SetupDefaultLayer(layerId);

        var rect = new LaserRectangle
        {
            LayerId = layerId,
            Position = new PointF(10, 10),
            Size = new SizeF(20, 10),
            IsEnabled = true
        };
        var group = new LaserGroup
        {
            LayerId = layerId,
            Rotation = 0
        };
        group.Children.Add(rect);

        var unrotated = _generator.Generate(new[] { group }).ToList();
        Assert.Contains("G0 X10.000 Y10.000", unrotated);

        group.Rotation = 90;
        var rotated = _generator.Generate(new[] { group }).ToList();
        // Rect (10,10,20,10) rotated 90deg clockwise around its center (20,15):
        // first corner lands at (25,5).
        Assert.Contains("G0 X25.000 Y5.000", rotated);
    }

    [Fact]
    public void LaserGroup_CreateRotatedChildren_RotatesGeometry()
    {
        var rect = new LaserRectangle
        {
            Position = new PointF(10, 10),
            Size = new SizeF(20, 10)
        };
        var group = new LaserGroup { Rotation = 90 };
        group.Children.Add(rect);

        var rotated = LaserGroup.CreateRotatedChildren(group);
        var r = Assert.IsType<LaserRectangle>(rotated[0]);
        // Center (20,15) is on the rotation axis, so it stays; the rect becomes
        // 10 wide x 20 tall standing on its side.
        Assert.Equal(10f, r.Position.X, 3);
        Assert.Equal(10f, r.Position.Y, 3);
        Assert.Equal(90f, r.Rotation, 3);
    }

    [Fact]
    public void LaserGroup_CreateRotatedChildren_RotatesPathPoints()
    {
        var path = new LaserPath();
        path.Points.Add(new PointF(10, 10));
        path.Points.Add(new PointF(30, 10));
        path.UpdateBounds(); // bounds (10,10)-(30,10), center (20,10)

        var group = new LaserGroup { Rotation = 90 };
        group.Children.Add(path);

        var rotated = LaserGroup.CreateRotatedChildren(group);
        var p = Assert.IsType<LaserPath>(rotated[0]);
        // (10,10) rotated 90deg cw around (20,10) -> (20,0)
        Assert.Equal(20f, p.Points[0].X, 3);
        Assert.Equal(0f, p.Points[0].Y, 3);
        // (30,10) rotated 90deg cw around (20,10) -> (20,20)
        Assert.Equal(20f, p.Points[1].X, 3);
        Assert.Equal(20f, p.Points[1].Y, 3);
    }

    // ================================================================
    // JobRunner end-to-end with the emulator
    // ================================================================

    [Fact]
    public async Task JobRunner_Emulator_CompletesWhenMachineIsIdle()
    {
        int port = 23501;
        TcpServer.Instance.Start(port);
        try
        {
            SerialInterface.Instance.Connect($"TCP:127.0.0.1:{port}", 115200);
            Assert.True(SerialInterface.Instance.IsConnected);

            try
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var runner = new JobRunner();
                runner.JobCompleted += () => tcs.TrySetResult(true);
                runner.JobFailed += (msg) => tcs.TrySetException(new Exception($"Job failed: {msg}"));

                runner.Start(new[] { "G0 X10 Y10", "G0 X20 Y20", "G0 X30 Y30" });

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(15000));
                Assert.True(completed == tcs.Task, "Job did not complete within 15s");
                Assert.False(runner.IsRunning);
                Assert.Equal("Idle", SerialInterface.Instance.MachineState);
                Assert.Equal(30f, SerialInterface.Instance.MachinePosition.X, 3);
            }
            finally
            {
                SerialInterface.Instance.Disconnect();
            }
        }
        finally
        {
            SerialInterface.Instance.Disconnect();
        }
    }

    [Fact]
    public async Task JobRunner_Disconnect_FailsJob()
    {
        int port = 23502;
        TcpServer.Instance.Start(port);
        try
        {
            SerialInterface.Instance.Connect($"TCP:127.0.0.1:{port}", 115200);
            Assert.True(SerialInterface.Instance.IsConnected);

            try
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var runner = new JobRunner();
                runner.JobFailed += (msg) => tcs.TrySetResult(true);
                runner.JobCompleted += () => tcs.TrySetResult(false);

                // Large enough job that it cannot finish before we drop the connection.
                var lines = new List<string>();
                for (int i = 0; i < 200; i++) lines.Add($"G0 X{100 + i} Y100");

                runner.Start(lines);
                await Task.Delay(300);
                SerialInterface.Instance.Disconnect(); // triggers ConnectionStatusChanged(false)

                var failureTask = tcs.Task;
                var completed = await Task.WhenAny(failureTask, Task.Delay(10000));
                Assert.True(completed == failureTask, "JobFailed did not fire after disconnect");
                Assert.True(await failureTask, "Expected a failure, not a successful completion");
                Assert.False(runner.IsRunning);
            }
            finally
            {
                SerialInterface.Instance.Disconnect();
            }
        }
        finally
        {
            SerialInterface.Instance.Disconnect();
        }
    }
}
