using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.CvEnum;
using grbl_burn_em.Data;
using grbl_burn_em_emulator;

namespace grbl_burn_em.Tests;

/// <summary>
/// End-to-end tests driving the emulator's real rendering and camera pipeline
/// (EmulatorBoardRenderer + VirtualCamera) and feeding the frames into the
/// application's calibration engine.
/// </summary>
public class EmulatorCalibrationIntegrationTests
{
    private const float BedScale = 1.5f;
    private const int BedPx = 600;

    // The emulator's virtual camera: 1280x960 over a 120x90 mm FOV at 100 mm height,
    // principal point at the frame center, zero distortion.
    private static readonly CameraIntrinsics EmulatorIntrinsics = new()
    {
        CameraMatrix = new[] { 1066.667, 0, 640, 0, 1066.667, 480, 0, 0, 1 },
        DistCoeffs = new double[5],
        CalibratedImageWidth = 1280,
        CalibratedImageHeight = 960
    };

    // The application's board setup matching the emulator's board.
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

    private static Bitmap CreateBed(float boardX, float boardY, int squares = 5, float sizeMm = 120f)
    {
        var bed = new Bitmap(BedPx, BedPx);
        using (var g = Graphics.FromImage(bed))
            g.Clear(Color.Beige);
        EmulatorBoardRenderer.DrawBoard(bed, BedScale, boardX, boardY, squares, sizeMm,
            new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_50));
        return bed;
    }

    /// <summary>
    /// Captures a frame with the emulator's virtual camera: default offset (50,0,100),
    /// FOV 120x90, head (laser) at (headX, headY).
    /// </summary>
    private static Bitmap CaptureFrame(Bitmap bed, float headX, float headY)
    {
        var cam = VirtualCamera.Instance;
        cam.OffsetX = 50; cam.OffsetY = 0; cam.OffsetZ = 100;
        cam.FovWidth = 120; cam.FovHeight = 90;
        cam.ResX = 1280; cam.ResY = 960;
        cam.SimulateDistortion = false;
        cam.DistortionK1 = 0; cam.DistortionK2 = 0;
        cam.NoiseLevel = 0;
        cam.DrawCrosshair = false;
        EmulatorLogic.Instance.X = headX;
        EmulatorLogic.Instance.Y = headY;
        return cam.Capture(bed, BedScale);
    }

    // ================================================================
    // Detection through the full emulator pipeline
    // ================================================================

    [Fact]
    public void EmulatorPipeline_BoardInView_IsDetected()
    {
        // Board origin at (50,50), size 120 -> covers x[50,170], y[50,170].
        using var bed = CreateBed(50, 50);
        // Head at (60,110) + offset (50,0) -> camera at (110,110), the board center.
        using var frame = CaptureFrame(bed, 60, 110);
        using var mat = CameraCalibrationEngine.BitmapToMat(frame);

        var engine = new CameraCalibrationEngine(AppConfig());
        var det = engine.DetectBoard(mat);

        Assert.True(det.Detected,
            $"Board should be detected (markers={det.MarkerIds?.Size ?? -1}, corners={det.CharucoIds?.Size ?? -1})");
        Assert.True(det.MarkerIds!.Size >= 6);
        Assert.True(det.CharucoIds!.Size >= 6);
    }

    [Fact]
    public void EmulatorPipeline_JPEGCompressedFrame_IsDetected()
    {
        // Simulates the CameraServer network path (frames are JPEG-compressed).
        using var bed = CreateBed(50, 50);
        using var frame = CaptureFrame(bed, 60, 110);
        using var ms = new MemoryStream();
        frame.Save(ms, ImageFormat.Jpeg);
        ms.Position = 0;
        using var jpeg = new Bitmap(ms);
        using var mat = CameraCalibrationEngine.BitmapToMat(jpeg);

        var engine = new CameraCalibrationEngine(AppConfig());
        var det = engine.DetectBoard(mat);

        Assert.True(det.Detected,
            $"JPEG frame should still be detected (markers={det.MarkerIds?.Size ?? -1})");
    }

    [Fact]
    public void EmulatorPipeline_BoardPartiallyInView_IsDetected()
    {
        // Camera sees only the lower ~2/3 of the board (rows 2-4) - still above the
        // 6-marker minimum (charuco boards only place markers in half the squares).
        using var bed = CreateBed(50, 50);
        using var frame = CaptureFrame(bed, 60, 130); // camera at (110,130) -> x[50,170], y[85,175]
        using var mat = CameraCalibrationEngine.BitmapToMat(frame);

        var engine = new CameraCalibrationEngine(AppConfig());
        var det = engine.DetectBoard(mat);

        Assert.True(det.Detected,
            $"Partial view should be detected (markers={det.MarkerIds?.Size ?? -1}, corners={det.CharucoIds?.Size ?? -1})");
        Assert.True(det.MarkerIds!.Size >= 6);
    }

    // ================================================================
    // Stationary registration
    // ================================================================

    [Fact]
    public void StationaryRegistration_EmulatorFrame_RecoversWorldCoordinates()
    {
        using var bed = CreateBed(50, 50);
        using var frame = CaptureFrame(bed, 60, 110); // camera at (110,110)
        using var mat = CameraCalibrationEngine.BitmapToMat(frame);

        var engine = new CameraCalibrationEngine(AppConfig());
        var result = engine.ComputeWorkAreaHomographyWithPose(mat, EmulatorIntrinsics, 50, 50, 0);

        Assert.NotNull(result);
        Assert.NotNull(result.Value.homography);
        Assert.True(result.Value.reprojError < 1.0, $"reprojection error {result.Value.reprojError:F3}");

        // The principal point (frame center) shows the world point directly below the camera.
        var center = ApplyHomography(result.Value.homography!, 640, 480);
        Assert.True(Dist(center, new PointF(110, 110)) < 5,
            $"camera center: got ({center.X:F1},{center.Y:F1}), expected (110,110)");

        // The board origin corner must map to its world position (50,50).
        var originImg = CameraCalibrationEngine.ProjectBoardPoint(
            0, 0, result.Value.rvec!, result.Value.tvec!, EmulatorIntrinsics);
        var origin = ApplyHomography(result.Value.homography!, originImg.X, originImg.Y);
        Assert.True(Dist(origin, new PointF(50, 50)) < 5,
            $"board origin: got ({origin.X:F1},{origin.Y:F1}), expected (50,50)");

        // A world point (board corner 100,100 mm in board coords) maps back to its
        // image position: world -> image round trip through the inverse homography.
        var cornerImg = ApplyInverseHomography(result.Value.homography!, 150, 150);
        var cornerWorld = ApplyHomography(result.Value.homography!, cornerImg.X, cornerImg.Y);
        Assert.True(Dist(cornerWorld, new PointF(150, 150)) < 5,
            $"round trip: got ({cornerWorld.X:F1},{cornerWorld.Y:F1}), expected (150,150)");
    }

    // ================================================================
    // Head-mounted offset calibration
    // ================================================================

    [Fact]
    public void HeadMountedOffset_EmulatorFrame_RecoversConfiguredCameraOffset()
    {
        // Board origin at (60,60). Head at (70,100) -> camera at (120,100).
        using var bed = CreateBed(60, 60);
        using var frame = CaptureFrame(bed, 70, 100);
        using var mat = CameraCalibrationEngine.BitmapToMat(frame);

        var engine = new CameraCalibrationEngine(AppConfig());
        var pose = engine.SolveCameraPose(mat, EmulatorIntrinsics);
        Assert.NotNull(pose);

        var (ox, oy, oz) = CameraCalibrationEngine.ComputeHeadMountedOffset(
            pose.Value.rvec, pose.Value.tvec, 60, 60, 0, machineX: 70, machineY: 100);

        Assert.True(Math.Abs(ox - 50) < 4, $"offsetX: got {ox:F1}, expected 50");
        Assert.True(Math.Abs(oy - 0) < 4, $"offsetY: got {oy:F1}, expected 0");
        Assert.True(Math.Abs(oz - 100) < 6, $"offsetZ: got {oz:F1}, expected 100");
    }

    [Fact]
    public void HeadMountedOffset_EmulatorFrame_DifferentHeadPosition_SameOffset()
    {
        // Same board, different head position - the offset must stay the same.
        using var bed = CreateBed(60, 60);
        using var frame = CaptureFrame(bed, 90, 120); // camera at (140,120) -> x[80,200], y[75,165]
        using var mat = CameraCalibrationEngine.BitmapToMat(frame);

        var engine = new CameraCalibrationEngine(AppConfig());
        var pose = engine.SolveCameraPose(mat, EmulatorIntrinsics);
        Assert.NotNull(pose);

        var (ox, oy, oz) = CameraCalibrationEngine.ComputeHeadMountedOffset(
            pose.Value.rvec, pose.Value.tvec, 60, 60, 0, machineX: 90, machineY: 120);

        Assert.True(Math.Abs(ox - 50) < 4, $"offsetX: got {ox:F1}, expected 50");
        Assert.True(Math.Abs(oy - 0) < 4, $"offsetY: got {oy:F1}, expected 0");
        Assert.True(Math.Abs(oz - 100) < 6, $"offsetZ: got {oz:F1}, expected 100");
    }

    // ================================================================
    // Lens calibration on emulator frames
    // ================================================================

    [Fact]
    public void LensCalibration_EmulatorFrames_IsStableAndUndistorted()
    {
        // The emulator camera is orthographic (no perspective), so the absolute focal
        // length is under-determined; what must hold is: the calibration succeeds,
        // the reprojection error is tiny and the distortion is recovered as ~zero.
        using var bed = CreateBed(60, 60);
        var frames = new List<Mat>();
        try
        {
            // Camera positions within +/-20 mm of the board center (120,120) so the
            // whole board stays comfortably in view at every position.
            foreach (var (hx, hy) in new[]
            {
                (70f, 120f), (55f, 120f), (85f, 120f), (70f, 105f),
                (70f, 135f), (55f, 105f), (85f, 135f), (65f, 132f)
            })
            {
                using var frame = CaptureFrame(bed, hx, hy);
                using var mat = CameraCalibrationEngine.BitmapToMat(frame);
                frames.Add(mat.Clone());
            }

            var engine = new CameraCalibrationEngine(AppConfig());
            var result = engine.CalibrateLens(frames);

            Assert.NotNull(result);
            Assert.True(result!.UsedViewCount >= 6, $"used views: {result.UsedViewCount}");
            Assert.True(result.ReprojectionError < 1.0, $"RMSE {result.ReprojectionError:F3}");
            Assert.True(Math.Abs(result.DistCoeffs[0]) < 0.1, $"k1={result.DistCoeffs[0]:F4}");
            Assert.True(Math.Abs(result.DistCoeffs[1]) < 0.1, $"k2={result.DistCoeffs[1]:F4}");
            Assert.True(result.CameraMatrix[0] > 400 && result.CameraMatrix[0] < 4000,
                $"fx={result.CameraMatrix[0]:F0}");
        }
        finally
        {
            foreach (var f in frames) f.Dispose();
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static PointF ApplyHomography(double[] h, double x, double y)
    {
        double w = h[6] * x + h[7] * y + h[8];
        return new PointF(
            (float)((h[0] * x + h[1] * y + h[2]) / w),
            (float)((h[3] * x + h[4] * y + h[5]) / w));
    }

    private static PointF ApplyInverseHomography(double[] h, double x, double y)
    {
        double det = h[0] * (h[4] * h[8] - h[5] * h[7])
                   - h[1] * (h[3] * h[8] - h[5] * h[6])
                   + h[2] * (h[3] * h[7] - h[4] * h[6]);
        double invDet = 1.0 / det;
        double a = (h[4] * h[8] - h[5] * h[7]) * invDet;
        double b = (h[2] * h[7] - h[1] * h[8]) * invDet;
        double c = (h[1] * h[5] - h[2] * h[4]) * invDet;
        double d = (h[5] * h[6] - h[3] * h[8]) * invDet;
        double e = (h[0] * h[8] - h[2] * h[6]) * invDet;
        double f = (h[2] * h[3] - h[0] * h[5]) * invDet;
        double g = (h[3] * h[7] - h[4] * h[6]) * invDet;
        double k = (h[1] * h[6] - h[0] * h[7]) * invDet;
        double l = (h[0] * h[4] - h[1] * h[3]) * invDet;
        double w = g * x + k * y + l;
        return new PointF(
            (float)((a * x + b * y + c) / w),
            (float)((d * x + e * y + f) / w));
    }

    private static double Dist(PointF a, PointF b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
