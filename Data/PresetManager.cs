using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace grbl_burn_em.Data;

public static class PresetManager
{
    private static readonly string PresetsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MachinePresets");

    public static void EnsureDefaultPresets()
    {
        if (!Directory.Exists(PresetsDirectory))
        {
            Directory.CreateDirectory(PresetsDirectory);
        }

        // Check if directory is empty
        if (Directory.GetFiles(PresetsDirectory, "*.json").Length == 0)
        {
            CreateDefaultPreset(new MachineProfile
            {
                Name = "Generic Laser (Grbl)",
                Type = DeviceType.LaserEngraver,
                GCodeGenerator = "Grbl",
                ToolOnCommand = "M4",
                ToolOffCommand = "M5",
                PwmCommand = "S",
                EnablePWM = true,
                DefaultTravelSpeed = 5000f,
                BaudRate = 115200
            });

            CreateDefaultPreset(new MachineProfile
            {
                Name = "Generic Pen Plotter",
                Type = DeviceType.PenPlotter,
                GCodeGenerator = "Grbl", // Pen plotters often use GRBL too, but with M3
                ToolOnCommand = "M3",
                ToolOffCommand = "M5",
                PwmCommand = "S", // ignored usually if PWM is false
                EnablePWM = false,
                DefaultTravelSpeed = 3000f,
                BaudRate = 115200
            });

            CreateDefaultPreset(new MachineProfile
            {
                Name = "Generic Router (Marlin)",
                Type = DeviceType.SpindleRouter,
                GCodeGenerator = "Marlin",
                ToolOnCommand = "M106",
                ToolOffCommand = "M107",
                PwmCommand = "S",
                EnablePWM = true,
                DefaultTravelSpeed = 3000f,
                BaudRate = 250000
            });

            CreateDefaultPreset(new MachineProfile
            {
                Name = "GRBL Emulator",
                Type = DeviceType.LaserEngraver,
                GCodeGenerator = "Grbl",
                ToolOnCommand = "M4",
                ToolOffCommand = "M5",
                PwmCommand = "S",
                EnablePWM = true,
                DefaultTravelSpeed = 5000f,
                BaudRate = 115200,
                PortName = "TCP:127.0.0.1:2345"
            });
        }
    }

    private static void CreateDefaultPreset(MachineProfile profile)
    {
        // Don't save IDs in presets, they'll be generated on clone
        string filename = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars())) + ".json";
        string path = Path.Combine(PresetsDirectory, filename);

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(profile, options);
        File.WriteAllText(path, json);
    }

    public static List<MachineProfile> LoadPresets()
    {
        EnsureDefaultPresets();

        var presets = new List<MachineProfile>();
        var files = Directory.GetFiles(PresetsDirectory, "*.json");

        foreach (var file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<MachineProfile>(json);
                if (profile != null)
                {
                    presets.Add(profile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading preset {file}: {ex.Message}");
            }
        }

        return presets;
    }
}
