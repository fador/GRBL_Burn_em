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
}
