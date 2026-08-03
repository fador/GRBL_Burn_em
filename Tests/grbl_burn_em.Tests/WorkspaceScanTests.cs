/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Aruco;
using grbl_burn_em.Data;
using grbl_burn_em_emulator;

namespace grbl_burn_em.Tests;

[Collection("Emulator")]
public class WorkspaceScanTests
{
    private const float BedScale = 1.5f;

    private static CharucoBoardConfig AppConfig(int squares = 5, float sizeMm = 120f)
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

    private static Bitmap CreateBed(float boardX = 50, float boardY = 50)
    {
        var bed = new Bitmap(600, 600);
        using (var g = Graphics.FromImage(bed))
            g.Clear(Color.Beige);
        EmulatorBoardRenderer.DrawBoard(bed, BedScale, boardX, boardY, 5, 120f,
            new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_50));
        return bed;
    }

    /// <summary>
    /// Configures the emulator's virtual camera and captures a frame with the board
    /// centered in view (camera at (110,110)).
    /// </summary>
    private static Bitmap CaptureFrame(Bitmap bed, bool distort, float k1 = 0.3f, float k2 = 0.1f)
    {
        var cam = VirtualCamera.Instance;
        cam.OffsetX = 50; cam.OffsetY = 0; cam.OffsetZ = 100;
        cam.FovWidth = 120; cam.FovHeight = 90;
        cam.ResX = 1280; cam.ResY = 960;
        cam.NoiseLevel = 0;
        cam.DrawCrosshair = false;
        cam.SimulateDistortion = distort;
        cam.DistortionK1 = k1;
        cam.DistortionK2 = k2;
        EmulatorLogic.Instance.X = 60;
        EmulatorLogic.Instance.Y = 110;
        return cam.Capture(bed, BedScale);
    }

    // ================================================================
    // Intrinsics scaling
    // ================================================================

    [Fact]
    public void ScaleIntrinsics_ToNewResolution_ScalesFocalAndPrincipalPoint()
    {
        var src = new CameraIntrinsics
        {
            CameraMatrix = new[] { 800.0, 0, 640, 0, 800.0, 480, 0, 0, 1 },
            DistCoeffs = new[] { 0.1, -0.05, 0, 0, 0 },
            CalibratedImageWidth = 1280,
            CalibratedImageHeight = 960
        };

        var scaled = CameraCalibrationEngine.ScaleIntrinsics(src, 640, 480);

        Assert.Equal(400, scaled.CameraMatrix[0], 3);
        Assert.Equal(400, scaled.CameraMatrix[4], 3);
        Assert.Equal(320, scaled.CameraMatrix[2], 3);
        Assert.Equal(240, scaled.CameraMatrix[5], 3);
        Assert.Equal(640, scaled.CalibratedImageWidth);
        Assert.Equal(480, scaled.CalibratedImageHeight);
        Assert.Equal(0.1, scaled.DistCoeffs[0], 3);

        // Scaling to the same resolution returns the same intrinsics instance.
        Assert.Same(src, CameraCalibrationEngine.ScaleIntrinsics(src, 1280, 960));
    }

    [Fact]
    public void ComputeScanGeometry_IsResolutionIndependent()
    {
        var intrinsics = new CameraIntrinsics
        {
            CameraMatrix = new[] { 800.0, 0, 700, 0, 800.0, 500, 0, 0, 1 }, // off-center principal point
            DistCoeffs = new double[5],
            CalibratedImageWidth = 1280,
            CalibratedImageHeight = 960
        };

        var atCal = CameraCalibrationEngine.ComputeScanGeometry(intrinsics, 100f, 1280, 960);
        var atLive = CameraCalibrationEngine.ComputeScanGeometry(intrinsics, 100f, 640, 480);

        // The same world geometry must be computed regardless of the frame resolution.
        Assert.Equal(atCal.fovW, atLive.fovW, 1);
        Assert.Equal(atCal.fovH, atLive.fovH, 1);
        Assert.Equal(atCal.shiftX, atLive.shiftX, 1);
        Assert.Equal(atCal.shiftY, atLive.shiftY, 1);

        // Spot-check values: fovW = 1280*100/800 = 160 mm, shiftX = (640-700)*100/800.
        Assert.Equal(160f, atCal.fovW, 1);
        Assert.Equal(120f, atCal.fovH, 1);
        Assert.Equal(-7.5f, atCal.shiftX, 1);
    }

    // ================================================================
    // Undistortion
    // ================================================================

    [Fact]
    public void UndistortImage_UndoesEmulatorDistortion()
    {
        using var bed = CreateBed();
        using var clean = CaptureFrame(bed, distort: false);
        using var distorted = CaptureFrame(bed, distort: true, k1: 0.2f, k2: 0.05f);
        VirtualCamera.Instance.SimulateDistortion = false;

        // The emulator applies the standard radial model with fx=fy=max(w,h)=1280.
        var intrinsics = new CameraIntrinsics
        {
            CameraMatrix = new[] { 1280.0, 0, 640, 0, 1280.0, 480, 0, 0, 1 },
            DistCoeffs = new[] { 0.2, 0.05, 0, 0, 0 },
            CalibratedImageWidth = 1280,
            CalibratedImageHeight = 960
        };

        var engine = new CameraCalibrationEngine(AppConfig());
        using var cleanMat = CameraCalibrationEngine.BitmapToMat(clean);
        using var distMat = CameraCalibrationEngine.BitmapToMat(distorted);
        using var recovered = new Mat();
        engine.UndistortImage(distMat, recovered, intrinsics);

        var detClean = engine.DetectBoard(cleanMat);
        var detRec = engine.DetectBoard(recovered);
        var detDist = engine.DetectBoard(distMat);

        Assert.True(detClean.Detected, "clean frame must detect");
        Assert.True(detRec.Detected, "undistorted frame must detect");
        Assert.True(detDist.Detected, "distorted frame must detect");

        // The undistorted frame's corner positions must match the clean frame.
        double maxErr = MaxCornerError(detClean, detRec);
        Assert.True(maxErr < 2.5, $"undistorted vs clean max corner error {maxErr:F2}px");

        // Sanity: the distortion actually moved the corners.
        double distErr = MaxCornerError(detClean, detDist);
        Assert.True(distErr > 3.0, $"distortion should move corners, got {distErr:F2}px");
    }

    [Fact]
    public void UndistortFrame_ScalesIntrinsicsToFrameResolution()
    {
        var store = CameraManager.Instance.CalibrationStore;
        var prevIntrinsics = store.Intrinsics;
        var prevBoard = store.BoardConfig;
        try
        {
            // Intrinsics calibrated at 1280x960 (matching the emulator's distortion model).
            store.Intrinsics = new CameraIntrinsics
            {
                CameraMatrix = new[] { 1280.0, 0, 640, 0, 1280.0, 480, 0, 0, 1 },
                DistCoeffs = new[] { 0.2, 0.05, 0, 0, 0 },
                CalibratedImageWidth = 1280,
                CalibratedImageHeight = 960
            };
            store.BoardConfig = AppConfig();

            using var bed = CreateBed();
            using var clean = CaptureFrame(bed, distort: false);
            using var distorted = CaptureFrame(bed, distort: true, k1: 0.2f, k2: 0.05f);
            VirtualCamera.Instance.SimulateDistortion = false;

            // Feed a HALF-RESOLUTION frame - the undistortion must scale the
            // intrinsics (calibrated at 1280x960) to 640x480 automatically.
            using var smallDistorted = new Bitmap(distorted, 640, 480);
            using var smallClean = new Bitmap(clean, 640, 480);

            using var und = CameraManager.Instance.UndistortFrame(smallDistorted);
            Assert.Equal(640, und.Width);
            Assert.Equal(480, und.Height);

            var engine = new CameraCalibrationEngine(AppConfig());
            using var undMat = CameraCalibrationEngine.BitmapToMat(und);
            using var cleanMat = CameraCalibrationEngine.BitmapToMat(smallClean);

            var detUnd = engine.DetectBoard(undMat);
            var detClean = engine.DetectBoard(cleanMat);
            Assert.True(detUnd.Detected, "undistorted half-res frame must detect");
            Assert.True(detClean.Detected);

            double maxErr = MaxCornerError(detClean, detUnd);
            Assert.True(maxErr < 3.0,
                $"scaled undistortion max corner error {maxErr:F2}px (intrinsics not scaled to frame resolution?)");
        }
        finally
        {
            store.Intrinsics = prevIntrinsics;
            store.BoardConfig = prevBoard;
        }
    }

    // ================================================================
    // Feathering / blending
    // ================================================================

    [Fact]
    public void CreateScanFrame_FeathersEdgesAndKeepsCenter()
    {
        using var src = new Bitmap(100, 100, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(src))
            g.Clear(Color.White);

        using var frame = CameraCalibrationEngine.CreateScanFrame(src, null);

        Assert.Equal(PixelFormat.Format32bppArgb, frame.PixelFormat);

        // Center: fully opaque, RGB preserved.
        Assert.Equal(255, frame.GetPixel(50, 50).A);
        Assert.Equal(255, frame.GetPixel(50, 50).R);
        Assert.Equal(255, frame.GetPixel(50, 50).G);
        Assert.Equal(255, frame.GetPixel(50, 50).B);

        // Corners and edges: fully transparent.
        Assert.Equal(0, frame.GetPixel(0, 0).A);
        Assert.Equal(0, frame.GetPixel(99, 99).A);
        Assert.Equal(0, frame.GetPixel(50, 0).A);

        // Inside the feather zone: intermediate alpha (12px zone -> 6px is half).
        int mid = frame.GetPixel(50, 6).A;
        Assert.True(mid > 40 && mid < 215, $"expected intermediate alpha, got {mid}");
    }

    [Fact]
    public void CreateScanFrame_WithDistortion_MasksInvalidUndistortRegion()
    {
        // Strong pincushion distortion: the undistorted image has no source data
        // near the borders (black margins). With feathering disabled, the alpha must
        // be zero exactly where the undistortion has no data.
        var intrinsics = new CameraIntrinsics
        {
            CameraMatrix = new[] { 300.0, 0, 150, 0, 300.0, 150, 0, 0, 1 },
            DistCoeffs = new[] { 5.0, 0, 0, 0, 0 },
            CalibratedImageWidth = 300,
            CalibratedImageHeight = 300
        };

        using var src = new Bitmap(300, 300, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(src))
            g.Clear(Color.White);

        using var frame = CameraCalibrationEngine.CreateScanFrame(src, intrinsics, featherFraction: 0f);

        // (5,150) is just inside the left edge, well within the invalid region.
        Assert.Equal(0, frame.GetPixel(5, 150).A);
        // (60,150) is well inside the valid region.
        Assert.Equal(255, frame.GetPixel(60, 150).A);
        // Center stays opaque.
        Assert.Equal(255, frame.GetPixel(150, 150).A);
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static double MaxCornerError(CameraCalibrationEngine.DetectionResult a, CameraCalibrationEngine.DetectionResult b)
    {
        var aIds = a.CharucoIds!.ToArray();
        var aCorners = a.CharucoCorners!.ToArray();
        var bIds = b.CharucoIds!.ToArray();
        var bCorners = b.CharucoCorners!.ToArray();

        var bById = new Dictionary<int, PointF>();
        for (int i = 0; i < bIds.Length; i++)
            bById[bIds[i]] = bCorners[i];

        double maxErr = 0;
        int matched = 0;
        for (int i = 0; i < aIds.Length; i++)
        {
            if (bById.TryGetValue(aIds[i], out var bp))
            {
                double dx = bp.X - aCorners[i].X;
                double dy = bp.Y - aCorners[i].Y;
                maxErr = Math.Max(maxErr, Math.Sqrt(dx * dx + dy * dy));
                matched++;
            }
        }
        Assert.True(matched >= 6, $"only {matched} corners matched between detections");
        return maxErr;
    }
}
