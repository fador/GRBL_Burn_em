/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Drawing;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

namespace grbl_burn_em.Data;

public class CameraCalibrationEngine
{
    public CharucoBoardConfig BoardConfig { get; set; }

    public CameraCalibrationEngine(CharucoBoardConfig config)
    {
        BoardConfig = config;
    }

    public DetectionResult DetectBoard(Mat image)
    {
        var result = new DetectionResult();
        if (image.IsEmpty) return result;

        using var board = BoardConfig.CreateBoard();
        using var dict = BoardConfig.GetDictionary();
        var param = DetectorParameters.GetDefault();

        var grey = new Mat();
        if (image.NumberOfChannels > 1)
            CvInvoke.CvtColor(image, grey, ColorConversion.Bgr2Gray);
        else
            image.CopyTo(grey);

        using var corners = new VectorOfVectorOfPointF();
        using var ids = new VectorOfInt();
        using var rejected = new VectorOfVectorOfPointF();

        ArucoInvoke.DetectMarkers(grey, dict, corners, ids, param, rejected);
        grey.Dispose();

        if (ids.Size < 4)
            return result;

        using var charucoCorners = new VectorOfPointF();
        using var charucoIds = new VectorOfInt();

        ArucoInvoke.InterpolateCornersCharuco(corners, ids, image, board, charucoCorners, charucoIds);

        result.Detected = charucoIds.Size >= 4;
        if (result.Detected)
        {
            result.CharucoCorners = new VectorOfPointF(charucoCorners.ToArray());
            result.CharucoIds = new VectorOfInt(charucoIds.ToArray());
            result.MarkerCorners = new VectorOfVectorOfPointF(corners.ToArrayOfArray());
            result.MarkerIds = new VectorOfInt(ids.ToArray());
        }

        return result;
    }

    public CameraIntrinsics? CalibrateLens(List<Mat> images)
    {
        if (images == null || images.Count < 6) return null;

        using var board = BoardConfig.CreateBoard();
        using var dict = BoardConfig.GetDictionary();
        var param = DetectorParameters.GetDefault();

        var allCharucoCorners = new VectorOfVectorOfPointF();
        var allCharucoIds = new VectorOfVectorOfInt();
        int validCount = 0;

        foreach (var image in images)
        {
            using var grey = new Mat();
            if (image.NumberOfChannels > 1)
                CvInvoke.CvtColor(image, grey, ColorConversion.Bgr2Gray);
            else
                image.CopyTo(grey);

            using var corners = new VectorOfVectorOfPointF();
            using var ids = new VectorOfInt();
            using var rejected = new VectorOfVectorOfPointF();
            ArucoInvoke.DetectMarkers(grey, dict, corners, ids, param, rejected);

            if (ids.Size < 4) continue;

            using var charucoCorners = new VectorOfPointF();
            using var charucoIds = new VectorOfInt();
            ArucoInvoke.InterpolateCornersCharuco(corners, ids, grey, board, charucoCorners, charucoIds);

            if (charucoIds.Size < 4) continue;

            allCharucoCorners.Push(charucoCorners);
            allCharucoIds.Push(charucoIds);
            validCount++;
        }

        if (validCount < 6) return null;

        int imgW = images[0].Width;
        int imgH = images[0].Height;
        var imgSize = new System.Drawing.Size(imgW, imgH);

        using var cameraMatrix = new Mat();
        using var distCoeffs = new Mat();
        var rvecs = new VectorOfMat();
        var tvecs = new VectorOfMat();

        double rmse = ArucoInvoke.CalibrateCameraCharuco(
            allCharucoCorners, allCharucoIds, board, imgSize,
            cameraMatrix, distCoeffs,
            rvecs, tvecs,
            CalibType.Default,
            new MCvTermCriteria(30, 0.001));

        double[] cm = new double[9];
        double[] dc = new double[5];
        Marshal.Copy(cameraMatrix.DataPointer, cm, 0, 9);
        Marshal.Copy(distCoeffs.DataPointer, dc, 0, Math.Min(5, distCoeffs.Rows * distCoeffs.Cols));

        return new CameraIntrinsics
        {
            CameraMatrix = cm,
            DistCoeffs = dc,
            ReprojectionError = rmse,
            UsedViewCount = validCount,
            CalibratedImageWidth = imgW,
            CalibratedImageHeight = imgH
        };
    }

