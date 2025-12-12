using System.Drawing;
using System.Text;
using System.Drawing.Drawing2D;

namespace laser_gui_test.Data.Generators;

public static class Rasterizer
{
    public static IEnumerable<string> Rasterize(LaserImage image, float maxPower, float speed, float lineInterval, float minSegmentLength, bool enableBicubic)
    {
        if (image.Image == null) yield break;

        // Grbl M4 Dynamic Power Mode:
        // S0 = 0% power, S{maxPower} = 100% power.
        // We assume 0-255 grayscale.

        // Initial setup
        yield return $"G0 F{speed}"; // Set feed rate

        float startX = image.Position.X;
        float startY = image.Position.Y;
        float width = image.Size.Width;
        float height = image.Size.Height;
        
        // Determine target resolution based on line interval
        // We want 1 pixel per lineInterval in Y, and square pixels in X (so same resolution)
        int targetH = (int)Math.Max(1, Math.Truncate(height / lineInterval));
        int targetW = (int)Math.Max(1, Math.Truncate(width / lineInterval));
        
        // Prepare Bitmap
        Bitmap scanBmp;
        bool disposeBmp = false;

        // Check if resize is needed (dimensions differ)
        if (image.Image.Width != targetW || image.Image.Height != targetH)
        {
             scanBmp = new Bitmap(targetW, targetH);
             using (var g = Graphics.FromImage(scanBmp))
             {
                 // Select Interpolation Mode
                 g.InterpolationMode = enableBicubic 
                    ? InterpolationMode.HighQualityBicubic 
                    : InterpolationMode.NearestNeighbor;
                 
                 g.PixelOffsetMode = PixelOffsetMode.Half; // Better center alignment
                 g.DrawImage(image.Image, 0, 0, targetW, targetH);
             }
             disposeBmp = true;
        }
        else
        {
             // Even if dimensions match, if user really wants 'Bicubic' on an image that is already 1:1, 
             // it doesn't do anything. Usage implies scaling.
             scanBmp = image.Image;
             disposeBmp = false;
        }

        float pixelWidth = width / scanBmp.Width;
        
        // Loop through lines (Y)
        for (int i = 0; i < targetH; i++)
        {
            // Calculate physical Y (Top Down scan)
            // Y-Up world: Top is StartY + Height
            float currentY = (startY + height) - (i * lineInterval);
            
            // Map to Bitmap Y (1:1 now)
            int y = i;
            if (y >= scanBmp.Height) y = scanBmp.Height - 1;

            // Extract row pixels
            var rawSegments = new List<(float intensity, int count)>();
            int currentCount = 0;
            float currentIntensity = -1;

            for (int x = 0; x < scanBmp.Width; x++)
            {
                Color pixel = scanBmp.GetPixel(x, y);
                // Simple grayscale conversion: 0.299R + 0.587G + 0.114B
                float gray = 0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B;
                
                // Invert: 0 (Black) -> Max Power, 255 (White) -> 0 Power
                float intensity = (255f - gray) / 255f; // 0.0 to 1.0
                intensity *= pixel.A / 255f; // Apply Alpha

                if (Math.Abs(intensity - currentIntensity) < 0.01f)
                {
                    currentCount++;
                }
                else
                {
                    if (currentCount > 0)
                    {
                        rawSegments.Add((currentIntensity, currentCount));
                    }
                    currentIntensity = intensity;
                    currentCount = 1;
                }
            }
            if (currentCount > 0) rawSegments.Add((currentIntensity, currentCount));

            // Filter by Min Segment Length
            var filteredSegments = new List<(float intensity, float length)>();
            
            if (rawSegments.Count > 0)
            {
                foreach (var seg in rawSegments)
                {
                    float len = seg.count * pixelWidth;
                    
                    if (filteredSegments.Count > 0)
                    {
                        var last = filteredSegments[filteredSegments.Count - 1];
                        if (len < minSegmentLength)
                        {
                            // Merge short segment into previous
                            filteredSegments[filteredSegments.Count - 1] = (last.intensity, last.length + len);
                        }
                        else
                        {
                            filteredSegments.Add((seg.intensity, len));
                        }
                    }
                    else
                    {
                        filteredSegments.Add((seg.intensity, len));
                    }
                }
            }
            
            // Check if row has data (any non-zero intensity)
            if (!filteredSegments.Any(s => s.intensity > 0)) continue;

            // Generate G-code
            float currentX = startX;
            yield return $"G0 X{currentX:F3} Y{currentY:F3}";
            bool lastG0 = true;

            foreach (var segment in filteredSegments)
            {
                float nextX = currentX + segment.length;
                float sValue = segment.intensity * maxPower;
                
                if (sValue <= 0)
                {
                    // Travel / Off
                    if(lastG0) yield return $"G1 X{nextX:F3} S0";
                    else yield return $"X{nextX:F3} S0";
                    lastG0 = false;
                }
                else
                {
                    // Burn
                    if(lastG0) yield return $"G1 X{nextX:F3} S{sValue:F0}";
                    else yield return $"X{nextX:F3} S{sValue:F0}";
                    lastG0 = false;
                }
                currentX = nextX;
            }
        }
        
        if (disposeBmp) scanBmp.Dispose();
        yield return "G0 S0"; 
    }
}
