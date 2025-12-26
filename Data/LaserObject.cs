/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Drawing;
using System;
using System.Text.Json.Serialization;
using System.Runtime.Versioning;
using System.Drawing.Drawing2D;

namespace grbl_burn_em.Data;

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

public enum TextWarpMethod
{
    Stretch,
    Align
}

public enum TextAnchor
{
    Start,
    Middle,
    End
}

public abstract class LaserObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Object";
    public Guid LayerId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public float? Power { get; set; } = null; // null = use layer settings
    public float? Speed { get; set; } = null; // null = use layer settings
    public LayerMode? Mode { get; set; } = null; // null = use layer settings
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

    public virtual void UpdateBounds() { }

    public abstract LaserObject Clone();

    protected RectangleF GetRotatedBoundsFromDef()
    {
        if (Rotation == 0) return new RectangleF(Position, Size);

        var cx = Position.X + Size.Width / 2f;
        var cy = Position.Y + Size.Height / 2f;

        var corners = new PointF[]
        {
            new PointF(Position.X, Position.Y),
            new PointF(Position.X + Size.Width, Position.Y),
            new PointF(Position.X + Size.Width, Position.Y + Size.Height),
            new PointF(Position.X, Position.Y + Size.Height)
        };

        using (var m = new System.Drawing.Drawing2D.Matrix())
        {
            m.RotateAt(Rotation, new PointF(cx, cy));
            m.TransformPoints(corners);
        }

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach(var p in corners)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
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

    public override RectangleF GetBounds()
    {
        return GetRotatedBoundsFromDef();
    }

    public override void Draw(Graphics g, float scale)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var pen = new Pen(c, 1.0f / scale);
        
        var state = g.Save();
        // Rotate around center
        float cx = Position.X + Size.Width / 2f;
        float cy = Position.Y + Size.Height / 2f;
        g.TranslateTransform(cx, cy);
        g.RotateTransform(Rotation);
        g.TranslateTransform(-cx, -cy);
        
        g.DrawEllipse(pen, Position.X, Position.Y, Size.Width, Size.Height);
        g.Restore(state);
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
            Mode = this.Mode,
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

    public override void UpdateBounds()
    {
        if (Points.Count == 0) return;
        float minX = Points.Min(p => p.X);
        float minY = Points.Min(p => p.Y);
        float maxX = Points.Max(p => p.X);
        float maxY = Points.Max(p => p.Y);
        Position = new PointF(minX, minY);
        Size = new SizeF(maxX - minX, maxY - minY);
    }

    public override RectangleF GetBounds()
    {
        return GetRotatedBoundsFromDef();
    }

    public override void Draw(Graphics g, float scale)
    {
        if (Points.Count < 2) return;
        
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var pen = new Pen(c, 1.0f / scale); // Constant width regardless of zoom
        
        var state = g.Save();
        float cx = Position.X + Size.Width / 2f;
        float cy = Position.Y + Size.Height / 2f;
        g.TranslateTransform(cx, cy);
        g.RotateTransform(Rotation);
        g.TranslateTransform(-cx, -cy);
        
        g.DrawLines(pen, Points.ToArray());
        g.Restore(state);
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        PointF testPoint = point;
        if (Rotation != 0)
        {
            float cx = Position.X + Size.Width / 2f;
            float cy = Position.Y + Size.Height / 2f;
            using (var m = new Matrix())
            {
                m.RotateAt(-Rotation, new PointF(cx, cy));
                var pts = new PointF[] { point };
                m.TransformPoints(pts);
                testPoint = pts[0];
            }
        }

        // BBox optimization (Unrotated)
        float bBuffer = tolerance;
        if (testPoint.X < Position.X - bBuffer || testPoint.X > Position.X + Size.Width + bBuffer || 
            testPoint.Y < Position.Y - bBuffer || testPoint.Y > Position.Y + Size.Height + bBuffer) 
            return false;

        for (int i = 0; i < Points.Count - 1; i++)
        {
            if (DistanceToSegment(testPoint, Points[i], Points[i + 1]) <= tolerance)
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
            Mode = this.Mode,
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

    public override RectangleF GetBounds()
    {
        return GetRotatedBoundsFromDef();
    }

    public override void Draw(Graphics g, float scale)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var pen = new Pen(c, 1.0f / scale);
        
        var state = g.Save();
        float cx = Position.X + Size.Width / 2f;
        float cy = Position.Y + Size.Height / 2f;
        g.TranslateTransform(cx, cy);
        g.RotateTransform(Rotation);
        g.TranslateTransform(-cx, -cy);
        
        g.DrawRectangle(pen, Position.X, Position.Y, Size.Width, Size.Height);
        g.Restore(state);
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
            Mode = this.Mode,
            Position = this.Position,
            Rotation = this.Rotation,
            Size = this.Size
        };
        return clone;
    }
}