    public (double[] rvec, double[] tvec, double reprojError)? SolveCameraPose(
        Mat image, CameraIntrinsics intrinsics)
    {
        if (!intrinsics.IsValid || image.IsEmpty) return null;

        var detection = DetectBoard(image);
        if (!detection.Detected) return null;

        int cornerCount = detection.CharucoIds!.Size;
        var idsArr = detection.CharucoIds.ToArray();
        var cornersArr = detection.CharucoCorners!.ToArray();

        var objPts = new PointF[cornerCount];
        var imgPts = new PointF[cornerCount];
        for (int i = 0; i < cornerCount; i++)
        {
            imgPts[i] = cornersArr[i];
            int id = idsArr[i];
            int row = id / BoardConfig.SquaresX;
            int col = id % BoardConfig.SquaresX;
            objPts[i] = new PointF(col * BoardConfig.SquareLengthMm, row * BoardConfig.SquareLengthMm);
        }

        using var cm = MatFromArray(intrinsics.CameraMatrix, 3, 3);
        using var dc = MatFromArray(intrinsics.DistCoeffs, 5, 1);
        using var rvecMat = new Mat();
        using var tvecMat = new Mat();
        using var vObj = new VectorOfPointF(objPts);
        using var vImg = new VectorOfPointF(imgPts);

        bool ok = CvInvoke.SolvePnP(vObj, vImg, cm, dc, rvecMat, tvecMat, false, SolvePnpMethod.Iterative);
        if (!ok) return null;

        double[] rvec = new double[3], tvec = new double[3];
        Marshal.Copy(rvecMat.DataPointer, rvec, 0, 3);
        Marshal.Copy(tvecMat.DataPointer, tvec, 0, 3);

        double reproj = ComputeReprojectionError(vObj, vImg, rvec, tvec, cm, dc);
        return (rvec, tvec, reproj);
    }

    public (double[]? homography, double[]? rvec, double[]? tvec, double reprojError)?
        ComputeWorkAreaHomographyWithPose(
        Mat image, CameraIntrinsics intrinsics,
        float boardWorldX, float boardWorldY, float boardWorldRotationDeg)
    {
        var pose = SolveCameraPose(image, intrinsics);
        if (pose == null) return null;
        var (rvec, tvec, reproj) = pose.Value;

        var h = ComputeHomographyFromPose(rvec, tvec, intrinsics, boardWorldX, boardWorldY, boardWorldRotationDeg);
        return (h, rvec, tvec, reproj);
    }

    public double[]? ComputeWorkAreaHomography(
        Mat image, CameraIntrinsics intrinsics,
        float boardWorldX, float boardWorldY, float boardWorldRotationDeg)
    {
        return ComputeWorkAreaHomographyWithPose(image, intrinsics, boardWorldX, boardWorldY, boardWorldRotationDeg)?.homography;
    }

