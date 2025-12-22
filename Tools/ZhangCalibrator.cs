/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace grbl_burn_em.Tools
{
    /// <summary>
    /// Implements Zhang's method for camera lens calibration.
    /// This provides the closed-form solution for intrinsic and extrinsic parameters.
    /// </summary>
    public class ZhangCalibrator
    {
        private readonly List<MyMatrix> _homographies = new List<MyMatrix>();
        private readonly List<PointSet> _viewpoints = new List<PointSet>();

        public struct PointSet
        {
            public List<double[]> WorldPoints; // (X, Y) - Z is assumed 0
            public List<double[]> ImagePoints; // (u, v)
        }

        public class CalibrationResult
        {
            public MyMatrix IntrinsicMatrix { get; set; } = null!; // K
            public double Alpha { get; set; }
            public double Beta { get; set; }
            public double Gamma { get; set; }
            public double U0 { get; set; }
            public double V0 { get; set; }
            public List<Extrinsics> ViewExtrinsics { get; set; } = new List<Extrinsics>();
        }

        public struct Extrinsics
        {
            public MyMatrix Rotation { get; set; }
            public double[] Translation { get; set; }
        }

        /// <summary>
        /// Adds a view (image of a calibration pattern).
        /// World points should be in a planar coordinate system (Z=0).
        /// </summary>
        public void AddView(List<double[]> worldPoints, List<double[]> imagePoints)
        {
            if (worldPoints.Count != imagePoints.Count || worldPoints.Count < 4)
                throw new ArgumentException("At least 4 point correspondences are required per view.");

            _viewpoints.Add(new PointSet { WorldPoints = worldPoints, ImagePoints = imagePoints });
            _homographies.Add(ComputeHomography(worldPoints, imagePoints));
        }

        /// <summary>
        /// Estimates the Homography H for a single view using DLT.
        /// </summary>
        private MyMatrix ComputeHomography(List<double[]> world, List<double[]> image)
        {
            int n = world.Count;
            // DLT involves finding null space of A. 
            // A has size 2*n x 9.
            var A = new MyMatrix(2 * n, 9);

            for (int i = 0; i < n; i++)
            {
                double X = world[i][0], Y = world[i][1];
                double u = image[i][0], v = image[i][1];

                // Row 1: [-X, -Y, -1, 0, 0, 0, uX, uY, u]
                A[2 * i, 0] = -X; A[2 * i, 1] = -Y; A[2 * i, 2] = -1;
                // 3,4,5 are 0
                A[2 * i, 6] = u * X; A[2 * i, 7] = u * Y; A[2 * i, 8] = u;

                // Row 2: [0, 0, 0, -X, -Y, -1, vX, vY, v]
                // 0,1,2 are 0
                A[2 * i + 1, 3] = -X; A[2 * i + 1, 4] = -Y; A[2 * i + 1, 5] = -1;
                A[2 * i + 1, 6] = v * X; A[2 * i + 1, 7] = v * Y; A[2 * i + 1, 8] = v;
            }

            // Find h in Null(A) -> eigenvector of A^T A associated with smallest eigenvalue
            var h = DefaultAlgebra.SolveHomogeneous(A);
            
            // h is h11, h12, h13, h21, h22, h23, h31, h32, h33
            return new MyMatrix(new[,] {
                { h[0], h[1], h[2] },
                { h[3], h[4], h[5] },
                { h[6], h[7], h[8] }
            });
        }

        /// <summary>
        /// Solves for the intrinsic matrix K.
        /// </summary>
        public CalibrationResult Calibrate()
        {
            if (_homographies.Count < 3)
                throw new InvalidOperationException("Zhang's method requires at least 3 views for a full solution.");

            // 1. Setup Vb = 0
            // Each homography gives 2 constraints on B.
            var V = new MyMatrix(2 * _homographies.Count, 6);
            for (int i = 0; i < _homographies.Count; i++)
            {
                var H = _homographies[i];
                var v11 = GetVVector(H, 0, 0);
                var v12 = GetVVector(H, 0, 1);
                var v22 = GetVVector(H, 1, 1);

                // Constraint 1: h1^T B h2 = 0  => v12^T b = 0
                SetRow(V, 2 * i, v12);
                // Constraint 2: h1^T B h1 - h2^T B h2 = 0 => (v11 - v22)^T b = 0
                SetRow(V, 2 * i + 1, DefaultAlgebra.Subtract(v11, v22));
            }

            // 2. Solve for b (elements of B = K^-T * K^-1)
            // b is null space of V
            var b = DefaultAlgebra.SolveHomogeneous(V);

            // 3. Extract Intrinsic Parameters from b
            // b = [B11, B12, B22, B13, B23, B33]
            double B11 = b[0], B12 = b[1], B22 = b[2], B13 = b[3], B23 = b[4], B33 = b[5];

            // Formulas from Zhang's paper
            double v0 = (B12 * B13 - B11 * B23) / (B11 * B22 - B12 * B12);
            double lambda = B33 - (B13 * B13 + v0 * (B12 * B13 - B11 * B23)) / B11;
            double alpha = Math.Sqrt(lambda / B11);
            double beta = Math.Sqrt(lambda * B11 / (B11 * B22 - B12 * B12));
            double gamma = -B12 * alpha * alpha * beta / lambda;
            double u0 = gamma * v0 / beta - B13 * alpha * alpha / lambda;

            // K
            var K = new MyMatrix(new[,] {
                { alpha, gamma, u0 },
                { 0,     beta,  v0 },
                { 0,     0,     1  }
            });

            // 4. Compute Extrinsics for each view
            var extrinsics = new List<Extrinsics>();
            var KInv = K.Inverse();
            foreach (var H in _homographies)
            {
                // H columns
                double[] h1 = new double[3] { H[0,0], H[1,0], H[2,0] };
                double[] h2 = new double[3] { H[0,1], H[1,1], H[2,1] };
                double[] h3 = new double[3] { H[0,2], H[1,2], H[2,2] };

                // s * KInv * h1
                var tmp1 = KInv.Multiply(h1);
                var tmp2 = KInv.Multiply(h2);
                
                double s1 = DefaultAlgebra.L2Norm(tmp1);
                double s2 = DefaultAlgebra.L2Norm(tmp2);
                double s = 1.0 / ((s1 + s2)/2.0); // take avg scale

                var r1 = DefaultAlgebra.Scale(s, tmp1);
                var r2 = DefaultAlgebra.Scale(s, tmp2);
                var r3 = DefaultAlgebra.Cross(r1, r2); // r3 = r1 x r2
                var t = DefaultAlgebra.Scale(s, KInv.Multiply(h3));

                // Force Rotation to be Orthogonal
                // R approx [r1 r2 r3]
                var R_approx = new MyMatrix(3, 3);
                for(int i=0; i<3; i++) {
                    R_approx[i,0] = r1[i];
                    R_approx[i,1] = r2[i];
                    R_approx[i,2] = r3[i];
                }
                
                var R_Ortho = DefaultAlgebra.Orthogonalize3x3(R_approx);

                extrinsics.Add(new Extrinsics { Rotation = R_Ortho, Translation = t });
            }

            return new CalibrationResult
            {
                IntrinsicMatrix = K,
                Alpha = alpha,
                Beta = beta,
                Gamma = gamma,
                U0 = u0,
                V0 = v0,
                ViewExtrinsics = extrinsics
            };
        }

        private void SetRow(MyMatrix M, int r, double[] data)
        {
            for(int c=0; c<M.Cols; c++) M[r,c] = data[c];
        }

        private double[] GetVVector(MyMatrix H, int i, int j)
        {
            // Helper for Zhang's Eq (8)
            // v_ij = [Hi1*Hj1, Hi1*Hj2 + Hi2*Hj1, Hi2*Hj2, Hi3*Hj1 + Hi1*Hj3, Hi3*Hj2 + Hi2*Hj3, Hi3*Hj3]
            // Note: Mathematical indices are 1-based, code 0-based.
            // H index: H[row, col]. Paper uses H = [h1 h2 h3]. so H[:, 0] is h1.
            
            double h0i = H[0, i], h1i = H[1, i], h2i = H[2, i];
            double h0j = H[0, j], h1j = H[1, j], h2j = H[2, j];

            return new[] {
                h0i * h0j,
                h0i * h1j + h1i * h0j,
                h1i * h1j,
                h2i * h0j + h0i * h2j,
                h2i * h1j + h1i * h2j,
                h2i * h2j
            };
        }
    }
}
