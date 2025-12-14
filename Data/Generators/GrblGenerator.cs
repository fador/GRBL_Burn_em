using System.Drawing;
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
                // Text "Cut" means Outline?
                // GraphicsPath from text can be used.
                // For now, let's assume Text in Cut mode is Outline.
                // Converting Text to Path is complex without GraphicsPath APIs.
                // Simplest fallback: If Text is in Cut mode, treat as Fill (Raster) for now or warn?
                // Or maybe the user expects valid "Cut" behavior for text?
                // Let's fallback to Raster for Text even in Cut mode because we don't have a Vectorizer here yet,
                // UNLESS we implement GraphicsPath iterator.
                // Given the scope, let's Rasterize Text always for now to guarantee output.
                // OR: Implement simple vector text if possible. 
                // Let's stick to Raster for Text for safety in this iteration.
                mode = LayerMode.Fill; 
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
