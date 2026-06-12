using System;
using System.Text.Json.Serialization;

namespace grbl_burn_em.Data;

public enum DeviceType
{
    LaserEngraver,
    PenPlotter,
    SpindleRouter
}

public class MachineProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Machine";
    public DeviceType Type { get; set; } = DeviceType.LaserEngraver;

    // Connection
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;

    // Machine Dimensions
    public float WorkAreaWidth { get; set; } = 400f;
    public float WorkAreaHeight { get; set; } = 400f;
    public string WorkOrigin { get; set; } = "BottomLeft";

    // Machine Settings
    public string GCodeGenerator { get; set; } = "Grbl";
    public float DefaultTravelSpeed { get; set; } = 5000f;

    // Commands (Laser / Spindle)
    public string ToolOnCommand { get; set; } = "M3";
    public string ToolOffCommand { get; set; } = "M5";
    public bool EnablePWM { get; set; } = true;
    public string PwmCommand { get; set; } = "S";

    // Clone helper
    public MachineProfile Clone()
    {
        return new MachineProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = this.Name + " (Copy)",
            Type = this.Type,
            PortName = this.PortName,
            BaudRate = this.BaudRate,
            WorkAreaWidth = this.WorkAreaWidth,
            WorkAreaHeight = this.WorkAreaHeight,
            WorkOrigin = this.WorkOrigin,
            GCodeGenerator = this.GCodeGenerator,
            DefaultTravelSpeed = this.DefaultTravelSpeed,
            ToolOnCommand = this.ToolOnCommand,
            ToolOffCommand = this.ToolOffCommand,
            EnablePWM = this.EnablePWM,
            PwmCommand = this.PwmCommand
        };
    }
}