    private double[]? ComputeHomographyFromPose(
        double[] rvec, double[] tvec, CameraIntrinsics intrinsics,
        float boardWorldX, float boardWorldY, float boardWorldRotationDeg)
    {

        using var rvecMat = MatFromArray(rvec, 3, 1);
        using var tvecMat = MatFromArray(tvec, 3, 1);
        using var rotMat = new Mat();
        CvInvoke.Rodrigues(rvecMat, rotMat);

        double[] R = new double[9];
        Marshal.Copy(rotMat.DataPointer, R, 0, 9);

        double fx = intrinsics.CameraMatrix[0];
        double fy = intrinsics.CameraMatrix[4];
        double cx = intrinsics.CameraMatrix[2];
        double cy = intrinsics.CameraMatrix[5];

        var H = new double[9];
        H[0] = fx * R[0] + cx * R[6]; H[1] = fx * R[1] + cx * R[7]; H[2] = fx * tvec[0] + cx * tvec[2];
        H[3] = fy * R[3] + cy * R[6]; H[4] = fy * R[4] + cy * R[7]; H[5] = fy * tvec[1] + cy * tvec[2];
        H[6] = R[6]; H[7] = R[7]; H[8] = tvec[2];

        float cosR = MathF.Cos(boardWorldRotationDeg * MathF.PI / 180f);
        float sinR = MathF.Sin(boardWorldRotationDeg * MathF.PI / 180f);
        var Tw = new double[9];
        Tw[0] = cosR; Tw[1] = -sinR; Tw[2] = (double)boardWorldX;
        Tw[3] = sinR; Tw[4] = cosR;  Tw[5] = (double)boardWorldY;
        Tw[6] = 0;    Tw[7] = 0;     Tw[8] = 1;

        var HworldToImage = MultiplyMat(H, Tw, 3);
        return InvertMat3x3(HworldToImage);
    }

    public void UndistortImage(Mat src, Mat dst, CameraIntrinsics intrinsics)
    {
        if (!intrinsics.IsValid || src.IsEmpty) return;
        using var cm = MatFromArray(intrinsics.CameraMatrix, 3, 3);
        using var dc = MatFromArray(intrinsics.DistCoeffs, 5, 1);
        CvInvoke.Undistort(src, dst, cm, dc);
    }

    public Mat UndistortImage(Mat src, CameraIntrinsics intrinsics)
    {
        if (!intrinsics.IsValid || src.IsEmpty) return src.Clone();
        var dst = new Mat();
        UndistortImage(src, dst, intrinsics);
        return dst;
    }

    public SizeF ComputeFovMm(float cameraHeightMm, CameraIntrinsics intrinsics)
    {
        if (!intrinsics.IsValid || cameraHeightMm <= 0) return SizeF.Empty;
        float fx = (float)intrinsics.CameraMatrix[0];
        float fy = (float)intrinsics.CameraMatrix[4];
        float worldW = cameraHeightMm * intrinsics.CalibratedImageWidth / fx;
        float worldH = cameraHeightMm * intrinsics.CalibratedImageHeight / fy;
        return new SizeF(worldW, worldH);
    }

    public void DrawDetectedBoard(Mat image, DetectionResult detection)
    {
        if (!detection.Detected) return;

        if (detection.MarkerCorners != null && detection.MarkerIds != null)
            ArucoInvoke.DrawDetectedMarkers(image, detection.MarkerCorners, detection.MarkerIds,
                new Bgr(0, 255, 0).MCvScalar);

        if (detection.CharucoCorners != null && detection.CharucoIds != null)
            ArucoInvoke.DrawDetectedCornersCharuco(image, detection.CharucoCorners, detection.CharucoIds,
                new Bgr(255, 0, 0).MCvScalar);
    }

    private static double ComputeReprojectionError(
        VectorOfPointF objPoints, VectorOfPointF imgPoints,
        double[] rvec, double[] tvec, Mat cameraMatrix, Mat distCoeffs)
    {
        using var rvecMat = MatFromArray(rvec, 3, 1);
        using var tvecMat = MatFromArray(tvec, 3, 1);
        using var proj = new Mat();
        CvInvoke.ProjectPoints(objPoints, rvecMat, tvecMat, cameraMatrix, distCoeffs, proj);

        double totalErr = 0;
        int count = Math.Min(imgPoints.Size, proj.Rows);
        var imgArr = imgPoints.ToArray();

        for (int i = 0; i < count; i++)
        {
            double[] projPt = new double[2];
            Marshal.Copy(proj.DataPointer + i * proj.Step, projPt, 0, 2);
            double dx = (double)imgArr[i].X - projPt[0];
            double dy = (double)imgArr[i].Y - projPt[1];
            totalErr += Math.Sqrt(dx * dx + dy * dy);
        }

        return count > 0 ? totalErr / count : double.MaxValue;
    }

