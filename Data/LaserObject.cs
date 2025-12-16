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
    Text,
    Circle,
    Bezier
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

// ... (Existing classes: LaserPath, LaserRectangle, etc.) ...

public class LaserCircle : LaserObject
{
    public LaserCircle()
    {
        Type = LaserObjectType.Circle;
        Name = "Circle";
        Size = new SizeF(50, 50); // Default size
    }

    public override void Draw(Graphics g, float scale)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var pen = new Pen(c, 1.0f / scale);
        g.DrawEllipse(pen, Position.X, Position.Y, Size.Width, Size.Height);
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        // Hit test for the outline of the ellipse
        // Simplified: Check if point is close to the ellipse equation
        
        float h = Position.X + Size.Width / 2f;
        float k = Position.Y + Size.Height / 2f;
        float a = Size.Width / 2f;
        float b = Size.Height / 2f;

        if (a <= 0 || b <= 0) return false;

        // Normalize point relative to center
        float x = point.X - h;
        float y = point.Y - k;

        // Ellipse equation: (x/a)^2 + (y/b)^2 = 1
        float val = (x * x) / (a * a) + (y * y) / (b * b);
        
        // We want to check if val is close to 1
        // How close? tolerance related.
        // It's non-linear.
        // Better: Check if inside outer boundary (radius+tol) AND outside inner boundary (radius-tol)
        
        // Let's use GraphicsPath for robust checking? Expensive?
        // Let's implement bounding box check first
        if (!GetBounds().Contains(point)) return false; // Optimization

        // Approximate with ring
        // Transform point to unit circle space?
        // x' = x/a, y' = y/b. dist = sqrt(x'^2 + y'^2). should be approx 1.
        // But tolerance is in world units, not unit space.
        
