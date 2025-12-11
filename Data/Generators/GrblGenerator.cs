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
            foreach (var line in Rasterizer.Rasterize(img, sVal, fVal))
            {
                yield return line;
            }
        }
        else if (obj is LaserText text)
        {
            // On-the-fly rasterization for text
            // Create a bitmap for the text
            var bounds = text.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) yield break;

            // Scaling for resolution? 
            // Screen 96 DPI. Laser might want more. 
            // Let's settle on a "pixel size" of approx 0.1mm (10 pixels/mm) -> 254 DPI?
            float dpmm = 10f; 
            int w = (int)Math.Max(1, Math.Ceiling(bounds.Width * dpmm));
            int h = (int)Math.Max(1, Math.Ceiling(bounds.Height * dpmm));

            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White); // White is "Off"
                // Draw text at (0,0) with scaling
                g.ScaleTransform(dpmm, dpmm);
                g.TranslateTransform(-bounds.X, -bounds.Y); // Local coordinates
                
                // We need to carefully draw only the text
                // But text.Draw uses Position. 
                // We want to draw it such that it appears on the bitmap.
                // Text.Draw uses Position.
                // We translated by -Bounds.X/Y.
                // If Position is (10, 10), and Bounds is (10, 10, 50, 20).
                // Translate(-10, -10) -> (0,0).
                // Draw at Position(10,10) -> (10,10) relative to origin? No.
                // Wait, Graphics transform applies to coordinates passed to Draw methods.
                // If Text is at (100, 100). Bounds starts at (100, 100).
                // Translate(-100, -100).
                // Draw at (100, 100) -> lands at (0, 0).
                // Yes, that works.
                
                text.Draw(g, 1.0f); // Scale 1.0 because we handled scaling via Transform
            }

            // Create a temp LaserImage to reuse Rasterizer
            var tempImg = new LaserImage
            {
                Position = bounds.Location, // Important to place it correctly in world
                Size = bounds.Size,
                Image = bmp,
                Power = obj.Power,
                Speed = obj.Speed
            };

            foreach (var line in Rasterizer.Rasterize(tempImg, sVal, fVal))
            {
                yield return line;
            }
        }
    }
}
