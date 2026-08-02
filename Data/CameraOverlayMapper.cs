/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace grbl_burn_em.Data;

/// <summary>
/// Warps a stationary camera frame into a world-rectified overlay using the
/// image-to-world homography produced by stationary registration.
/// </summary>
public static class CameraOverlayMapper
{
    /// <summary>
    /// Rectifies the frame into world coordinates (mm, Y-up) and returns the bitmap
    /// plus the world top-left position and size of the rectified overlay rectangle.
    /// </summary>
    public static bool TryCreateRectifiedOverlay(
        Bitmap frame, double[] homography,
        out Bitmap rectified, out PointF worldTopLeft, out SizeF worldSize)
    {
        rectified = null!;
        worldTopLeft = PointF.Empty;
        worldSize = SizeF.Empty;

        if (frame == null || homography == null || homography.Length < 9)
            return false;

        try
        {
            // Project the image corners through the image-to-world homography.
            var corners = new[]
            {
                new PointF(0, 0),
                new PointF(frame.Width, 0),
                new PointF(frame.Width, frame.Height),
                new PointF(0, frame.Height)
            };

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                var w = ApplyHomography(homography, c.X, c.Y);
                minX = Math.Min(minX, w.X);
                maxX = Math.Max(maxX, w.X);
                minY = Math.Min(minY, w.Y);
                maxY = Math.Max(maxY, w.Y);
            }

            float worldW = maxX - minX;
            float worldH = maxY - minY;
            if (worldW <= 0 || worldH <= 0 || worldW > 10000 || worldH > 10000)
                return false;

            const float pxPerMm = 3f;
            int destW = Math.Max(1, (int)Math.Round(worldW * pxPerMm));
            int destH = Math.Max(1, (int)Math.Round(worldH * pxPerMm));

            using var frameMat = CameraCalibrationEngine.BitmapToMat(frame);

            // World -> image is the inverse homography. Compose it with the world -> pixel
            // mapping: px = (wx - minX)*s, py = (maxY - wy)*s (image y points down).
            using var h = MatFromArray(homography, 3, 3);
            using var hInv = new Mat();
            CvInvoke.Invert(h, hInv, DecompMethod.LU);

            double[] hInvArr = new double[9];
            Marshal.Copy(hInv.DataPointer, hInvArr, 0, 9);

            double s = pxPerMm;
            double[] sMat =
            {
                s, 0, -minX * s,
                0, -s, maxY * s,
                0, 0, 1
            };

            // M = S * H_inv
            var m = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                        sum += sMat[i * 3 + k] * hInvArr[k * 3 + j];
                    m[i * 3 + j] = sum;
                }

            using var mMat = MatFromArray(m, 3, 3);
            using var warped = new Mat();
            CvInvoke.WarpPerspective(frameMat, warped, mMat,
                new System.Drawing.Size(destW, destH), Inter.Linear);

            rectified = CameraCalibrationEngine.MatToBitmap(warped);
            worldTopLeft = new PointF(minX, maxY);
            worldSize = new SizeF(worldW, worldH);
            return true;
        }
        catch
        {
            rectified?.Dispose();
            rectified = null!;
            return false;
        }
    }

    private static PointF ApplyHomography(double[] h, double x, double y)
    {
        double w = h[6] * x + h[7] * y + h[8];
        if (Math.Abs(w) < 1e-12) w = 1e-12;
        return new PointF(
            (float)((h[0] * x + h[1] * y + h[2]) / w),
            (float)((h[3] * x + h[4] * y + h[5]) / w));
    }

    private static Mat MatFromArray(double[] data, int rows, int cols)
    {
        var mat = new Mat(rows, cols, DepthType.Cv64F, 1);
        Marshal.Copy(data, 0, mat.DataPointer, Math.Min(data.Length, rows * cols));
        return mat;
    }
}