        // Simple approximation: Closest point on ellipse is hard.
        // Let's rely on loose check or "Contains" if filled? But it's hollow.
        // Use GraphicsPath IsOutlineVisible
        using var path = new GraphicsPath();
        path.AddEllipse(Position.X, Position.Y, Size.Width, Size.Height);
        using var pen = new Pen(Color.Black, tolerance);
        return path.IsOutlineVisible(point, pen);
    }

    public override LaserObject Clone()
    {
         var clone = new LaserCircle
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
    public Guid MaskId { get; set; } = Guid.Empty;

    public LaserImage()
    {
        Type = LaserObjectType.Image;
        Name = "Image";
    }

    public override void Draw(Graphics g, float scale)
    {
        if (Image != null)
        {
            GraphicsState state = g.Save();

            // Apply Mask if present
            if (MaskId != Guid.Empty)
            {
                var maskObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == MaskId);
                if (maskObj != null)
                {
                    GraphicsPath? clipPath = null;
                    if (maskObj is LaserCircle c)
                    {
                        clipPath = new GraphicsPath();
                        clipPath.AddEllipse(c.Position.X, c.Position.Y, c.Size.Width, c.Size.Height);
                    }
                    else if (maskObj is LaserRectangle r)
                    {
                        clipPath = new GraphicsPath();
                        clipPath.AddRectangle(new RectangleF(r.Position, r.Size));
                    }
                    
                    if (clipPath != null)
                    {
                        g.SetClip(clipPath); 
                        // Note: clipPath needs disposal?
                        // Yes. But we can't dispose it immediately if SetClip uses it?
                        // SetClip clones it? documentation says "Sets the clipping region... to the property of the specified GraphicsPath".
                        // Usually SetClip copies.
                    }
                }
            }

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
    public Guid PathId { get; set; } = Guid.Empty;
    public float PathOffset { get; set; } = 0f;
    public float VerticalOffset { get; set; } = 0f;
    public bool ReversePath { get; set; } = false;

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
        var state = g.Save();
        
        if (PathId != Guid.Empty)
        {
            var pathObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == PathId);
            if (pathObj != null)
            {
                 // Create Text Path at 0,0 locally
                 using (var gp = new GraphicsPath())
                 {
                     var family = new FontFamily(FontName);
                     // Conversion: FontSize (Points) -> EmSize (World)
                     // Assuming display is roughly 96 DPI for "Points" meaning...
                     // 1 Pt = 1/72 inch.
                     // EmSize in AddString is in World Coordinates.
                     // If 1 World Unit = 1 mm.
                     // 1 Pt = 0.35277 mm.
                     float emSize = FontSize * 0.35277f;
                     // But previously we used Graphics.DrawString which takes Points.
                     // We need to match visual size.
                     // Graphics.DrawString(..., 20pt) draws 20pt text.
                     // If World Scale is 1mm.
                     // We probably want to keep consistency.
                     // Let's stick to the multiplier used in GrblGenerator if possible,
                     // or derive it.
                     // 20pt at 96dpi = 20 * 96/72 pixels = 26.6 pixels.
                     // If 1 pixel = 1mm (unlikely, usually 1px = screen px).
                     // But our View is Scalable.
                     // Let's assume EmSize ~ FontSize for now, or tune it.
                     // Actually, Font(Size) uses Points. AddString uses EmHeight in World Units.
                     // We need conversion factor.
                     // Let's use 1.0 for now, user can resize.
                     
                     // Use the same scale as GrblGenerator logic: 96/72?
                     emSize = FontSize * 96f / 72f; 

                     gp.AddString(Text, family, (int)FontStyle.Regular, emSize, new PointF(0, 0), StringFormat.GenericDefault);
                     
                     // Fix Orientation: AddString is Top-Down. World is Y-Up.
                     // We want Baseline to be on the curve (Y=0 locally).
                     // AddString draws from Top (Y=0) downwards.
                     // Calculate Ascent to find Baseline.
                     float ascent = family.GetCellAscent((int)FontStyle.Regular) * emSize / family.GetEmHeight((int)FontStyle.Regular);
                     
                     using (var m = new System.Drawing.Drawing2D.Matrix())
                     {
                         // 1. Move Baseline to Y=0 (Downwards Y means Baseline is at +Ascent)
                         // Also apply VerticalOffset here (Y axis)
                         m.Translate(0, -ascent + VerticalOffset);
                         // 2. Flip Y so Up is Positive
                         m.Scale(1, -1);
                         
                         // 3. Apply Object Rotation (around Origin 0,0?)
                         // Text Origin is Top-Left of text box (now transformed to Baseline-Start).
                         // User expects Rotation around Center usually?
                         // But for Path Text, rotating around Start seems safer to just flip direction.
                         // But if they rotate 180, they might want to keep position?
                         // Let's just apply Rotation.
                         m.Rotate(Rotation);
                         
                         gp.Transform(m);
                     }
                     
                     // Get backbone
                     var backbone = PathWarp.FlattenPath(pathObj);
                     if (backbone.Count > 1)
                     {
                         // Reverse if requested
                         if (ReversePath)
                         {
                             backbone.Reverse();
                         }

                         using (var warped = PathWarp.CreateWarpedPath(gp, backbone, PathOffset))
                         {
                             // Warped path is in World Coordinates (derived from backbone).
                             // So we draw it directly.
                             using (var p = new Pen(c, 1.0f / scale))
                             {
                                 // Fill?
                                 if (scale > 0.5f) g.FillPath(brush, warped);
                                 // And/Or Outline?
                                // g.DrawPath(p, warped);
                             }
                         }
                     }
                 }
                 g.Restore(state);
                 return;
            }
        }

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
            // We could optionally verify Bounds but changing Size here breaks manual resize.
            // var size = g.MeasureString(Text, font);
            // this.Size = size;
        }

        g.Restore(state);
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        // Simple bounding box hit test
        // WARN: If warped, Position/Size might not be accurate!
        // We should update Position/Size when attaching?
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
            FontSize = this.FontSize,
            PathId = this.PathId,
            PathOffset = this.PathOffset
        };
        return clone;
    }
}

public class LaserBezier : LaserObject
{
    // Points structure:
    // P0 (Start)
    // P1 (Control 1.1)
    // P2 (Control 1.2)
    // P3 (End 1 / Start 2)
    // P4 (Control 2.1) ...
    // Count should be 3*N + 1
    public List<PointF> Points { get; set; } = new List<PointF>();

    public LaserBezier()
    {
        Type = LaserObjectType.Bezier;
        Name = "Bezier";
    }

    public void UpdateBounds()
    {
        if (Points.Count == 0) return;
        
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        
        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        
        Position = new PointF(minX, minY);
        Size = new SizeF(maxX - minX, maxY - minY);
    }

    public override void Draw(Graphics g, float scale)
    {
        if (Points.Count < 4) return;

        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var pen = new Pen(c, 1.0f / scale);
        // Valid Bezier sequence: 4, 7, 10... points
        // Graphics.DrawBeziers requires array of 3*k + 1 points
        int count = Points.Count;
        int validCount = count - (count - 1) % 3;
        if (validCount < 4) return;
        
        g.DrawBeziers(pen, Points.Take(validCount).ToArray());
    }

    public override bool HitTest(PointF point, float tolerance)
    {
         if (Points.Count < 4) return false;
         int count = Points.Count;
         int validCount = count - (count - 1) % 3;
         using var path = new GraphicsPath();
         path.AddBeziers(Points.Take(validCount).ToArray());
         using var pen = new Pen(Color.Black, tolerance);
         return path.IsOutlineVisible(point, pen);
    }

    public override LaserObject Clone()
    {
        return new LaserBezier
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
            Points = new List<PointF>(this.Points)
        };
    }
}
