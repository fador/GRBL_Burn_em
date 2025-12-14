using System.Drawing;
using System;
using System.Drawing.Drawing2D;

namespace laser_gui_test.Data;

public enum LaserObjectType
{
    Path,
    Image,
    Rectangle,
    Group,
    Text
}

public abstract class LaserObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Object";
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
    public abstract bool HitTest(PointF point, float tolerance);
    
    public virtual RectangleF GetBounds()
    {
        return new RectangleF(Position, Size);
    }

    public abstract LaserObject Clone();
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

    public override bool HitTest(PointF point, float tolerance)
    {
        // BBox optimization
        float bBuffer = tolerance;
        float minX = Points.Min(p => p.X) - bBuffer;
        float minY = Points.Min(p => p.Y) - bBuffer;
        float maxX = Points.Max(p => p.X) + bBuffer;
        float maxY = Points.Max(p => p.Y) + bBuffer;
        
        if (point.X < minX || point.X > maxX || point.Y < minY || point.Y > maxY) 
            return false;

        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (DistanceToSegment(point, Points[i], Points[i + 1]) <= tolerance)
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

    public override LaserObject Clone()
    {
        var clone = new LaserPath
        {
            Id = Guid.NewGuid(),
            Name = this.Name + " (Copy)",
            LayerId = this.LayerId,
            IsEnabled = this.IsEnabled,
            Power = this.Power,
            Speed = this.Speed,
            Position = this.Position,
            Rotation = this.Rotation,
            Size = this.Size,
            Points = this.Points.ToList() 
        };
        return clone;
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
    
    public override bool HitTest(PointF point, float tolerance)
    {
         // Edge-only hit test for Laser Cutting (Hollow)
         float l = Position.X;
         float t = Position.Y;
         float r = l + Size.Width;
         float b = t + Size.Height;
         
         if (point.X < l - tolerance || point.X > r + tolerance || point.Y < t - tolerance || point.Y > b + tolerance) 
             return false;
             
         // Check distance to 4 lines
         bool hitLeft = Math.Abs(point.X - l) <= tolerance && point.Y >= t - tolerance && point.Y <= b + tolerance;
         bool hitRight = Math.Abs(point.X - r) <= tolerance && point.Y >= t - tolerance && point.Y <= b + tolerance;
         bool hitTop = Math.Abs(point.Y - t) <= tolerance && point.X >= l - tolerance && point.X <= r + tolerance;
         bool hitBottom = Math.Abs(point.Y - b) <= tolerance && point.X >= l - tolerance && point.X <= r + tolerance;
         
         return hitLeft || hitRight || hitTop || hitBottom;
    }

    public override LaserObject Clone()
    {
        var clone = new LaserRectangle
        {
            Id = Guid.NewGuid(),
            Name = this.Name + " (Copy)",
            LayerId = this.LayerId,
            IsEnabled = this.IsEnabled,
            Power = this.Power,
            Speed = this.Speed,
            Position = this.Position,
            Rotation = this.Rotation,
            Size = this.Size
        };
        return clone;
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
            
            // We want to draw the image UPRIGHT with Position as Bottom-Left.
            // Image (0,0) [Top] should be at Position.Y + Height.
            // Image (0,H) [Bottom] should be at Position.Y.
            
            GraphicsState state = g.Save();
            
            // Move to Top-Left of the target rect (which is Pos.Y + Height)
            // Flip Y axis so Y+ is Down (Standard Image drawing)
            g.TranslateTransform(Position.X, Position.Y + Size.Height);
            g.ScaleTransform(1, -1);
            
            // Draw standard top-down image at 0,0
            g.DrawImage(Image, 0, 0, Size.Width, Size.Height);
            
            g.Restore(state);
        }
        else
        {
            // Placeholder
             // Save state to handle rectangle? 
             // Rectangle is vector, it works fine with inverted Y (it just draws -H or +H).
             // But text "Img Missing" needs flip.
            
            using var pen = new Pen(Color.Red, 1.0f / scale);
            g.DrawRectangle(pen, Position.X, Position.Y, Size.Width, Size.Height);
            
            var state = g.Save();
            g.TranslateTransform(Position.X, Position.Y);
            g.ScaleTransform(1, -1);
            g.DrawString("Img Missing", SystemFonts.DefaultFont, Brushes.Red, 0, 0);
            g.Restore(state);
        }
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        // For Image, we generally want selection if inside
        return new RectangleF(Position, Size).Contains(point);
    }

    public override LaserObject Clone()
    {
        var clone = new LaserImage
        {
            Id = Guid.NewGuid(),
            Name = this.Name + " (Copy)",
            LayerId = this.LayerId,
            IsEnabled = this.IsEnabled,
            Power = this.Power,
            Speed = this.Speed,
            Position = this.Position,
            Rotation = this.Rotation,
            Size = this.Size,
            ImagePath = this.ImagePath
        };
        if (this.Image != null)
        {
            clone.Image = new Bitmap(this.Image);
        }
        return clone;
    }
}

public class LaserText : LaserObject
{
    public string Text { get; set; } = "Text";
    public string FontName { get; set; } = "Arial";
    public float FontSize { get; set; } = 20f; // Points

    public LaserText()
    {
        Type = LaserObjectType.Text;
        Name = "Text";
    }

    public override void Draw(Graphics g, float scale)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var brush = new SolidBrush(c);
        // Simple scale handling for font: create font of appropriate size? 
        // Or transform graphics? Graphics is already transformed. 
        // Font size is in em/points usually. 
        // If we want WYSIWYG, we need to handle size carefully.
        // For now, assume FontSize is in world units (mm or whatever).
        // Standard GDI+ Font size is in Point (1/72 inch). 
        // 1 pt = 0.3527 mm.
        // If we want FontSize 10mm ~ 28pt.
        
        // Let's assume FontSize is in POINTS for generic text.
        // But if we resize via 'Size' property we might want to scale it.
        // For simple MVP usage: DrawString uses Font Size.
        
        // Text drawing needs unflip
        // Text drawing needs unflip
        var state = g.Save();
        // Position is Bottom-Left. We want to draw text starting there, but Graphics.DrawString draws Top-Down.
        // And we have Y-Up world coordinates.
        // We need to translate to Top-Left of the text box (Position.Y + Size.Height).
        // Then flip Y to get Top-Down coordinates for DrawString.
        
        g.TranslateTransform(Position.X, Position.Y + Size.Height);
        g.ScaleTransform(1, -1);
        
        using (var font = new Font(FontName, FontSize))
        {
            g.DrawString(Text, font, brush, 0, 0); // Local 0,0 is now Top-Left of text
            
            // Measure while font is alive
            // Note: MeasureString might be affected by transform?
            // If scale is 1, -1. Width is 1 * w. Height is -1 * h?
            // Usually returns positive SizeF.
            var size = g.MeasureString(Text, font);
            this.Size = size;
        }

        g.Restore(state);
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        // Simple bounding box hit test
        return new RectangleF(Position, Size).Contains(point);
    }

    public override LaserObject Clone()
    {
        var clone = new LaserText
        {
            Id = Guid.NewGuid(),
            Name = this.Name + " (Copy)",
            LayerId = this.LayerId,
            IsEnabled = this.IsEnabled,
            Power = this.Power,
            Speed = this.Speed,
            Position = this.Position,
            Rotation = this.Rotation,
            Size = this.Size,
            Text = this.Text,
            FontName = this.FontName,
            FontSize = this.FontSize
        };
        return clone;
    }
}
