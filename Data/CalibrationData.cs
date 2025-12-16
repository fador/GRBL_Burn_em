using System;
using System.Collections.Generic;
using System.Numerics;

namespace laser_gui_test.Data
{
    [Serializable]
    public class CalibrationData
    {
        // Intrinsics
        public double[] CameraMatrix { get; set; } = new double[9];
        public double[] DistCoeffs { get; set; } = new double[5];

        // Extrinsics (Camera in World Space)
        // Or Homography for 2D-2D mapping (simplest for top-down laser)
        // If camera is fixed top-down, a Homography matrix (3x3) maps Image Coords to World Coords.
        public double[] Homography { get; set; } = new double[9];
        
        // For Moving Camera (Head Mounted):
        // We need Offset from Laser Head.
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        
        // Settings
        public bool IsHeadMounted { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
    }
}
