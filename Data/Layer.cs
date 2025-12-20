using System.Drawing;
using System.Text.Json.Serialization;

namespace laser_gui_test.Data;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LayerMode
{
    Cut, // Vector following
    Fill // Raster scan
}

public class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public Color Color { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; } = false;
    
    // Laser Settings
    public float Power { get; set; } = 80.0f; // %
    public float Speed { get; set; } = 1000.0f; // mm/min
    public LayerMode Mode { get; set; } = LayerMode.Cut;

    public Layer() 
    {
        Name = "New Layer";
        Color = Color.Red;
    }

    public Layer(string name, Color color)
    {
        Name = name;
        Color = color;
    }

    /// <summary>
    /// Scales the Power to a new value and adjusts Speed linearly to maintain the same energy density.
    /// Ratio = Power / Speed. New Speed = New Power / Ratio.
    /// </summary>
    public void ScaleToPower(float newPower)
    {
        if (Power == 0) return; // Prevent division by zero or invalid scaling from 0
        float ratio = Speed / Power;
        
        Power = newPower;
        Speed = Power * ratio;
    }

    /// <summary>
    /// Scales the Speed to a new value and adjusts Power linearly to maintain the same energy density.
    /// Ratio = Power / Speed. New Power = New Speed * Ratio.
    /// </summary>
    public void ScaleToSpeed(float newSpeed)
    {
        if (Speed == 0) return; // Prevent division by zero
        float ratio = Power / Speed;

        Speed = newSpeed;
        Power = Speed * ratio;
    }
}
