using System.Drawing;
using System;

namespace laser_gui_test.Data;

public enum LaserObjectType
{
    Path,
    Image,
    Rectangle,
    Group
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
    public LaserObject? Parent { get; set; }

    public abstract void Draw(Graphics g, float scale);
    public abstract bool HitTest(PointF point);
    
    public virtual RectangleF GetBounds()
    {
        return new RectangleF(Position, Size);
    }
}

public class LaserPath : LaserObject
{
    public List<PointF> Points { get; set; } = new();

    public LaserPath()
    {
        Type = LaserObjectType.Path;
    }

    public override RectangleF GetBounds()
    {
        if (Points.Count == 0) return RectangleF.Empty;
        float minX = Points.Min(p => p.X);
        float minY = Points.Min(p => p.Y);
        float maxX = Points.Max(p => p.X);
        float maxY = Points.Max(p => p.Y);
        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
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
        const float Tolerance = 10.0f; // Increased for better usability
        // Actually grid is 1cm = 37.8px. 5px is small.
        
        // BBox optimization
        float bBuffer = Tolerance;
        float minX = Points.Min(p => p.X) - bBuffer;
        float minY = Points.Min(p => p.Y) - bBuffer;
        float maxX = Points.Max(p => p.X) + bBuffer;
        float maxY = Points.Max(p => p.Y) + bBuffer;
        
        if (point.X < minX || point.X > maxX || point.Y < minY || point.Y > maxY) 
            return false;

        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (DistanceToSegment(point, Points[i], Points[i + 1]) <= Tolerance)
                return true;
        }
        return false;
    }

    private float DistanceToSegment(PointF p, PointF v, PointF w)
    {
        float l2 = DistSq(v, w);
        if (l2 == 0) return Dist(p, v);
        float t = ((p.X - v.X) * (w.X - v.X) + (p.Y - v.Y) * (w.Y - v.Y)) / l2;
        t = Math.Max(0, Math.Min(1, t));
        PointF projection = new PointF(v.X + t * (w.X - v.X), v.Y + t * (w.Y - v.Y));
        return Dist(p, projection);
    }

    private float Dist(PointF p1, PointF p2) => (float)Math.Sqrt(DistSq(p1, p2));
    private float DistSq(PointF p1, PointF p2) => (p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y);
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
         // Edge-only hit test for Laser Cutting (Hollow)
         const float Tolerance = 8.0f; 
         
         float l = Position.X;
         float t = Position.Y;
         float r = l + Size.Width;
         float b = t + Size.Height;
         
         if (point.X < l - Tolerance || point.X > r + Tolerance || point.Y < t - Tolerance || point.Y > b + Tolerance) 
             return false;
             
         // Check distance to 4 lines
         bool hitLeft = Math.Abs(point.X - l) <= Tolerance && point.Y >= t - Tolerance && point.Y <= b + Tolerance;
         bool hitRight = Math.Abs(point.X - r) <= Tolerance && point.Y >= t - Tolerance && point.Y <= b + Tolerance;
         bool hitTop = Math.Abs(point.Y - t) <= Tolerance && point.X >= l - Tolerance && point.X <= r + Tolerance;
         bool hitBottom = Math.Abs(point.Y - b) <= Tolerance && point.X >= l - Tolerance && point.X <= r + Tolerance;
         
         return hitLeft || hitRight || hitTop || hitBottom;
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
