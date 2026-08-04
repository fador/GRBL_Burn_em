/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using Emgu.CV.Aruco;
using Emgu.CV.Util;
using grbl_burn_em.Data;
using grbl_burn_em_emulator;

namespace grbl_burn_em.Tests;

/// <summary>
/// Tests for the lens-calibration "Center on Board" jog: the pixel->mm scaling
/// against the board's known size and the image->machine coordinate convention.
/// </summary>
[Collection("Emulator")]
public class CenterOnBoardTests
{
    private const float BedScale = 1.5f;

    private static CharucoBoardConfig AppConfig(int squares = 5, float sizeMm = 80f)
    {
        float sq = sizeMm / squares;
        return new CharucoBoardConfig
        {
            DictionaryName = "DICT_4X4_50",
            SquaresX = squares,
            SquaresY = squares,
            SquareLengthMm = sq,
            MarkerLengthMm = sq * 0.7f
        };
    }

    // Physical positions of the 5x7 board corners with ids 0,5,18,23 (span 60x100 mm,
    // physical center (50,70)).
    private static readonly int[] TestIds = { 0, 5, 18, 23 };

    private static CameraCalibrationEngine.DetectionResult MakeDetection(PointF[] pixels)
    {
        var det = new CameraCalibrationEngine.DetectionResult
        {
            Detected = true,
            CharucoCorners = new VectorOfPointF(pixels),
            CharucoIds = new VectorOfInt(TestIds)
        };
        return det;
    }

    // ================================================================
    // Jog math (no camera needed)
    // ================================================================

    [Fact]
    public void ComputeCenteringJog_CenteredBoard_ReturnsZero()
    {
        // Pixel bbox 60x100 centered exactly on the image center (320,240).
        var pixels = new[]
        {
            new PointF(290, 190), new PointF(350, 190),
            new PointF(350, 290), new PointF(290, 290)
        };
        var det = MakeDetection(pixels);

        var (dx, dy) = CameraCalibrationEngine.ComputeCenteringJog(det, 640, 480, AppConfig(5, 100f));
        Assert.Equal(0f, dx, 2);
        Assert.Equal(0f, dy, 2);
    }

    [Fact]
    public void ComputeCenteringJog_OffCenterBoard_ReturnsCorrectDelta()
    {
        // Same physical board, shifted 30px right and 30px down in the image.
        // mm/px = 60mm/60px = 1 -> jog (+30, +30) with image Y down = machine +Y.
        var pixels = new[]
        {
            new PointF(320, 220), new PointF(380, 220),
            new PointF(380, 320), new PointF(320, 320)
        };
        var det = MakeDetection(pixels);

        var (dx, dy) = CameraCalibrationEngine.ComputeCenteringJog(det, 640, 480, AppConfig(5, 100f));
        Assert.Equal(30f, dx, 2);
        Assert.Equal(30f, dy, 2);
    }

    [Fact]
    public void ComputeCenteringJog_UsesBoardPhysicalSizeForScale()
    {
        // Physical span 60x100 mm over 120x200 px -> mm/px = 0.5.
        // Pixel center 20px right of the image center -> dx = 20 * 0.5 = 10 mm.
        var pixels = new[]
        {
            new PointF(280, 140), new PointF(400, 140),
            new PointF(400, 340), new PointF(280, 340)
        };
        var det = MakeDetection(pixels);

        var (dx, dy) = CameraCalibrationEngine.ComputeCenteringJog(det, 640, 480, AppConfig(5, 100f));
        Assert.Equal(10f, dx, 2);
        Assert.Equal(0f, dy, 2);
    }

    [Fact]
    public void ComputeCenteringJog_NotDetected_ReturnsZero()
    {
        var det = new CameraCalibrationEngine.DetectionResult { Detected = false };
        var (dx, dy) = CameraCalibrationEngine.ComputeCenteringJog(det, 640, 480, AppConfig());
        Assert.Equal(0f, dx);
        Assert.Equal(0f, dy);
    }

    // ================================================================
    // End-to-end with the emulator
    // ================================================================

    [Fact]
    public void ComputeCenteringJog_EmulatorFrame_MatchesTrueOffset()
    {
        // 60mm board at (60,70): spans x[60,120], y[70,130], center (90,100).
        // Camera at (110,110) (head (60,110) + offset (50,0)) with FOV 120x90:
        // the whole board is in view, so the jog must be boardCenter - camera = (-20, -10).
        var bed = new Bitmap(600, 600);
        using (var g = Graphics.FromImage(bed))
            g.Clear(Color.Beige);
        EmulatorBoardRenderer.DrawBoard(bed, BedScale, 60, 70, 5, 60f,
            new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_50));

        var cam = VirtualCamera.Instance;
        cam.OffsetX = 50; cam.OffsetY = 0; cam.OffsetZ = 100;
        cam.FovWidth = 120; cam.FovHeight = 90;
        cam.ResX = 1280; cam.ResY = 960;
        cam.NoiseLevel = 0; cam.DrawCrosshair = false;
        cam.SimulateDistortion = false;
        EmulatorLogic.Instance.X = 60;
        EmulatorLogic.Instance.Y = 110;

        using var frame = cam.Capture(bed, BedScale);
        using var mat = CameraCalibrationEngine.BitmapToMat(frame);

        var config = AppConfig(5, 60f);
        var engine = new CameraCalibrationEngine(config);
        var detection = engine.DetectBoard(mat);
        Assert.True(detection.Detected, "board must be detected");

        var (dx, dy) = CameraCalibrationEngine.ComputeCenteringJog(detection, 1280, 960, config);

        Assert.True(Math.Abs(dx - (-20f)) < 2, $"dx: got {dx:F1}, expected -20");
        Assert.True(Math.Abs(dy - (-10f)) < 2, $"dy: got {dy:F1}, expected -10");
    }
}
