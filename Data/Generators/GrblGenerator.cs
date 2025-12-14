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
                using (var path = new GraphicsPath())
                {
                    // Add String expects a signature with origin.
                    // We need to match the visual representation (Bottom-Left origin, Y-Up).
                    // AddString uses Top-Down coordinates usually?
                    // "The Position of the text in the path in world coordinates"
                    // If we use AddString, we should be careful about coordinate systems.
                    // Let's create path at 0,0 and translate?
                    
                    var family = new FontFamily(text.FontName);
                    // EmSize is height of the em square in world units.
                    // LaserText.FontSize is in Points? Or World Units?
                    // In LaserText.Draw we used new Font(..., FontSize). 
                    // If FontSize is in Points (assuming 96dpi), 1 pt = 1.33 px? No.
                    // Font(...) takes arguments based on Unit. Default is Point.
                    // 1 Point = 1/72 inch.
                    // 1 inch = 25.4 mm.
                    // 1 Point = 0.35277 mm.
                    // So if FontSize=20 (Points), height is ~7mm.
                    
                    // AddString EmSize argument is in "World Units" of the GraphicsPath.
                    // We want it to match the visual size.
                    // Graphics.DrawString with Font(size) draws size in Points.
                    
                    // Conversion:
                    // We need to know what "20" means in LaserText.
                    // It seems it is treated as Points in Drawing.
                    // So we must convert Points to Millimeters (World Units).
                    float emSize = text.FontSize * 0.35277f; // Pt to mm
                    // However, EmSize is not exactly Height. 
                    // But for GCode generation, we want a rough match or exact match?
                    // Let's try to match 72 DPI logic if GDI+ default.
                    
                    // Actually, let's look at LaserText.Draw again.
                    // g.ScaleTransform(1, -1) was used.
                    // Here we are generating raw coordinates.
                    
                    // Strategy:
                    // 1. Generate path at O(0,0) with Y-Flip to match DrawString logic.
                    // 2. Translate to Position.
                    
                    int style = (int)FontStyle.Regular;
                    path.AddString(text.Text, family, style, emSize, new PointF(0, 0), StringFormat.GenericDefault);
                    
                    // Now Transform:
                    // Text Visual Logic:
                    // Translate(Pos.X, Pos.Y + Height)
                    // Scale(1, -1)
                    
                    // We need to verify what AddString does. It adds standard text.
                    // If we just flip Y:
                    using (var matrix = new System.Drawing.Drawing2D.Matrix())
                    {
                         // We used Translate(Pos.X, Pos.Y + Height) then Scale(1, -1) in Draw()
                         // So we apply the same transform to the path.
                         
                         // BUT: We need to know 'Height' (text.Size.Height).
                         // LaserText has .Size property which is updated on Draw. 
                         // If it hasn't been drawn, it might be 0? 
                         // Usually it serves as the bounds.
                         
                         // If Size is 0, we might have offset issues.
                         // But typically user generates GCode after viewing (Draw).
                         
                         matrix.Translate(text.Position.X, text.Position.Y + text.Size.Height);
                         matrix.Scale(1, -1);
                         path.Transform(matrix);
                    }
                    
                    // Now Flatten to get line segments
                    // Flatness: smaller = smoother curves, more segments.
                    // 0.1mm error?
                    path.Flatten(null, 0.05f); // 0.05mm precision
                    
                    // Iterate Logic
                   if (path.PointCount > 0)
                   {
                        // PathPoints and PathTypes work together
                        PointF[] points = path.PathPoints;
                        byte[] types = path.PathTypes;
                        
                        for (int i = 0; i < points.Length; i++)
                        {
                            var p = points[i];
                            byte type = types[i];
                            
                            // 0 = Start (Move)
                            // 1 = Line (Cut)
                            // 3 = Bezier (Should be flattened to Line, so we shouldn't see 3)
                            // Mask 0x7 to get type (remove closepath flag 0x80)
                            
                            byte typeMasked = (byte)(type & 0x07);
                            
                            if (typeMasked == 0) // Start
                            {
                                yield return "G1 S0"; // Ensure off
                                yield return $"G0 X{p.X:F3} Y{p.Y:F3}";
                                yield return $"G1 F{fVal:F0}"; // Prep Feed
                            }
                            else // Line
                            {
                                yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
                            }
                            
                            if ((type & 0x80) != 0) // CloseSubpath
                            {
                                // If closed, it usually draws line to start?
                                // Valid GCode flow will just follow points.
                                // If CloseSubpath implies an extra segment not in points, we might miss it.
                                // But Flatten usually adds explicit closing point if needed?
                                // Let's check GDI+ docs: Flatten "converts all curves... and connects end points of closed paths". 
                                // So we should be good.
                            }
                        }
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
        }

        // If we are here, it is either FILL Mode OR it is an Image
        // Rasterization Logic
        
        Bitmap? bitmapToRasterize = null;
        bool disposeBitmap = false;
        PointF rasterPos = obj.Position;
        SizeF rasterSize = obj.Size;

        if (obj is LaserImage img)
        {
             bitmapToRasterize = img.Image;
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
        yield return $"G1 X{minX:F3} Y{minY:F3} S{sVal:F0}";
        
        yield return "G1 S0";
        yield return "M5";
        yield return "G0 X0 Y0";
    }
}
