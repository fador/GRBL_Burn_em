using System;
using System.Collections.Generic;
using System.Drawing;
using Xunit;
using grbl_burn_em.Tools;

namespace grbl_burn_em.Tests
{
    public class LensDistortionTests
    {
        [Fact]
        public void TestDistortionAndUndistortion()
        {
            // 1. Setup Camera Parameters
            // Intrinsic Matrix K (fx, fy, cx, cy)
            double fx = 1000;
            double fy = 1000;
            double cx = 500;
            double cy = 500;
            double[] cameraMatrix = new double[] { fx, 0, cx, 0, fy, cy, 0, 0, 1 };

            // Distortion Coefficients (k1, k2, p1, p2, k3)
            // Barrel distortion: k1 < 0
            double k1 = -0.1;
            double k2 = 0.01;
            double[] distCoeffs = new double[] { k1, k2, 0, 0, 0 };

            // 2. Generate Asymmetric Circles Grid (Ideal Points in Normalized Image Coordinates)
            // Or better: Generate in "Screen Coordinates" assuming Ideal Camera, 
            // then Undistort them to get "Normalized", then Distort to get back?
            // "DistortPoint" takes Screen Coords (Ideal) and returns Screen Coords (Distorted).
            // "UndistortPoint" takes Screen Coords (Distorted) and returns Screen Coords (Ideal).

            // Let's generate points that form a grid in the Ideal Screen plane.
            int rows = 4;
            int cols = 11;
            float spacing = 40.0f; // pixels in ideal image

            List<PointF> idealPoints = new List<PointF>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    // Asymmetric Grid Logic usually used in OpenCV:
                    // y = r * spacing
                    // x = (2*c + (r % 2)) * spacing / 2.0  (roughly)
                    // Or simply:
                    // Even rows: 0, 1, 2...
                    // Odd rows: 0.5, 1.5, 2.5...
                    float y = r * spacing;
                    float x = c * spacing;
                    if (r % 2 == 1) x += spacing * 0.5f;

                    // Center the grid around principal point so distortion is symmetric
                    x += (float)cx - (cols * spacing / 2.0f);
                    y += (float)cy - (rows * spacing / 2.0f);

                    idealPoints.Add(new PointF(x, y));
                }
            }

            // 3. Distort the points
            // These points are "Linear" (Ideal). We want to simulate what the camera sees (Distorted).
            // DistortPoint function: Input Ideal -> Output Distorted.
            List<PointF> distortedPoints = new List<PointF>();
            foreach (var p in idealPoints)
            {
                // Note: Our DistortPoint func effectively takes Ideal Pixel Coords -> Distorted Pixel Coords
                var dp = CalibrationMath.DistortPoint(p, cameraMatrix, distCoeffs);
                distortedPoints.Add(dp);
            }

            // Verify distortion happened
            Assert.NotEqual(idealPoints[0].X, distortedPoints[0].X);
            // Further away from center -> more distortion.
            // Point close to center should be similar.
            var centerPoint = new PointF((float)cx, (float)cy);
            var centerDist = CalibrationMath.DistortPoint(centerPoint, cameraMatrix, distCoeffs);
            Assert.Equal(centerPoint.X, centerDist.X, 0.001);
            Assert.Equal(centerPoint.Y, centerDist.Y, 0.001);


            // 4. Undistort the points
            // Now take the "observed" distorted points and try to recover the ideal points.
            // UndistortPoint function: Input Distorted -> Output Ideal.
            List<PointF> recoveredPoints = new List<PointF>();
            double maxError = 0;
            
            for (int i = 0; i < distortedPoints.Count; i++)
            {
                var dp = distortedPoints[i];
                var recovered = CalibrationMath.UndistortPoint(dp, cameraMatrix, distCoeffs);
                
                var original = idealPoints[i];
                double dist = Math.Sqrt(Math.Pow(recovered.X - original.X, 2) + Math.Pow(recovered.Y - original.Y, 2));
                if (dist > maxError) maxError = dist;

                // Check individual point accuracy (tolerance 1.0 pixel is plenty for iterative solver)
                Assert.True(dist < 0.5, $"Undistortion error too high at index {i}: {dist} px. Orig: {original}, Distorted: {dp}, Recovered: {recovered}");
            }

            // 5. Final check
            Assert.True(maxError < 0.1, $"Max Undistortion Error: {maxError}");
        }
    }
}
