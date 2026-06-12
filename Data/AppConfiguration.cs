/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Text.Json;
using System.Text.Json.Serialization;

namespace grbl_burn_em.Data;

public class AppConfiguration
{
    private static AppConfiguration? _instance;
    public static AppConfiguration Instance => _instance ??= Load();

    public static void Reset()
    {
        _instance = new AppConfiguration();
    }

    public List<MachineProfile> MachineProfiles { get; set; } = new();
    public string ActiveProfileId { get; set; } = "";

    [JsonIgnore]
    public MachineProfile ActiveProfile
    {
        get
        {
            if (MachineProfiles == null || MachineProfiles.Count == 0)
            {
                var defaultProfile = new MachineProfile { Name = "Default Machine" };
                if (MachineProfiles == null) MachineProfiles = new List<MachineProfile>();
                MachineProfiles.Add(defaultProfile);
                ActiveProfileId = defaultProfile.Id;
            }
            
            var profile = MachineProfiles.FirstOrDefault(p => p.Id == ActiveProfileId);
            if (profile == null)
            {
                profile = MachineProfiles.First();
                ActiveProfileId = profile.Id;
            }
            return profile;
        }
    }

    // Legacy properties for backward compatibility migration
    public string LastPortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public string GCodeGenerator { get; set; } = "Grbl";
    
    public float WorkAreaWidth { get; set; } = 400f;
    public float WorkAreaHeight { get; set; } = 400f;
    public string WorkOrigin { get; set; } = "BottomLeft";
    public float DefaultTravelSpeed { get; set; } = 5000f;
    public string ToolOnCommand { get; set; } = "M3";
    public string ToolOffCommand { get; set; } = "M5";
    public bool EnablePWM { get; set; } = true;
    public string PwmCommand { get; set; } = "S";

    // Global Settings
    public float RasterLineInterval { get; set; } = 0.3f;
    public float MinRasterSegmentLength { get; set; } = 0.2f;
    public bool EnableBicubicResampling { get; set; } = true;
    public float FramingPower { get; set; } = 0f;
    public float FramingSpeed { get; set; } = 1000f;
    public float SnapGridSize { get; set; } = 1.0f;
    public bool SkipSplashScreen { get; set; } = false;
    public float SvgCurveQuality { get; set; } = 0.002f; // Lower is better quality (more points)

    public bool Enable1BitDithering { get; set; } = false;
    public bool EmbedImagesInProject { get; set; } = false;
    public bool EnableSafetyBoundsCheck { get; set; } = true;
    
    // View Settings
    public float LastPanX { get; set; } = 0f;
    public float LastPanY { get; set; } = 0f;
    public float LastZoom { get; set; } = 1.0f;

    // Window Settings
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public int WindowState { get; set; } = 0; // 0=Normal, 1=Min, 2=Max
    
    // Camera Settings
    public string LastCameraDevice { get; set; } = "";
    public bool ShowCameraOverlay { get; set; } = false;
    public float CameraOverlayOpacity { get; set; } = 0.5f;
    public bool CameraIsMounted { get; set; } = false; // False = Stationary
    
    // Manual Overlay Override (Legacy/Simple mode)
    public float CameraOverlayX { get; set; } = 0;
    public float CameraOverlayY { get; set; } = 0;
    public float CameraOverlayWidth { get; set; } = 100;
    public float CameraOverlayHeight { get; set; } = 100;

    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }

    private static AppConfiguration Load()
    {
        AppConfiguration? config = null;
        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                config = JsonSerializer.Deserialize<AppConfiguration>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            }
        }
        
        config ??= new AppConfiguration();

        // Migrate legacy settings if no profiles exist
        if (config.MachineProfiles == null || config.MachineProfiles.Count == 0)
        {
            config.MachineProfiles = new List<MachineProfile>();
            var defaultProfile = new MachineProfile
            {
                Name = "Default Machine",
                PortName = config.LastPortName,
                BaudRate = config.BaudRate > 0 ? config.BaudRate : 115200,
                GCodeGenerator = string.IsNullOrEmpty(config.GCodeGenerator) ? "Grbl" : config.GCodeGenerator,
                WorkAreaWidth = config.WorkAreaWidth > 0 ? config.WorkAreaWidth : 400f,
                WorkAreaHeight = config.WorkAreaHeight > 0 ? config.WorkAreaHeight : 400f,
                WorkOrigin = string.IsNullOrEmpty(config.WorkOrigin) ? "BottomLeft" : config.WorkOrigin,
                DefaultTravelSpeed = config.DefaultTravelSpeed > 0 ? config.DefaultTravelSpeed : 5000f,
                ToolOnCommand = string.IsNullOrEmpty(config.ToolOnCommand) ? "M3" : config.ToolOnCommand,
                ToolOffCommand = string.IsNullOrEmpty(config.ToolOffCommand) ? "M5" : config.ToolOffCommand,
                EnablePWM = config.EnablePWM,
                PwmCommand = string.IsNullOrEmpty(config.PwmCommand) ? "S" : config.PwmCommand
            };
            config.MachineProfiles.Add(defaultProfile);
            config.ActiveProfileId = defaultProfile.Id;
        }

        return config;
    }
}
