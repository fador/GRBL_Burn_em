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
                    // Use scale 96/72 to match screen
                    float emSize = text.FontSize * 96f / 72f; 
                    
                    int style = (int)FontStyle.Regular;
                    gp.AddString(text.Text, family, style, emSize, new PointF(0, 0), StringFormat.GenericDefault);

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
                                    float ascent = family.GetCellAscent((FontStyle)style) * emSize / family.GetEmHeight((FontStyle)style);
                                    using (var m = new System.Drawing.Drawing2D.Matrix())
                                    {
                                        m.Translate(0, -ascent + text.VerticalOffset);
                                        m.Scale(1, -1);
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
                              matrix.Translate(text.Position.X, text.Position.Y + text.Size.Height);
                              matrix.Scale(1, -1);
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
                     // AddEllipse takes x,y,w,h. Position is Bottom-Left in our World Logic?
                     // No, Position is usually Top-Left of the Bounds in GDI+ logic?
                     // LaserObject.Position:
                     // LaserRectangle.Draw: DrawRectangle(pen, Position.X, Position.Y, ...) -> GDI+ draws from Top-Left (Coordinate system dependent).
                     // In Workbench: Translate(CenterX, CenterY), Scale(Zoom, -Zoom).
                     // So Y+ is Up.
                     // If I draw Rectangle at (0,0) with size (10,10):
                     // It draws from 0,0 to 10,10.
                     // In Screen Coords (Y+ Down): 0,0 to 10,10 is Top-Left to Bottom-Right.
                     // In World Coords (Y+ Up): 0,0 to 10,10 is Bottom-Left to Top-Right?
                     // BUT `DrawRectangle` takes (x, y, w, h). 
                     // If Y scale is negative:
                     // Coordinate (0,0) maps to ScreenCenter.
                     // Coordinate (0, 10). Scale(1, -1) -> (0, -10).
                     // So (0, 10) is "Higher" on screen (smaller Y pixel value).
                     // So yes, Y+ is Up.
                     // `DrawRectangle(0, 0, 10, 10)`:
                     // Starts at 0,0. Extends Width 10 (Right). Extends Height 10 (Down in GDI+ RAW).
                     // But with Scale(1, -1):
                     // The "Height" extension of +10 in Y becomes -10 in Screen Y (Up).
                     // So result is box from 0,0 extending Right and Up.
                     // So `Position` is Bottom-Left. Good.
                     
                     // GraphicsPath.AddEllipse(x, y, w, h).
                     // Adds ellipse constrained by rect (x, y, w, h).
                     // In transformed space (Y+ Up), this rect is Position (BL) extending Right and Up.
                     // So the ellipse will be correct.
                     
                     path.AddEllipse(circle.Position.X, circle.Position.Y, circle.Size.Width, circle.Size.Height);
                     
                     // Flatten to polyline
                     path.Flatten(null, 0.05f); // 0.05mm precision
                     
                     if (path.PointCount > 0)
                     {
                         var points = path.PathPoints;
                         var p0 = points[0];
                         
                         yield return $"G0 X{p0.X:F3} Y{p0.Y:F3}";
                         yield return $"G1 F{fVal:F0}";
                         
                         for (int i = 1; i < points.Length; i++)
                         {
                             var p = points[i];
                             yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
                         }
                         // Close loop
                         yield return $"G1 X{p0.X:F3} Y{p0.Y:F3} S{sVal:F0}";
                     }
                 }
                 yield return "G1 S0";
                 yield break;
            }
            else if (obj is LaserPath path)
            {
                if (path.Points.Count < 2) yield break;
                // Move to start
                var start = path.Points[0];
                yield return $"G0 X{start.X:F3} Y{start.Y:F3}";
                yield return $"G1 F{fVal:F0}"; 

                for (int i = 1; i < path.Points.Count; i++)
                {
                    var p = path.Points[i];
                    yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
                }
                yield return "G1 S0"; 
                yield break;
            }
            else if (obj is LaserRectangle rect)
            {
                float l = rect.Position.X;
                float t = rect.Position.Y;
                float r = l + rect.Size.Width;
                float b = t + rect.Size.Height;

                yield return $"G0 X{l:F3} Y{t:F3}";
                yield return $"G1 F{fVal:F0}";

                yield return $"G1 X{r:F3} Y{t:F3} S{sVal:F0}";
                yield return $"G1 X{r:F3} Y{b:F3} S{sVal:F0}";
                yield return $"G1 X{l:F3} Y{b:F3} S{sVal:F0}";
                yield return $"G1 X{l:F3} Y{t:F3} S{sVal:F0}";
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
             // Apply Mask if needed
             if (img.MaskId != Guid.Empty)
             {
                 var maskObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == img.MaskId);
                 if (maskObj != null && img.Image != null)
                 {
                     bitmapToRasterize = new Bitmap(img.Image.Width, img.Image.Height);
                     disposeBitmap = true;
                     using (var g = Graphics.FromImage(bitmapToRasterize))
                     {
                         // Maintain resolution/coordinates
                         // We are drawing into a bitmap of precise PIXEL size of the original image.
                         // But the MASK is defined in WORLD coordinates.
                         // This is tricky.
                         // The original image pixels map to World Rect (Position, Size).
                         // The Mask is in World Rect.
                         
                         // Coordinate transform:
                         // 0,0 of Bitmap -> Top-Left of Image in World?
                         // Image.Draw draws (0,0,W,H) at Position.
                         // So we need to map World Mask to Bitmap Coords.
                         // Bitmap Width Bw maps to World Width Sw.
                         // Scale = Bw / Sw.
                         // Pos (Bitmap) = (Pos(World) - ImagePos(World)) * Scale.
                         
                         // Create GraphicsPath for mask
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
                             // Transform Path to Bitmap Coordinates
                             using (var matrix = new Matrix())
                             {
                                 float scaleX = img.Image.Width / img.Size.Width;
                                 float scaleY = img.Image.Height / img.Size.Height;
                                 
                                 // Translate World Point P to Image-Local P'
                                 // P' = (P - ImagePos) * Scale
                                 // Note: Y axis?
                                 // Image Bitmap 0,0 is Top-Left.
                                 // World Space Y+ is Up (Bottom-Left).
                                 // Image.Draw does: Translate(Pos.X, Pos.Y+H), Scale(1,-1).
                                 // So Image Top-Left is at World(Pos.X, Pos.Y+H).
                                 // So P_Bitmap_X = (P_World_X - Pos.X) * scaleX.
                                 // P_Bitmap_Y = (Pos.Y + H - P_World_Y) * scaleY.
                                 
                                 // Let's adjust Matrix:
                                 // 1. Translate World Origin to Image Top-Left (which is Pos.X, Pos.Y+H)
                                 matrix.Translate(-img.Position.X, -(img.Position.Y + img.Size.Height), MatrixOrder.Append);
                                 
                                 // 2. Flip Y (Because Bitmap Y+ is Down, World Y+ is Up)
                                 matrix.Scale(1, -1, MatrixOrder.Append);
                                 
                                 // 3. Scale to Bitmap Pixels
                                 matrix.Scale(scaleX, scaleY, MatrixOrder.Append);
                                 
                                 clipPath.Transform(matrix);
                             }
                             
                             g.SetClip(clipPath);
                             g.DrawImage(img.Image, 0, 0, img.Image.Width, img.Image.Height);
                         }
                         else
                         {
                             // Fallback
                             g.DrawImage(img.Image, 0, 0, img.Image.Width, img.Image.Height);
                         }
                     }
                 }
                 else
                 {
                     bitmapToRasterize = img.Image;
                 }
             }
             else
             {
                 bitmapToRasterize = img.Image;
             }
        }
        else
        {
            // Vector to Bitmap for Fill
            var bounds = obj.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) yield break;

            // Resolution for Rasterization (Pixels per mm)
            // AppConfiguration.Instance.RasterLineInterval (mm/line). 
            // If 0.1 mm, then 10 lines/mm.
            float interval = AppConfiguration.Instance.RasterLineInterval;
            if (interval <= 0) interval = 0.1f;
            float dpmm = 1.0f / interval; 
            
            // Limit resolution to avoid huge bitmaps
            // Max 10000x10000?
            int w = (int)Math.Ceiling(bounds.Width * dpmm);
            int h = (int)Math.Ceiling(bounds.Height * dpmm);
            
            if (w > 0 && h > 0)
            {
                bitmapToRasterize = new Bitmap(w, h);
                disposeBitmap = true;
                rasterPos = bounds.Location;
                rasterSize = bounds.Size;

                using (var g = Graphics.FromImage(bitmapToRasterize))
                {
                    g.Clear(Color.White); // Background White (No Burn)
                    g.ScaleTransform(dpmm, dpmm);
                    g.TranslateTransform(-bounds.X, -bounds.Y);
                    
                    using (var brush = new SolidBrush(Color.Black)) // Black = Burn
                    {
                        if (obj is LaserRectangle)
                        {
                            g.FillRectangle(brush, obj.Position.X, obj.Position.Y, obj.Size.Width, obj.Size.Height);
                        }
                        else if (obj is LaserPath lp)
                        {
                             if (lp.Points.Count > 2)
                                 g.FillPolygon(brush, lp.Points.ToArray());
                        }
                        else if (obj is LaserText lt)
                        {
                             // Simple draw for now
                             // Text Draw method does transform. We need to match.
                             // Text.Draw uses Position. We normalized manually above?
                             // No, we translated G so 0,0 is Bounds TopLeft.
                             // Text Position is in World.
                             // If we just call Draw, it should draw at world coords, which are shifted by TranslateTransform.
                             // But Text Draw expects to handle Scale(1,-1) itself for upright?
                             // Our bitmap is Top-Down.
                             // Text Logic:
                             // g.TranslateTransform(Position.X, Position.Y);
                             // g.ScaleTransform(1, -1);
                             
                             // We don't want the Y-flip for the bitmap generation if we just want "Black pixels where text is".
                             // But Rasterizer expects standard image logic?
                             // Rasterizer iterates Y top to bottom.
                             // So we should draw Text Normally (Upright).
                             
                             // We can use Graphics.DrawString directly here.
                             using (var f = new Font(lt.FontName, lt.FontSize)) // Unit?
                             {
                                 // DrawString coordinates are local. 
                                 // We established transform.
                                 g.DrawString(lt.Text, f, brush, lt.Position.X, lt.Position.Y);
                             }
                        }
                    }
                }
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
