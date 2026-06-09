/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Text.Json;

namespace grbl_burn_em.Data;

public class CameraIntrinsics
{
    public double[] CameraMatrix { get; set; } = new double[9];
    public double[] DistCoeffs { get; set; } = new double[5];
    public double ReprojectionError { get; set; }
    public int UsedViewCount { get; set; }
    public int CalibratedImageWidth { get; set; }
    public int CalibratedImageHeight { get; set; }

    public bool IsValid => CameraMatrix[0] != 0;
}

public class StationaryRegistration
{
    public double[] Homography { get; set; } = new double[9];
    public double[] Rvec { get; set; } = new double[3];
    public double[] Tvec { get; set; } = new double[3];
    public double ReprojectionError { get; set; }

    public bool IsValid => Homography[8] > 0 || Homography[0] != 0;
}

public class HeadMountedOffset
{
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
}

public class CalibrationStore
{
    public CameraIntrinsics? Intrinsics { get; set; }
    public StationaryRegistration? Registration { get; set; }
    public HeadMountedOffset? Offset { get; set; }
    public CharucoBoardConfig? BoardConfig { get; set; }

    public bool HasIntrinsics => Intrinsics?.IsValid == true;
    public bool HasRegistration => Registration?.IsValid == true;
    public bool HasOffset => Offset != null;

    private static readonly string DefaultPath = "calibration.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static CalibrationStore Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (System.IO.File.Exists(path))
            {
                var json = System.IO.File.ReadAllText(path);
                var store = JsonSerializer.Deserialize<CalibrationStore>(json, JsonOptions);
                return store ?? new CalibrationStore();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load calibration: {ex.Message}");
        }
        return new CalibrationStore();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            System.IO.File.WriteAllText(path, json);
            CameraManager.Instance.ReloadCalibration();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save calibration: {ex.Message}");
        }
    }
}
