using System.Drawing;

namespace laser_gui_test.Data;

public enum LaserObjectType
{
    Path,
    Image,
    Rectangle
}

public abstract class LaserObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public Guid LayerId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public float Power { get; set; } = 100f; // 0-100%
    public float Speed { get; set; } = 1000f; // mm/min
    public PointF Position { get; set; }
    public float Rotation { get; set; }
    public SizeF Size { get; set; }
    public LaserObjectType Type { get; protected set; }

    public abstract void Draw(Graphics g, float scale);
    public abstract bool HitTest(PointF point);
}

public class LaserPath : LaserObject
{
    public List<PointF> Points { get; set; } = new();

    public LaserPath()
    {
        Type = LaserObjectType.Path;
    }

    public override void Draw(Graphics g, float scale)
    {
        if (Points.Count < 2) return;
        
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var pen = new Pen(c, 1.0f / scale); // Constant width regardless of zoom
        g.DrawLines(pen, Points.ToArray());
    }

    public override bool HitTest(PointF point)
    {
        // Simplified bounding box check for now
        var rect = new RectangleF(Position, Size);
        return rect.Contains(point); // TODO: Implement precise path hit testing
    }
}

public class LaserRectangle : LaserObject
{
    public LaserRectangle()
    {
        Type = LaserObjectType.Rectangle;
        Name = "Rectangle";
    }

    public override void Draw(Graphics g, float scale)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var pen = new Pen(c, 1.0f / scale);
        g.DrawRectangle(pen, Position.X, Position.Y, Size.Width, Size.Height);
    }
    
    public override bool HitTest(PointF point)
    {
         return new RectangleF(Position, Size).Contains(point);
    }
}

public class LaserImage : LaserObject
{
    // We shouldn't serialize Bitmap directly usually, but for GUI it's needed
    // In a real app we'd store the path or byte array
    public Bitmap? Image { get; set; }
    public string ImagePath { get; set; } = "";

    public LaserImage()
    {
        Type = LaserObjectType.Image;
        Name = "Image";
    }

    public override void Draw(Graphics g, float scale)
    {
        if (Image != null)
        {
            // Draw image within bounds
            g.DrawImage(Image, Position.X, Position.Y, Size.Width, Size.Height);
        }
        else
        {
            // Placeholder
            using var pen = new Pen(Color.Red, 1.0f / scale);
            g.DrawRectangle(pen, Position.X, Position.Y, Size.Width, Size.Height);
            g.DrawString("Img Missing", SystemFonts.DefaultFont, Brushes.Red, Position);
        }
    }

    public override bool HitTest(PointF point)
    {
        return new RectangleF(Position, Size).Contains(point);
    }
}
