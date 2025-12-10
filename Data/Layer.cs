using System.Drawing;

namespace laser_gui_test.Data;

public class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public Color Color { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; } = false;

    public Layer(string name, Color color)
    {
        Name = name;
        Color = color;
    }
}
