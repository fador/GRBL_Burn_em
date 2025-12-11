using System.Drawing;
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

        // Common settings
        float sVal = obj.Power * 10f; // 0-100 -> 0-1000
        float fVal = obj.Speed;

        if (obj is LaserPath path)
        {
            if (path.Points.Count < 2) yield break;

            // Move to start
            var start = path.Points[0];
            yield return $"G0 X{start.X:F3} Y{start.Y:F3}";
            yield return $"G1 F{fVal:F0}"; // Set speed

            for (int i = 1; i < path.Points.Count; i++)
            {
                var p = path.Points[i];
                yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
            }
            yield return "G1 S0"; // Turn off after path
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
        }
        else if (obj is LaserImage img)
        {
            // Rasterize
            float interval = AppConfiguration.Instance.RasterLineInterval;
            float minSeg = AppConfiguration.Instance.MinRasterSegmentLength;
            bool bicubic = AppConfiguration.Instance.EnableBicubicResampling;
            
            foreach (var line in Rasterizer.Rasterize(img, sVal, fVal, interval, minSeg, bicubic))
            {
                yield return line;
            }
        }
        else if (obj is LaserText text)
        {
            // On-the-fly rasterization for text
            // ... (Bitmap generation code omitted for brevity as it is unchanged mostly) ... 
            
            // Create a bitmap for the text
            var bounds = text.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) yield break;

            float dpmm = 10f; 
            int w = (int)Math.Max(1, Math.Ceiling(bounds.Width * dpmm));
            int h = (int)Math.Max(1, Math.Ceiling(bounds.Height * dpmm));
            
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White); // White is "Off"
                g.ScaleTransform(dpmm, dpmm);
                
                g.TranslateTransform(-bounds.X, -bounds.Y); 
                text.Draw(g, 1.0f); 
            }

            // Create a temp LaserImage
            var tempImg = new LaserImage
            {
                Position = bounds.Location,
                Size = bounds.Size,
                Image = bmp,
                Power = obj.Power,
                Speed = obj.Speed
            };

            float interval = AppConfiguration.Instance.RasterLineInterval;
            float minSeg = AppConfiguration.Instance.MinRasterSegmentLength;
            // Bicubic for text? Text bitmap is generated at high res (10 pixels/mm). 
            // If we use bicubic, it might smooth edges if scaled. 
            // But here we generated it at "native" resolution for raster?
            // Actually, if interval is 0.1mm, we match 10 pixels/mm.
            // If interval is 0.3mm, we are "downscaling" the rows.
            // Bicubic might help if interval > 0.1mm.
            bool bicubic = AppConfiguration.Instance.EnableBicubicResampling;

            foreach (var line in Rasterizer.Rasterize(tempImg, sVal, fVal, interval, minSeg, bicubic))
            {
                yield return line;
            }
        }
    }
}
