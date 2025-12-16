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
        
        // Calibration Pattern Settings
        public int PatternRows { get; set; } = 4;
        public int PatternCols { get; set; } = 11;
        public float PatternSpacingMm { get; set; } = 20.0f; // Distance between centers
        public CalibrationPatternType PatternType { get; set; } = CalibrationPatternType.AsymmetricCircles;

        // Settings
        public bool IsHeadMounted { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        
        // Helper for basic affine (if not using Homography)
        public float ScaleX { get; set; } = 1.0f;
        public float ScaleY { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0.0f; // Degrees
        public float TranslationX { get; set; }
        public float TranslationY { get; set; }
    }

    public enum CalibrationPatternType
    {
        Chessboard,
        Circles,
        AsymmetricCircles
    }
}
