using System.Drawing;
using System.Text;

namespace laser_gui_test.Data.Generators;

public static class Rasterizer
{
    public static IEnumerable<string> Rasterize(LaserImage image, float maxPower, float speed)
    {
        if (image.Image == null) yield break;

        var bmp = image.Image;
        // Grbl M4 Dynamic Power Mode:
        // S0 = 0% power, S{maxPower} = 100% power.
        // We assume 0-255 grayscale.

        // Initial setup
        yield return $"G0 F{speed}"; // Set feed rate

        float startX = image.Position.X;
        float startY = image.Position.Y;
        float width = image.Size.Width;
        float height = image.Size.Height;
        
        float pixelWidth = width / bmp.Width;
        float pixelHeight = height / bmp.Height;

        bool direction = true; // Zig-zag scanning: true = right, false = left

        for (int y = 0; y < bmp.Height; y++)
        {
            // Calculate physical Y
            // Y-Up: StartY is Bottom. StartY + Height is Top.
            // Scanlines normally go Top to Bottom for image rastering?
            // Or we scan the logic Y?
            // y=0 is Top of bitmap.
            // So y=0 matches Physical Top (StartY + Height).
            // y=H matches Physical Bottom (StartY).
            float currentY = (startY + height) - (y * pixelHeight) - (pixelHeight / 2); // Center of pixel
            
            // Move to start of line (fast move)
            // Left-to-Right
            float lineStartX = startX;
            // Right-to-Left
            if (!direction) lineStartX = startX + width;
            
            // We optimize by skipping completely empty lines?
            // For now, simple implementation: just process every line that has non-white content.
            
            // Extract row pixels
            var rowPixels = new List<(float intensity, int count)>();
            int currentCount = 0;
            float currentIntensity = -1;

            int xStart = direction ? 0 : bmp.Width - 1;
            int xEnd = direction ? bmp.Width : -1;
            int xStep = direction ? 1 : -1;

            bool rowHasData = false;

            for (int x = xStart; x != xEnd; x += xStep)
            {
                Color pixel = bmp.GetPixel(x, y);
                // Simple grayscale conversion: 0.299R + 0.587G + 0.114B
                float gray = 0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B;
                
                // Invert: 0 (Black) -> Max Power, 255 (White) -> 0 Power
                // Also handle Alpha
                float intensity = 0;
                if (pixel.A > 10) // Threshold for transparency
                {
                    intensity = (255f - gray) / 255f; // 0.0 to 1.0
                }

                if (intensity > 0) rowHasData = true;

                if (Math.Abs(intensity - currentIntensity) < 0.01f)
                {
                    currentCount++;
                }
                else
                {
                    if (currentCount > 0)
                    {
                        rowPixels.Add((currentIntensity, currentCount));
                    }
                    currentIntensity = intensity;
                    currentCount = 1;
                }
            }
            if (currentCount > 0) rowPixels.Add((currentIntensity, currentCount));

            if (!rowHasData) continue; // Skip empty rows

            // Move to start of the row (or the first pixel of the row?)
            // We need to be careful with zig-zag. 
            // If direction is right, we start at X=0 (Physical startX).
            // If direction is left, we start at X=Width (Physical startX + width).
            
            // Actually, we should move to the start of the first segment. 
            // But G-code works by moving TO a coordinate.
            // So we need to be at the start position before issuing the first cutting move.
            
            float currentX = direction ? startX : startX + width;
            yield return $"G0 X{currentX:F3} Y{currentY:F3}";

            // Now execute segments
            foreach (var segment in rowPixels)
            {
                float segmentLength = segment.count * pixelWidth;
                if (!direction) segmentLength = -segmentLength; // Moving left reduces X

                float nextX = currentX + segmentLength;
                float sValue = segment.intensity * maxPower;
                
                if (sValue <= 0)
                {
                     // Move without power (G0 or G1 S0). 
                     // G1 S0 is safer to keep 'Laser Mode' behavior consistent (no turn off/on delay?)
                     // Actually G0 is usually non-cutting travel.
                     // In M4 mode, G1 S0 turns laser off but kept in motion.
                     yield return $"G1 X{nextX:F3} S0";
                }
                else
                {
                    yield return $"G1 X{nextX:F3} S{sValue:F0}";
                }
                
                currentX = nextX;
            }
            
            // Flip direction for next row
           // direction = !direction; // TODO: Zigzag needs backlash compensation usually. Let's do Uni-directional for quality first?
           // Uni-directional is slower but safer. 
           // Let's stick to uni-directional (Always Left-to-Right) for now to be safe and simple.
        }
        
        yield return "G0 S0"; // Ensure off
    }
}
