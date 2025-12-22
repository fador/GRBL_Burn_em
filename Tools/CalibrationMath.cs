using System;
using System.Drawing;
using System.Collections.Generic;

namespace grbl_burn_em.Tools
{
    public static class CalibrationMath
    {
        /// <summary>
        /// Computes 3x3 Homography Matrix mapping src points to dst points using DLT.
        /// Corresponds to finding H such that dst = H * src.
        /// </summary>
        public static double[]? ComputeHomography(PointF[] src, PointF[] dst)
        {
            if (src == null || dst == null || src.Length != 4 || dst.Length != 4)
                return null;

            // We want to find H = [h1 h2 h3; h4 h5 h6; h7 h8 1]
            // x' = (h1 x + h2 y + h3) / (h7 x + h8 y + 1)
            // y' = (h4 x + h5 y + h6) / (h7 x + h8 y + 1)
            
            // Linear system Ah = B
            // h = [h1, h2, h3, h4, h5, h6, h7, h8]^T
            
            double[][] A = new double[8][];
            double[] B = new double[8];

            for (int i = 0; i < 4; i++)
            {
                double x = src[i].X;
                double y = src[i].Y;
                double u = dst[i].X;
                double v = dst[i].Y;

                // Equation for x' (u)
                A[2 * i] = new double[] { x, y, 1, 0, 0, 0, -x * u, -y * u };
                B[2 * i] = u;

                // Equation for y' (v)
                A[2 * i + 1] = new double[] { 0, 0, 0, x, y, 1, -x * v, -y * v };
                B[2 * i + 1] = v;
            }

            double[]? h = SolveGaussian(A, B);
            if (h == null) return null;

            return new double[] { h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1.0 };
        }

        private static double[]? SolveGaussian(double[][] A, double[] B)
        {
            int n = B.Length;
            // Augmented Matrix [A | B]
            double[][] M = new double[n][];
            for (int i = 0; i < n; i++)
            {
                M[i] = new double[n + 1];
                for (int j = 0; j < n; j++) M[i][j] = A[i][j];
                M[i][n] = B[i];
            }

            // Forward Elimination
            for (int i = 0; i < n; i++)
            {
                // Pivot
                int pivot = i;
                for (int j = i + 1; j < n; j++)
                {
                    if (Math.Abs(M[j][i]) > Math.Abs(M[pivot][i])) pivot = j;
                }

                // Swap rows
                double[] temp = M[i];
                M[i] = M[pivot];
                M[pivot] = temp;

                if (Math.Abs(M[i][i]) < 1e-9) return null; // Singular

                // Normalize row i
                for (int j = i + 1; j <= n; j++) M[i][j] /= M[i][i];
                M[i][i] = 1.0;

                // Eliminate other rows
                for (int j = 0; j < n; j++)
                {
                    if (i != j)
                    {
                        double factor = M[j][i];
                        for (int k = i; k <= n; k++) M[j][k] -= factor * M[i][k];
                    }
                }
            }

            // Extract result
            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = M[i][n];
            return x;
        }

        /// <summary>
        /// Applies perspective transform using Homography matrix H.
        /// </summary>
        public static PointF ApplyHomography(PointF p, double[] H)
        {
            double x = p.X;
            double y = p.Y;
            
            double w = H[6] * x + H[7] * y + H[8];
            // Avoid divide by zero
            if (Math.Abs(w) < 1e-9) w = 1.0;

            double newX = (H[0] * x + H[1] * y + H[2]) / w;
            double newY = (H[3] * x + H[4] * y + H[5]) / w;

            return new PointF((float)newX, (float)newY);
        }