    private static Mat MatFromArray(double[] data, int rows, int cols)
    {
        var mat = new Mat(rows, cols, DepthType.Cv64F, 1);
        Marshal.Copy(data, 0, mat.DataPointer, Math.Min(data.Length, rows * cols));
        return mat;
    }

    private static double[] MultiplyMat(double[] a, double[] b, int size)
    {
        var result = new double[size * size];
        for (int i = 0; i < size; i++)
            for (int j = 0; j < size; j++)
            {
                double sum = 0;
                for (int k = 0; k < size; k++)
                    sum += a[i * size + k] * b[k * size + j];
                result[i * size + j] = sum;
            }
        return result;
    }

    private static double[] InvertMat3x3(double[] m)
    {
        double det = m[0] * (m[4] * m[8] - m[5] * m[7])
                   - m[1] * (m[3] * m[8] - m[5] * m[6])
                   + m[2] * (m[3] * m[7] - m[4] * m[6]);

        if (Math.Abs(det) < 1e-10)
            return (double[])m.Clone();

        double invDet = 1.0 / det;
        var inv = new double[9];
        inv[0] = (m[4] * m[8] - m[5] * m[7]) * invDet;
        inv[1] = (m[2] * m[7] - m[1] * m[8]) * invDet;
        inv[2] = (m[1] * m[5] - m[2] * m[4]) * invDet;
        inv[3] = (m[5] * m[6] - m[3] * m[8]) * invDet;
        inv[4] = (m[0] * m[8] - m[2] * m[6]) * invDet;
        inv[5] = (m[2] * m[3] - m[0] * m[5]) * invDet;
        inv[6] = (m[3] * m[7] - m[4] * m[6]) * invDet;
        inv[7] = (m[1] * m[6] - m[0] * m[7]) * invDet;
        inv[8] = (m[0] * m[4] - m[1] * m[3]) * invDet;
        return inv;
    }

    public static Mat BitmapToMat(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
        try
        {
            int channels = bmp.PixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed ? 1
                : bmp.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb ? 3 : 4;
            var mat = new Mat(h, w, DepthType.Cv8U, channels);
            for (int y = 0; y < h; y++)
            {
                IntPtr src = bd.Scan0 + y * bd.Stride;
                IntPtr dst = mat.DataPointer + y * mat.Step;
                CopyMemory(dst, src, (uint)(w * channels));
            }
            bmp.UnlockBits(bd);
            if (channels == 4)
            {
                var rgb = new Mat();
                CvInvoke.CvtColor(mat, rgb, ColorConversion.Bgra2Bgr);
                mat.Dispose();
                return rgb;
            }
            return mat;
        }
        catch
        {
            bmp.UnlockBits(bd);
            throw;
        }
    }

    public static Bitmap MatToBitmap(Mat mat)
    {
        Mat? temp1 = null, temp2 = null;
        Mat display = mat;

        if (mat.NumberOfChannels == 1)
        {
            temp1 = new Mat();
            CvInvoke.CvtColor(mat, temp1, ColorConversion.Gray2Bgr);
            display = temp1;
        }
        else if (mat.NumberOfChannels == 4)
        {
            temp2 = new Mat();
            CvInvoke.CvtColor(mat, temp2, ColorConversion.Bgra2Bgr);
            display = temp2;
        }

        var bmp = new Bitmap(display.Width, display.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        for (int y = 0; y < display.Height; y++)
        {
            IntPtr src = display.DataPointer + y * display.Step;
            IntPtr dst = bd.Scan0 + y * bd.Stride;
            CopyMemory(dst, src, (uint)(bmp.Width * 3));
        }
        bmp.UnlockBits(bd);

        temp1?.Dispose();
        temp2?.Dispose();
        return bmp;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

    public class DetectionResult
    {
        public bool Detected { get; set; }
        public VectorOfPointF? CharucoCorners { get; set; }
        public VectorOfInt? CharucoIds { get; set; }
        public VectorOfVectorOfPointF? MarkerCorners { get; set; }
        public VectorOfInt? MarkerIds { get; set; }
    }
}
