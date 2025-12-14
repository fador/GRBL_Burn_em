using System.Drawing;
using System.Text;
using System.Drawing.Drawing2D;

namespace laser_gui_test.Data.Generators;

public static class Rasterizer
{
    public static IEnumerable<string> Rasterize(LaserImage image, float maxPower, float speed, float lineInterval, float minSegmentLength, bool enableBicubic, bool enableDithering)
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
             // If dithering is enabled, we MUST resize first, THEN dither.
             // Quality scaling is good here because it gives us a better source for dithering.
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
             scanBmp = image.Image;
             disposeBmp = false;
        }
        
        // Helper to clone if we need to modify pixels (Dithering modifies in-place) without touching original
        if (enableDithering && !disposeBmp)
        {
            scanBmp = new Bitmap(image.Image);
            disposeBmp = true;
        }

        // Apply Dithering if enabled
        if (enableDithering)
        {
            ApplyDithering(scanBmp);
        }

        float pixelWidth = width / scanBmp.Width;
        
        bool scanForward = true;

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
                
                float intensity = 0;
                if (pixel.A == 0)
                {
                    intensity = 0;
                }
                else
                {
                    // Simple grayscale conversion: 0.299R + 0.587G + 0.114B
                    float gray = 0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B;
                
                    // Invert: 0 (Black) -> Max Power, 255 (White) -> 0 Power
                    intensity = (255f - gray) / 255f; // 0.0 to 1.0
                    intensity *= pixel.A / 255f; // Apply Alpha
                }

                // With dithering, intensity should be mostly 0 or 1.
                // But let's keep the logic generic.
                
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
            
            // Find Active Segment Range (Trim Start/End zeros)
            int startIndex = -1;
            int endIndex = -1;

            for (int k = 0; k < filteredSegments.Count; k++)
            {
                if (filteredSegments[k].intensity > 0)
                {
                    if (startIndex == -1) startIndex = k;
                    endIndex = k;
                }
            }

            if (startIndex == -1) continue; // Empty line

            // Calculate Offsets
            float preOffset = 0;
            for (int k = 0; k < startIndex; k++) preOffset += filteredSegments[k].length;

            float activeLength = 0;
            for (int k = startIndex; k <= endIndex; k++) activeLength += filteredSegments[k].length;

            // Generate G-code
            // Determine Start Position for this pass
            float startScanX = startX + preOffset;
            float endScanX = startX + preOffset + activeLength;

            float currentX = scanForward ? startScanX : endScanX;
            yield return $"G0 X{currentX:F3} Y{currentY:F3}";
            bool lastG0 = true;

            // Loop logic
            // Forward: startIndex -> endIndex
            // Reverse: endIndex -> startIndex
            
            var range = scanForward 
                ? Enumerable.Range(startIndex, endIndex - startIndex + 1) 
                : Enumerable.Range(startIndex, endIndex - startIndex + 1).Reverse();

            foreach (int k in range)
            {
                var segment = filteredSegments[k];
                // In reverse, we move -length. In forward, +length.
                float nextX = scanForward ? currentX + segment.length : currentX - segment.length;
                float sValue = segment.intensity * maxPower;
                
                if (sValue <= 0)
                {
                    // Travel / Off
                    // Even inside the active area, we might have gaps.
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
            scanForward = !scanForward; // Toggle for next line
        }
        
        if (disposeBmp) scanBmp.Dispose();
        yield return "G0 S0"; 
    }

    private static void ApplyDithering(Bitmap bmp)
    {
        // Floyd-Steinberg Dithering
        // Iterate over pixels, calculate error, diffuse to neighbors.
        // Requires locking bits for speed, or direct pixel access.
        // Since we are typically processing on main thread or BG, standard Get/SetPixel is SLOW.
        // But for simplicity/safety first implementation, we can use Get/SetPixel (performance hit).
        // Optimization: Use LockBits.
        
        int w = bmp.Width;
        int h = bmp.Height;
        
        // We need a floating point buffer to store errors? 
        // Or we can modify the bitmap directly if we are careful. 
        // But SetPixel clamps to 0-255. Error propagation needs float/int beyond 255.
        // So we need a temporary buffer.
        
        float[,] buffer = new float[w, h];

        // Fill buffer with grayscale values
        for(int y=0; y<h; y++)
        {
            for(int x=0; x<w; x++)
            {
                Color c = bmp.GetPixel(x, y);
                // Grayscale
                // Grayscale
                float gray = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
                
                // Handle Alpha: Transparent pixels should be considered "White" (No Burn).
                // We blend the grayscale value with White (255) based on Alpha.
                // If Alpha=1, use Gray. If Alpha=0, use 255.
                float alpha = c.A / 255f;
                buffer[x,y] = (gray * alpha) + (255f * (1f - alpha));
            }
        }

        // Dither
        for(int y=0; y<h; y++)
        {
            for(int x=0; x<w; x++)
            {
                float oldPixel = buffer[x, y];
                float newPixel = oldPixel < 128 ? 0 : 255;
                buffer[x,y] = newPixel;
                
                float quantError = oldPixel - newPixel;

                if (x + 1 < w)
                    buffer[x + 1, y] += quantError * 7 / 16;
                
                if (x - 1 >= 0 && y + 1 < h)
                    buffer[x - 1, y + 1] += quantError * 3 / 16;
                
                if (y + 1 < h)
                    buffer[x, y + 1] += quantError * 5 / 16;
                
                if (x + 1 < w && y + 1 < h)
                    buffer[x + 1, y + 1] += quantError * 1 / 16;
            }
        }

        // Write back to Bitmap
        for(int y=0; y<h; y++)
        {
            for(int x=0; x<w; x++)
            {
                int val = (int)Math.Clamp(buffer[x, y], 0, 255);
                bmp.SetPixel(x, y, Color.FromArgb(255, val, val, val));
            }
        }
    }
}