        /// <summary>
        /// Undistorts a single point using Camera Matrix and Distortion Coefficients (k1, k2, p1, p2, k3).
        /// Iterative method to approximate the inverse of the distortion model.
        /// </summary>
        public static PointF UndistortPoint(PointF p, double[] cameraMatrix, double[] distCoeffs)
        {
            if (cameraMatrix == null || distCoeffs == null || cameraMatrix.Length != 9) return p;

            double fx = cameraMatrix[0];
            double fy = cameraMatrix[4];
            double cx = cameraMatrix[2];
            double cy = cameraMatrix[5];
            
            double k1 = distCoeffs.Length > 0 ? distCoeffs[0] : 0;
            double k2 = distCoeffs.Length > 1 ? distCoeffs[1] : 0;
            double p1 = distCoeffs.Length > 2 ? distCoeffs[2] : 0;
            double p2 = distCoeffs.Length > 3 ? distCoeffs[3] : 0;
            double k3 = distCoeffs.Length > 4 ? distCoeffs[4] : 0;

            // Normalize
            double x0 = (p.X - cx) / fx;
            double y0 = (p.Y - cy) / fy;

            double x = x0;
            double y = y0;

            // Iterative solver (Gauss-Newton / Fixed Point)
            // 5 iterations is usually enough
            for (int i = 0; i < 5; i++)
            {
                double r2 = x * x + y * y;
                double r4 = r2 * r2;
                double r6 = r4 * r2;

                double k = 1 + k1 * r2 + k2 * r4 + k3 * r6;
                
                // Tangential
                double deltaX = 2 * p1 * x * y + p2 * (r2 + 2 * x * x);
                double deltaY = p1 * (r2 + 2 * y * y) + 2 * p2 * x * y;

                // Predict distorted coords from current estimate (x, y)
                double x_pred = x * k + deltaX;
                double y_pred = y * k + deltaY;

                // Error
                x = x0 - (x_pred - x); // Simple fixed point adjustment? 
                                       // Usually: x_new = x0 - distortion_term
                                       // But distortion term depends on x_new.
                                       // Better approximation: x = (x0 - deltaX) / k
                
                // Let's use Inverse formulation:
                // x0 = x * k + deltaX => x = (x0 - deltaX) / k
                
                x = (x0 - deltaX) / k;
                y = (y0 - deltaY) / k;
            }

            // Denormalize
            return new PointF((float)(x * fx + cx), (float)(y * fy + cy));
        }
        /// <summary>
        /// Distorts a single point using Camera Matrix and Distortion Coefficients.
        /// This is the forward model: Ideal -> Distorted.
        /// </summary>
        public static PointF DistortPoint(PointF p, double[] cameraMatrix, double[] distCoeffs)
        {
            if (cameraMatrix == null || distCoeffs == null || cameraMatrix.Length != 9) return p;

            double fx = cameraMatrix[0];
            double fy = cameraMatrix[4];
            double cx = cameraMatrix[2];
            double cy = cameraMatrix[5];
            
            double k1 = distCoeffs.Length > 0 ? distCoeffs[0] : 0;
            double k2 = distCoeffs.Length > 1 ? distCoeffs[1] : 0;
            double p1 = distCoeffs.Length > 2 ? distCoeffs[2] : 0;
            double p2 = distCoeffs.Length > 3 ? distCoeffs[3] : 0;
            double k3 = distCoeffs.Length > 4 ? distCoeffs[4] : 0;

            // Normalize (Screen -> Normalized Device Coordinates)
            double x = (p.X - cx) / fx;
            double y = (p.Y - cy) / fy;

            double r2 = x * x + y * y;
            double r4 = r2 * r2;
            double r6 = r4 * r2;

            // Radial
            double k = 1 + k1 * r2 + k2 * r4 + k3 * r6;

            // Tangential
            double deltaX = 2 * p1 * x * y + p2 * (r2 + 2 * x * x);
            double deltaY = p1 * (r2 + 2 * y * y) + 2 * p2 * x * y;

            // Distorted Normalized
            double xDistort = x * k + deltaX;
            double yDistort = y * k + deltaY;

            // Denormalize (Normalized -> Screen)
            return new PointF((float)(xDistort * fx + cx), (float)(yDistort * fy + cy));
        }
    }
}
