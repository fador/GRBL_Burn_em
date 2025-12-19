using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;

namespace laser_gui_test.Data.Generators;

public class GrblGenerator : IGCodeGenerator
{
    public string Name => "Grbl";

    public IEnumerable<string> Generate(IEnumerable<LaserObject> objects)
    {
        // Startup
        yield return "G21"; // Metric
        yield return "G90"; // Absolute positioning
        yield return "M4 S0"; // Dynamic laser mode, Laser Off

        foreach (var obj in objects)
        {
            if (!obj.IsEnabled) continue;
            
            foreach (var line in GenerateObject(obj))
            {
                yield return line;
            }
        }

        // Shutdown
        yield return "M5"; // Laser Off
        yield return "G0 X0 Y0"; // Return to home (optional, but good for preview)
    }

    private IEnumerable<string> GenerateObject(LaserObject obj)
    {
        if (obj is LaserGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var line in GenerateObject(child)) yield return line;
            }
            yield break;
        }

        // Get Layer Settings
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        
        float pwrPercent = layer?.Power ?? obj.Power;
        float speedVal = layer?.Speed ?? obj.Speed;
        LayerMode mode = layer?.Mode ?? LayerMode.Cut;

        // Force Fill for Images (they are always raster) works naturally
        // But if user sets Image layer to "Cut", what happens? Images can't be cut effectively without vectorizing.
        // We will treat Images as Raster always, but use Layer Speed/Power.

        float sVal = pwrPercent * 10f; // 0-100 -> 0-1000
        float fVal = speedVal;

        // If Mode is CUT, generate Vector GCode (unless Image)
        if (mode == LayerMode.Cut && !(obj is LaserImage))
        {
            if (obj is LaserText text)
            {
                // Generate Vector Path for Text
                using (var gp = new GraphicsPath())
                {
                    var family = new FontFamily(text.FontName);
                    // Use FontSize directly to match UI
                    float emSize = text.FontSize; 
                    
                    int style = (int)text.FontStyle;
                    gp.AddString(text.Text, family, style, emSize, new PointF(0, 0), StringFormat.GenericTypographic);

                    // Warp if needed
                    GraphicsPath workPath = gp;
                    GraphicsPath? warpedPath = null;
                    bool checkWarp = false;
                    
                    if (text.PathId != Guid.Empty)
                    {
                        var pathObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == text.PathId);
                        if (pathObj != null)
                        {
                            var backbone = PathWarp.FlattenPath(pathObj);
                            if (backbone.Count > 1)
                            {
                                if (text.ReversePath)
                                {
                                    backbone.Reverse();
                                }

                                // Fix Orientation LOCALLY for warping
                                // We need a clone to not mess up the original gp for fallback
                                using (var warpInput = (GraphicsPath)gp.Clone())
                                {
                                    float emHeight = family.GetEmHeight(text.FontStyle);
                                    float cellAscent = family.GetCellAscent(text.FontStyle);
                                    float cellDescent = family.GetCellDescent(text.FontStyle);
                                    
                                    float ascent = (emSize * cellAscent) / emHeight;
                                    float descent = (emSize * cellDescent) / emHeight;

                                     using (var m = new System.Drawing.Drawing2D.Matrix())
                                     {
                                         // Match LaserText.Draw logic for baseline alignment
                                         float baselineY = (emSize * cellAscent) / emHeight;
                                         float finalYShift = -baselineY - text.VerticalOffset;
 
                                         m.Translate(0, finalYShift);
                                         m.Scale(1, -1);

                                        if (text.UpsideDown)
                                        {
                                            m.Scale(1, -1);
                                        }

                                        m.Rotate(text.Rotation);
                                        warpInput.Transform(m);
                                    }
                                    
                                    warpedPath = PathWarp.CreateWarpedPath(warpInput, backbone, text.PathOffset);
                                    workPath = warpedPath;
                                    checkWarp = true;
                                }
                            }
                        }
                    }
                    
                    // Transform if NOT warped (Placement logic)
                    // If warped, position is defined by the backbone + offest.
                  if (!checkWarp)
                  {
                       using (var matrix = new System.Drawing.Drawing2D.Matrix())
                       {
                             // Match LaserText.Draw logic exactly
                             matrix.Translate(-text.Size.Width / 2f, text.Size.Height / 2f);
                             matrix.Scale(1, -1, MatrixOrder.Append);
                             if (text.Rotation != 0) matrix.Rotate(text.Rotation, MatrixOrder.Append);
                             matrix.Translate(text.Position.X + text.Size.Width / 2f, text.Position.Y + text.Size.Height / 2f, MatrixOrder.Append);
 
                             workPath.Transform(matrix);
                       }
                  }
                    
                    workPath.Flatten(null, 0.05f); // 0.05mm precision
                    
                    if (workPath.PointCount > 0)
                    {
                         PointF[] points = workPath.PathPoints;
                         byte[] types = workPath.PathTypes;
                         PointF lastPos = new PointF(float.NaN, float.NaN);
                         PointF subpathStart = new PointF(0,0);

                         for (int i = 0; i < points.Length; i++)
                         {
                             var p = points[i];
                             byte type = types[i];
                             byte typeMasked = (byte)(type & 0x07);
                             
                             bool isStart = (typeMasked == 0);
                             
                             if (isStart && !float.IsNaN(lastPos.X))
                             {
                                 float dist = Math.Abs(p.X - lastPos.X) + Math.Abs(p.Y - lastPos.Y);
                                 if (dist < 0.001f)
                                 {
                                     isStart = false; 
                                     subpathStart = p;
                                 }
                             }
                             
                             if (isStart) 
                             {
                                 subpathStart = p;
                                 yield return "G1 S0"; 
                                 yield return $"G0 X{p.X:F3} Y{p.Y:F3}";
                                 yield return $"G1 F{fVal:F0}"; 
                             }
                             else 
                             {
                                 yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
                             }
                             
                             lastPos = p;

                             if ((type & 0x80) != 0) // CloseSubpath
                             {
                                 float dist = Math.Abs(p.X - subpathStart.X) + Math.Abs(p.Y - subpathStart.Y);
                                 if (dist > 0.001f)
                                 {
                                     yield return $"G1 X{subpathStart.X:F3} Y{subpathStart.Y:F3} S{sVal:F0}";
                                     lastPos = subpathStart;
                                 }
                             }
                         }
                    }
                    
                    warpedPath?.Dispose();
                }
                yield return "G1 S0";
                yield break;
            }
            else if (obj is LaserCircle circle)
            {
                 // Linearize Ellipse/Circle
                 using (var path = new GraphicsPath())
                 {
                     path.AddEllipse(circle.Position.X, circle.Position.Y, circle.Size.Width, circle.Size.Height);
                     
                     // Rotate around center
                     float cx = circle.Position.X + circle.Size.Width / 2f;
                     float cy = circle.Position.Y + circle.Size.Height / 2f;
                     
                     using (var m = new System.Drawing.Drawing2D.Matrix())
                     {
                         m.RotateAt(circle.Rotation, new PointF(cx, cy));
                         path.Transform(m);
                     }

                     path.Flatten(null, 0.05f);
                     PointF[] points = path.PathPoints;
                     
                     if (points.Length > 0)
                     {
                        yield return "G1 S0";
                        yield return $"G0 X{points[0].X:F3} Y{points[0].Y:F3}";
                        yield return $"G1 F{fVal:F0}"; // G1 to apply speed? Start Logic.
                        // Actually Start usually means move to start.
                        // G1 Sxxx is Power.
                        
                        // First point is Move.
                        for(int i=1; i<points.Length; i++)
                        {
                            yield return $"G1 X{points[i].X:F3} Y{points[i].Y:F3} S{sVal:F0}";
                        }
                        // Close loop
                        yield return $"G1 X{points[0].X:F3} Y{points[0].Y:F3} S{sVal:F0}";
                     }
                 }
                 yield return "G1 S0";
                 yield break;
            }
            else if (obj is LaserRectangle rect)
            {
                // Rectangle with optional Rotation
                // We can use GraphicsPath for easy rotation
                 using (var path = new GraphicsPath())
                 {
                     path.AddRectangle(new RectangleF(rect.Position, rect.Size));
                     
                     float cx = rect.Position.X + rect.Size.Width / 2f;
                     float cy = rect.Position.Y + rect.Size.Height / 2f;
                     
                     using (var m = new System.Drawing.Drawing2D.Matrix())
                     {
                         m.RotateAt(rect.Rotation, new PointF(cx, cy));
                         path.Transform(m);
                     }
                     
                     // Rectangle is 4 points (closed). Flattening might add more if we warped (we didn't).
                     // But AddRectangle adds 4 points.
                     // Flattening ensures it's lines.
                     path.Flatten(null, 0.05f); // Overkill for rect but safe
                     
                     PointF[] points = path.PathPoints;
                     if (points.Length > 0)
                     {
                        yield return "G1 S0";
                        yield return $"G0 X{points[0].X:F3} Y{points[0].Y:F3}";
                        yield return $"G1 F{fVal:F0}"; 
                        
                        for(int i=1; i<points.Length; i++)
                        {
                            yield return $"G1 X{points[i].X:F3} Y{points[i].Y:F3} S{sVal:F0}";
                        }
                        // Close loop
                        yield return $"G1 X{points[0].X:F3} Y{points[0].Y:F3} S{sVal:F0}";
                     }
                 }
                 yield return "G1 S0";
                 yield break;
            }
            else if (obj is LaserPath path)
            {
                if (path.Points.Count < 2) yield break;
                
                // Rotation handling
                var finalPoints = path.Points.ToArray();
                if (path.Rotation != 0)
                {
                    using (var m = new System.Drawing.Drawing2D.Matrix())
                    {
                         float cx = path.Position.X + path.Size.Width / 2f;
                         float cy = path.Position.Y + path.Size.Height / 2f;
                         m.RotateAt(path.Rotation, new PointF(cx, cy));
                         m.TransformPoints(finalPoints);
                    }
                }

                // Move to start
                var start = finalPoints[0];
                yield return $"G0 X{start.X:F3} Y{start.Y:F3}";
                yield return $"G1 F{fVal:F0}"; 

                for (int i = 1; i < finalPoints.Length; i++)
                {
                    var p = finalPoints[i];
                    yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
                }
                yield return "G1 S0"; 
                yield break;
            }
            else if (obj is LaserBezier bezier)
            {
                using (var gPath = new GraphicsPath())
                {
                    if (bezier.Points.Count >= 4)
                    {
                        // Ensure we have N segments: 4, 7, 10...
                        int count = bezier.Points.Count;
                        int valid = count - (count - 1) % 3;
                        gPath.AddBeziers(bezier.Points.Take(valid).ToArray());
                        
                        // Apply Rotation
                        if (bezier.Rotation != 0)
                        {
                            float cx = bezier.Position.X + bezier.Size.Width / 2f;
                            float cy = bezier.Position.Y + bezier.Size.Height / 2f;
                            using (var m = new System.Drawing.Drawing2D.Matrix())
                            {
                                m.RotateAt(bezier.Rotation, new PointF(cx, cy));
                                gPath.Transform(m);
                            }
                        }
                        
                        gPath.Flatten(null, 0.05f);
                    }
                    else
                    {
                        // Nothing to draw
                    }

                    if (gPath.PointCount > 0)
                    {
                         var points = gPath.PathPoints;
                         var p0 = points[0];
                         
                         yield return $"G0 X{p0.X:F3} Y{p0.Y:F3}";
                         yield return $"G1 F{fVal:F0}";
                         
                         for (int i = 1; i < points.Length; i++)
                         {
                             var p = points[i];
                             yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
                         }
                    }
                }
                yield return "G1 S0";
                yield break;
            }
        }

        // If we are here, it is either FILL Mode OR it is an Image
        // Rasterization Logic
        
        Bitmap? bitmapToRasterize = null;
        bool disposeBitmap = false;
        PointF rasterPos = obj.Position;
        SizeF rasterSize = obj.Size;

        if (obj is LaserImage img)
        {
             // Rasterize Image using its own Draw method to handle Rotation/Masking/etc consistently
             if (img.Image == null) yield break;

             var bounds = img.GetBounds(); // Rotated AABB
             if (bounds.Width <= 0 || bounds.Height <= 0) yield break;

             rasterPos = bounds.Location;
             rasterSize = bounds.Size;

             // Resolution
             float interval = AppConfiguration.Instance.RasterLineInterval;
             if (interval <= 0) interval = 0.1f;
             float dpmm = 1.0f / interval;

             int w = (int)Math.Ceiling(bounds.Width * dpmm);
             int h = (int)Math.Ceiling(bounds.Height * dpmm);
             
             if (w > 0 && h > 0)
             {
                 bitmapToRasterize = new Bitmap(w, h);
                 disposeBitmap = true;
                 
                 using (var g = Graphics.FromImage(bitmapToRasterize))
                 {
                     g.Clear(Color.White);
                     
                     // Setup Transform: World (Y-Up) -> Bitmap (Y-Down)
                     // Map World Bounds Top-Left (MinX, MaxY) to Bitmap (0,0)
                     g.ScaleTransform(dpmm, -dpmm);
                     g.TranslateTransform(-bounds.X, -(bounds.Y + bounds.Height));
                     
                     // Draw Image
                     img.Draw(g, 1.0f);
                 }
             }
        }
        else
        {
            // Vector to Bitmap for Fill
            
            // 1. Generate Path FIRST to determine actual bounds
            using (var path = new GraphicsPath())
            {
                bool hasPath = false;
                
                if (obj is LaserRectangle rect)
                {
                    path.AddRectangle(new RectangleF(rect.Position, rect.Size));
                    hasPath = true;
                }
                else if (obj is LaserCircle circ)
                {
                    path.AddEllipse(circ.Position.X, circ.Position.Y, circ.Size.Width, circ.Size.Height);
                    hasPath = true;
                }
                else if (obj is LaserPath lp)
                {
                    if (lp.Points.Count > 1) 
                    {
                        path.AddLines(lp.Points.ToArray());
                        path.CloseFigure();
                        hasPath = true;
                    }
                }
                else if (obj is LaserBezier lb)
                {
                    if (lb.Points.Count >= 4)
                    {
                        int count = lb.Points.Count;
                        int valid = count - (count - 1) % 3;
                        path.AddBeziers(lb.Points.Take(valid).ToArray());
                        path.CloseFigure();
                        hasPath = true;
                    }
                }
                else if (obj is LaserText lt)
                {
                    var family = new FontFamily(lt.FontName);
                    float emSize = lt.FontSize; 
                    path.AddString(lt.Text, family, (int)lt.FontStyle, emSize, new PointF(0, 0), StringFormat.GenericTypographic);
                    
                    using (var m = new System.Drawing.Drawing2D.Matrix())
                    {
                        // Match LaserText.Draw logic exactly
                        m.Translate(-lt.Size.Width / 2f, lt.Size.Height / 2f);
                        m.Scale(1, -1, MatrixOrder.Append);
                        if (lt.Rotation != 0) m.Rotate(lt.Rotation, MatrixOrder.Append);
                        m.Translate(lt.Position.X + lt.Size.Width / 2f, lt.Position.Y + lt.Size.Height / 2f, MatrixOrder.Append);
 
                        path.Transform(m);
                    }
                    hasPath = true;
                }

                if (!hasPath) yield break;

                // Apply Object Rotation
                if (obj.Rotation != 0)
                {
                    float cx = obj.Position.X + obj.Size.Width / 2f;
                    float cy = obj.Position.Y + obj.Size.Height / 2f;
                    using (var m = new System.Drawing.Drawing2D.Matrix())
                    {
                        m.RotateAt(obj.Rotation, new PointF(cx, cy));
                        path.Transform(m);
                    }
                }

                // 2. Get Exact Bounds from Path
                var exactBounds = path.GetBounds();
                if (exactBounds.Width <= 0 || exactBounds.Height <= 0) yield break;

                // Add small padding to prevent edge clipping (antialiasing safety)
                exactBounds.Inflate(1.0f, 1.0f);

                rasterPos = exactBounds.Location;
                rasterSize = exactBounds.Size;

                // 3. Setup Bitmap
                float interval = AppConfiguration.Instance.RasterLineInterval;
                if (interval <= 0) interval = 0.1f;
                float dpmm = 1.0f / interval; 
                
                int w = (int)Math.Ceiling(rasterSize.Width * dpmm);
                int h = (int)Math.Ceiling(rasterSize.Height * dpmm);
                
                if (w > 0 && h > 0)
                {
                    bitmapToRasterize = new Bitmap(w, h);
                    disposeBitmap = true;

                    using (var g = Graphics.FromImage(bitmapToRasterize))
                    {
                        g.Clear(Color.White); // Background White (No Burn)
                        
                        // Transform World (Y-Up) to Bitmap (Y-Down)
                        // Be careful with Matrix Order (Prepend is default)
                        // We want M = Scale * Translate
                        // Code Order: Translate THEN Scale
                        
                        g.ScaleTransform(dpmm, -dpmm);
                        g.TranslateTransform(-rasterPos.X, -(rasterPos.Y + rasterSize.Height)); 
                        
                        using (var brush = new SolidBrush(Color.Black)) // Black = Burn
                        {
                             g.FillPath(brush, path);
                        }
                    }
                }
                
                // path is disposed at end of using block
            }
        }

        if (bitmapToRasterize != null)
        {
            float interval = AppConfiguration.Instance.RasterLineInterval;
            float minSeg = AppConfiguration.Instance.MinRasterSegmentLength;
            bool bicubic = AppConfiguration.Instance.EnableBicubicResampling;
            bool dither = AppConfiguration.Instance.Enable1BitDithering;

            // Temp image wrapper
            var tempImg = new LaserImage
            {
                Image = bitmapToRasterize,
                Position = rasterPos,
                Size = rasterSize,
                Power = pwrPercent,
                Speed = speedVal
            };

            foreach (var line in Rasterizer.Rasterize(tempImg, sVal, fVal, interval, minSeg, bicubic, dither))
            {
                yield return line;
            }

            if (disposeBitmap) bitmapToRasterize.Dispose();
        }
    }
    public IEnumerable<string> GenerateFraming(IEnumerable<LaserObject> objects, float power, float speed)
    {
        var enabled = objects.Where(o => o.IsEnabled).ToList();
        if (enabled.Count == 0) yield break;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var obj in enabled)
        {
            var b = obj.GetBounds();
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
        }

        yield return "G21";
        yield return "G90";
        yield return "M4 S0"; 

        yield return $"G0 X{minX:F3} Y{minY:F3}";
        
        float sVal = power * 10f; 
        yield return $"G1 F{speed:F0}";
        yield return $"G1 X{maxX:F3} Y{minY:F3} S{sVal:F0}";
        yield return $"G1 X{maxX:F3} Y{maxY:F3} S{sVal:F0}";
        yield return $"G1 X{minX:F3} Y{maxY:F3} S{sVal:F0}";
        yield return "M5";
        yield return "G0 X0 Y0";
    }

    public IEnumerable<string> GenerateObjectOutlines(IEnumerable<LaserObject> objects, float power, float speed)
    {
        var enabled = objects.Where(o => o.IsEnabled).ToList();
        if (enabled.Count == 0) yield break;

        yield return "G21";
        yield return "G90";
        yield return "M4 S0"; 

        float sVal = power * 10f; 

        foreach (var obj in enabled)
        {
            var b = obj.GetBounds();
            // Move to Start
            yield return $"M4 S0";
            yield return $"G0 X{b.Left:F3} Y{b.Top:F3}";
            
            // Cut Box
            yield return $"G1 F{speed:F0}";
            yield return $"G1 X{b.Right:F3} Y{b.Top:F3} S{sVal:F0}";
            yield return $"G1 X{b.Right:F3} Y{b.Bottom:F3} S{sVal:F0}";
            yield return $"G1 X{b.Left:F3} Y{b.Bottom:F3} S{sVal:F0}";
            yield return $"G1 X{b.Left:F3} Y{b.Top:F3} S{sVal:F0}";
        }

        yield return "M5";
        yield return "G0 X0 Y0";
    }

    public IEnumerable<string> GenerateCenterMarks(IEnumerable<LaserObject> objects, float power, float speed)
    {
        var enabled = objects.Where(o => o.IsEnabled).ToList();
        if (enabled.Count == 0) yield break;

        yield return "G21";
        yield return "G90";
        yield return "M4 S0"; 

        float sVal = power * 10f; 
        float size = 5.0f; // 10mm total width

        foreach (var obj in enabled)
        {
            var b = obj.GetBounds();
            float cx = b.X + b.Width / 2f;
            float cy = b.Y + b.Height / 2f;

            // Mark 1: TL to BR
            yield return $"M4 S0";
            yield return $"G0 X{cx - size:F3} Y{cy - size:F3}";
            yield return $"G1 F{speed:F0}";
            yield return $"G1 X{cx + size:F3} Y{cy + size:F3} S{sVal:F0}";

            // Mark 2: BL to TR
            yield return $"M4 S0";
            yield return $"G0 X{cx - size:F3} Y{cy + size:F3}";
            yield return $"G1 X{cx + size:F3} Y{cy - size:F3} S{sVal:F0}";
        }

        yield return "M5";
        yield return "G0 X0 Y0";
    }
}