public class LaserImage : LaserObject, IDisposable
{
    // We shouldn't serialize Bitmap directly usually, but for GUI it's needed
    // In a real app we'd store the path or byte array
    
    [JsonIgnore]
    public Bitmap? Image { get; set; }
    public string ImagePath { get; set; } = "";
    public Guid MaskId { get; set; } = Guid.Empty;

    public LaserImage()
    {
        Type = LaserObjectType.Image;
        Name = "Image";
    }

    public void Dispose()
    {
        Image?.Dispose();
    }

    public override RectangleF GetBounds()
    {
        return GetRotatedBoundsFromDef();
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
                    }
                }
            }

            // Move to Top-Left of the target rect (which is Pos.Y + Height)
            // Flip Y axis so Y+ is Down (Standard Image drawing)
            
            // Rotation Handling:
            // We want to rotate around the center of the image.
            // Center in World Coords:
            float cx = Position.X + Size.Width / 2f;
            float cy = Position.Y + Size.Height / 2f;

            // 1. Translate to Center
            g.TranslateTransform(cx, cy);
            // 2. Rotate
            g.RotateTransform(Rotation);
            // 3. Scale/Flip (Y+ Down) - Note: This flips the LOCAL axis
            g.ScaleTransform(1, -1);
            // 4. Draw Image centered at 0,0 (Extent is -W/2 to W/2)
            g.DrawImage(Image, -Size.Width/2f, -Size.Height/2f, Size.Width, Size.Height);
            
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
            Mode = this.Mode,
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
    public bool UpsideDown { get; set; } = false;
    public TextWarpMethod WarpMethod { get; set; } = TextWarpMethod.Align;
    public FontStyle FontStyle { get; set; } = FontStyle.Regular;
    public TextAnchor Anchor { get; set; } = TextAnchor.Start;

    public LaserText()
    {
        Type = LaserObjectType.Text;
        Name = "Text";
    }

    public void UpdateTextSize()
    {
        if (PathId != Guid.Empty)
        {
            UpdateWarpedBounds();
            return;
        }

        using (var gp = new GraphicsPath())
        using (var family = new FontFamily(FontName))
        {
            float emSize = FontSize;
            gp.AddString(Text, family, (int)FontStyle, emSize, new PointF(0, 0), StringFormat.GenericTypographic);
            var b = gp.GetBounds();
            
            // Width: Use the maximum extent.
            // Height: Use FontSize (Em-Height) to maintain consistent baseline alignment.
            // But for accurate bounds, we should use the actual bounding box height or the font metrics.
            // Let's use the bounding box width and the emSize for height.
            // However, SvgImporter used G.MeasureString which includes padding.
            // AddString + GetBounds is tighter and better for laser.
            Size = new SizeF(b.Width, emSize);
        }
    }

    public void UpdateWarpedBounds()
    {
        if (PathId == Guid.Empty) return;
        var pathObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == PathId);
        if (pathObj == null) return;

        var backbonePointsList = PathWarp.FlattenPath(pathObj);
        if (backbonePointsList.Count < 2) return;

        var family = new FontFamily(FontName);
        float emSize = FontSize;

        using var gp = new GraphicsPath();
        gp.AddString(Text, family, (int)FontStyle, emSize, new PointF(0, 0), StringFormat.GenericTypographic);
        
        float cellAscent = family.GetCellAscent(FontStyle);
        float emHeight = family.GetEmHeight(FontStyle);
        float baselineY = emSize * cellAscent / emHeight;

        using (var m = new Matrix())
        {
            m.Translate(0, -baselineY - VerticalOffset);
            m.Scale(1, -1, MatrixOrder.Append);
            if (UpsideDown) m.Scale(1, -1, MatrixOrder.Append);
            m.Rotate(Rotation, MatrixOrder.Append);
            gp.Transform(m);
        }

        var effectiveBackbone = new List<PointF>(backbonePointsList);
        if (ReversePath) effectiveBackbone.Reverse();

        RectangleF bounds;
        if (WarpMethod == TextWarpMethod.Stretch)
        {
            using var warped = PathWarp.CreateWarpedPath(gp, effectiveBackbone, PathOffset);
            bounds = warped.GetBounds();
        }
        else // Align
        {
            // Simulate alignment layout
            PathWarp.ComputeBackboneProperties(effectiveBackbone, out var lengths, out var normals);
            float totalPathLen = lengths.Last();
            
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool hasPoints = false;

            using (var tmpBmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(tmpBmp))
            using (var f = new Font(family, emSize, FontStyle, GraphicsUnit.World))
            {
                var sf = StringFormat.GenericTypographic;
                
                // Calculate total width first for Anchor alignment
                float totalWidth = 0;
                var charAdvances = new List<float>();
                foreach (char ch in Text)
                {
                    if (char.IsControl(ch)) 
                    {
                        charAdvances.Add(0); 
                        continue;
                    }
                    string s = ch.ToString();
                    float adv = g.MeasureString(s, f, 1000, sf).Width;
                     if (adv <= 0) adv = emSize * 0.3f;
                    charAdvances.Add(adv);
                    totalWidth += adv;
                }

                float curX = 0;
                if (Anchor == TextAnchor.Middle) curX = -totalWidth / 2f;
                else if (Anchor == TextAnchor.End) curX = -totalWidth;

                int charIndex = 0;
                foreach (char ch in Text)
                {
                    float advance = charAdvances[charIndex++];
                    if (char.IsControl(ch)) continue;
                    
                    if (char.IsWhiteSpace(ch))
                    {
                        curX += advance;
                        continue;
                    }

                    string s = ch.ToString();
                    using (var charPath = new GraphicsPath())
                    {
                        charPath.AddString(s, family, (int)FontStyle, emSize, new PointF(0, 0), sf);
                        
                        float charMidX = curX + advance / 2f;
                        float targetDist = charMidX + PathOffset;
                        
                        // Handle loop wrap? usually textPath doesn't wrap unless requested, implies closed loop
                        if (totalPathLen > 0.001f) 
                             targetDist = ((targetDist % totalPathLen) + totalPathLen) % totalPathLen;
                        
                        PathWarp.GetPointAndNormalAt(targetDist, effectiveBackbone, lengths, normals, out PointF origin, out PointF normal);

                        using (var mChar = new Matrix())
                        {
                            mChar.Translate(-(advance / 2f), -baselineY);
                            mChar.Scale(1, -1, MatrixOrder.Append);
                            float rotAngle = (float)(Math.Atan2(-normal.X, normal.Y) * 180 / Math.PI);
                            mChar.Rotate(rotAngle, MatrixOrder.Append);
                            PointF finalPos = new PointF(origin.X + normal.X * VerticalOffset, origin.Y + normal.Y * VerticalOffset);
                            mChar.Translate(finalPos.X, finalPos.Y, MatrixOrder.Append);
                            charPath.Transform(mChar);

                            var cb = charPath.GetBounds();
                            if (cb.Left < minX) minX = cb.Left; if (cb.Top < minY) minY = cb.Top;
                            if (cb.Right > maxX) maxX = cb.Right; if (cb.Bottom > maxY) maxY = cb.Bottom;
                            hasPoints = true;
                        }
                    }
                    curX += advance;
                }
            }
            bounds = hasPoints ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : RectangleF.Empty;
        }

        if (bounds != RectangleF.Empty)
        {
            Position = bounds.Location;
            Size = bounds.Size;
        }
    }

    // Re-calculated on property change?
    // Optimization is possible but let's stick to functional first.
    public GraphicsPath GetPath()
    {
         if (PathId != Guid.Empty)
         {
            var pathObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == PathId);
            if (pathObj != null)
            {
                 // Create Text Path at 0,0 locally
                 var gp = new GraphicsPath();
                 
                     var family = new FontFamily(FontName);
                     float emSize = FontSize; 

                     var sf = StringFormat.GenericTypographic;
                     gp.AddString(Text, family, (int)FontStyle, emSize, new PointF(0, 0), sf);
                     
                     // Fix Orientation: AddString is Top-Down. World is Y-Up.
                     float emHeight = family.GetEmHeight(FontStyle);
                     float cellAscent = family.GetCellAscent(FontStyle);
                     
                     // Convert to World Units
                     float baselineY = (emSize * cellAscent) / emHeight;
                     
                     using (var m = new System.Drawing.Drawing2D.Matrix())
                     {
                         float finalYShift = -baselineY - VerticalOffset; 
                         m.Translate(0, finalYShift);
                         m.Scale(1, -1);
                         if (UpsideDown) m.Scale(1, -1);
                         m.Rotate(Rotation);
                         gp.Transform(m);
                     }
                     
                     // Get backbone
                     var backbonePointsList = PathWarp.FlattenPath(pathObj);
                     if (backbonePointsList.Count > 1)
                     {
                         // Create a local copy to avoid corrupting shared backbone
                         var effectiveBackbone = new List<PointF>(backbonePointsList);
                         if (ReversePath) effectiveBackbone.Reverse();

                         if (WarpMethod == TextWarpMethod.Stretch)
                         {
                             var warped = PathWarp.CreateWarpedPath(gp, effectiveBackbone, PathOffset);
                             gp.Dispose();
                             return warped;
                         }
                         else // Align
                         {
                             gp.Dispose();
                             gp = new GraphicsPath();

                             PathWarp.ComputeBackboneProperties(effectiveBackbone, out var lengths, out var normals);
                             
                             float totalPathLen = lengths.Last();

                             using (var tmpBmp = new Bitmap(1, 1))
                             using (var gCtx = Graphics.FromImage(tmpBmp))
                             using (var f = new Font(family, emSize, FontStyle, GraphicsUnit.World))
                             {

                                 
                                 // Calculate total width first for Anchor alignment
                                 float totalWidth = 0;
                                 var charAdvances = new List<float>(); // Store advances to avoid re-measuring
                                 foreach (char ch in Text)
                                 {
                                     if (char.IsControl(ch)) 
                                     {
                                         charAdvances.Add(0);
                                         continue;
                                     }
                                     string s = ch.ToString();
                                     float adv = gCtx.MeasureString(s, f, 1000, sf).Width;
                                     if (adv <= 0) adv = emSize * 0.3f;
                                     charAdvances.Add(adv);
                                     totalWidth += adv;
                                 }
                                 
                                 float curX = 0;
                                 if (Anchor == TextAnchor.Middle) curX = -totalWidth / 2f;
                                 else if (Anchor == TextAnchor.End) curX = -totalWidth;

                                 int charIndex = 0;
                                 foreach (char ch in Text)
                                 {
                                     float advance = charAdvances[charIndex++];
                                     if (char.IsControl(ch)) continue; 
                                     
                                     string s = ch.ToString();

                                     using (var charPath = new GraphicsPath())
                                     {
                                         charPath.AddString(s, family, (int)FontStyle, emSize, new PointF(0, 0), sf);
                                         
                                         if (char.IsWhiteSpace(ch))
                                         {
                                              curX += advance; 
                                              continue;
                                         }

                                         float charMidX = curX + advance / 2f;
                                         float targetDist = charMidX + PathOffset;
                                         if (totalPathLen > 0.001f)
                                             targetDist = ((targetDist % totalPathLen) + totalPathLen) % totalPathLen;
                                         
                                         PathWarp.GetPointAndNormalAt(targetDist, effectiveBackbone, lengths, normals, out PointF origin, out PointF normal);
                                         
                                         using(var mChar = new Matrix())
                                         {
                                             mChar.Translate(-(advance / 2f), -baselineY); 
                                             mChar.Scale(1, -1, MatrixOrder.Append);
                                             float rotAngle = (float)(Math.Atan2(-normal.X, normal.Y) * 180 / Math.PI);
                                             mChar.Rotate(rotAngle, MatrixOrder.Append);
                                             PointF finalPos = new PointF(origin.X + normal.X * VerticalOffset, origin.Y + normal.Y * VerticalOffset);
                                             mChar.Translate(finalPos.X, finalPos.Y, MatrixOrder.Append);
                                             charPath.Transform(mChar);
                                         } 
                                         
                                         gp.AddPath(charPath, false);
                                         curX += advance;
                                     } // End Using CharPath
                                 } // End Foreach
                             } // End Using Font/Graphics
                             return gp;
                         }
                     }
                     return gp; // Fallback if backbone invalid
            }
         }

         // Unwarped
         var gpNormal = new GraphicsPath();
         FontFamily fontFamily;
         try 
         {
             fontFamily = new FontFamily(FontName);
         }
         catch
         {
             // Fallback
             fontFamily = FontFamily.GenericSansSerif;
         }

         using (fontFamily)
         {
            float emSize = FontSize;
            gpNormal.AddString(Text, fontFamily, (int)FontStyle, emSize, new PointF(0, 0), StringFormat.GenericTypographic);

            using (var m = new Matrix())
            {
                // Align text based on Anchor
                float offsetX = 0;
                if (Anchor == TextAnchor.Middle) offsetX = -Size.Width / 2f;
                else if (Anchor == TextAnchor.End) offsetX = -Size.Width;

                m.Translate(offsetX, 0);
                m.Scale(1, -1, MatrixOrder.Append); // Flip Y because GraphicsPath.AddString is Y-down
                if (Rotation != 0) m.Rotate(Rotation, MatrixOrder.Append);
                m.Translate(Position.X, Position.Y, MatrixOrder.Append);
                
                gpNormal.Transform(m);
            }
        }
        return gpNormal;
    }

    public override RectangleF GetBounds()
    {
        if (Rotation == 0)
        {
            float offsetX = 0;
            if (Anchor == TextAnchor.Middle) offsetX = -Size.Width / 2f;
            else if (Anchor == TextAnchor.End) offsetX = -Size.Width;
            return new RectangleF(Position.X + offsetX, Position.Y - Size.Height, Size.Width, Size.Height);
        }

        using (var gp = GetPath())
        {
            return gp.GetBounds();
        }
    }

    public override void Draw(Graphics g, float scale)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        Color c = layer?.Color ?? Color.Black;

        using var brush = new SolidBrush(c);
        
        // Use central GetPath
        using var path = GetPath();
        if (scale > 0.5f) 
        {
            g.FillPath(brush, path);
        }
        else
        {
            // Optimization for zoomed out??
            g.FillPath(brush, path);
        }
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
            FontStyle = this.FontStyle,
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

    public override RectangleF GetBounds()
    {
        // Ensure Position/Size are up to date?
        // UpdateBounds() should be called when points change.
        // Assuming Position/Size defines the Unrotated AABB.
        return GetRotatedBoundsFromDef();
    }

    public override void UpdateBounds()
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
        
        var state = g.Save();
        float cx = Position.X + Size.Width / 2f;
        float cy = Position.Y + Size.Height / 2f;
        g.TranslateTransform(cx, cy);
        g.RotateTransform(Rotation);
        g.TranslateTransform(-cx, -cy);
        
        g.DrawBeziers(pen, Points.Take(validCount).ToArray());
        g.Restore(state);
    }

    public override bool HitTest(PointF point, float tolerance)
    {
         if (Points.Count < 4) return false;
         
         PointF testPoint = point;
         if (Rotation != 0)
         {
             float cx = Position.X + Size.Width / 2f;
             float cy = Position.Y + Size.Height / 2f;
             using (var m = new Matrix())
             {
                 m.RotateAt(-Rotation, new PointF(cx, cy));
                 var pts = new PointF[] { point };
                 m.TransformPoints(pts);
                 testPoint = pts[0];
             }
         }

         int count = Points.Count;
         int validCount = count - (count - 1) % 3;
         using var path = new GraphicsPath();
         path.AddBeziers(Points.Take(validCount).ToArray());
         using var pen = new Pen(Color.Black, tolerance);
         return path.IsOutlineVisible(testPoint, pen);
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


