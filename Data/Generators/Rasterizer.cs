using System.Drawing;
using System.Text;

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
        
        // Determine number of lines
        int numLines = (int)Math.Max(1, Math.Truncate(height / lineInterval));
        
        // Prepare Bitmap
        Bitmap scanBmp = image.Image;
        bool disposeBmp = false;

        // Bicubic Resampling
        if (enableBicubic)
        {
            // Target dimensions
            int targetH = numLines;
            int targetW = (int)(width / lineInterval); // Keep square pixel aspect ratio for resampling? 
                                                        // Or match native resolution?
                                                        // Ideally we want 1 pixel per lineInterval in Y.
                                                        // And similar density in X.
            if (targetW < 1) targetW = 1;
            
            // Create resized bitmap
            var resized = new Bitmap(targetW, targetH);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image.Image, 0, 0, targetW, targetH);
            }
            scanBmp = resized;
            disposeBmp = true;
        }

        float pixelWidth = width / scanBmp.Width;
        // pixelHeight is effective lineInterval for Y stepping

        bool direction = true; // Zig-zag scanning: true = right, false = left (Only Right currently implemented)

        for (int i = 0; i < numLines; i++)
        {
            // Calculate physical Y
            // Y-Up: StartY is Bottom. StartY + Height is Top.
            // i=0 -> Top.
            float currentY = (startY + height) - (i * lineInterval);
            
            // Map to Bitmap Y
            // If we resized, it's 1:1 map i -> y
            // If not, we map proportionally
            int y = enableBicubic ? i : (int)((i * lineInterval) / height * scanBmp.Height);
            
            if (y >= scanBmp.Height) y = scanBmp.Height - 1;
            if (y < 0) y = 0;

            // Extract row pixels
            var rawSegments = new List<(float intensity, int count)>();
            int currentCount = 0;
            float currentIntensity = -1;

            // Only Left-to-Right for now to match safety logic
            for (int x = 0; x < scanBmp.Width; x++)
            {
                Color pixel = scanBmp.GetPixel(x, y);
                // Simple grayscale conversion: 0.299R + 0.587G + 0.114B
                float gray = 0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B;
                
                // Invert: 0 (Black) -> Max Power, 255 (White) -> 0 Power
                // Also handle Alpha
                float intensity = (255f - gray) / 255f; // 0.0 to 1.0

                intensity *= pixel.A / 255f;


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
                // Convert to physical length
                foreach (var seg in rawSegments)
                {
                    float len = seg.count * pixelWidth;
                    
                    // Merge logic
                    if (filteredSegments.Count > 0)
                    {
                        var last = filteredSegments[filteredSegments.Count - 1];
                        
                        // If current segment is too short, merge into previous? 
                        // Or if previous was short? 
                        // Logic: If THIS segment is short, extend previous segment to cover it?
                        // But what about intensity? 
                        // Ideally: weighted average? Or just keep previous intensity?
                        // If we are rastering, small blips might be noise.
                        
                        // Strategy: Always add. Then pass 2: merge smalls.
                        // Better: Merge on the fly.
                        
                        if (len < minSegmentLength)
                        {
                            // Merge into previous
                            // We effectively ignore this intensity change and extend the previous one.
                            filteredSegments[filteredSegments.Count - 1] = (last.intensity, last.length + len);
                        }
                        else
                        {
                            // Check if PREVIOUS was too short? No, we merge current INTO previous.
                            // So previous is always accumulating until we hit a long-enough segment?
                            // Issue: If we have many short segments (gradient), we might merge them all into one flat block?
                            // Maybe valid for "Min Segment".
                            
                            filteredSegments.Add((seg.intensity, len));
                        }
                    }
                    else
                    {
                        filteredSegments.Add((seg.intensity, len));
                    }
                }
                
                // Post-check: Is the FIRST segment too short?
                // If yes, and we have a second, merge first into second? 
                // Or just keep it (start point is stricter).
                // Let's keep it simple.
            }
            
            // Check if row has data (any non-zero intensity)
            // Note: After filtering, we might have merged a tiny black dot into white space.
            bool rowHasData = filteredSegments.Any(s => s.intensity > 0);
            if (!rowHasData) continue;

            // Generate G-code
            // Move to start of row
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
                    if(lastG0) {
                        yield return $"G1 X{nextX:F3} S0";
                    }
                    else
                    {
                         yield return $"X{nextX:F3} S0";
                    }
                    lastG0 = false;
                }
                else
                {
                    // Burn
                    if(lastG0) {
                        yield return $"G1 X{nextX:F3} S{sValue:F0}";
                    }
                    else
                    {
                         yield return $"X{nextX:F3} S{sValue:F0}";
                    }
                    lastG0 = false;
                }
                
                currentX = nextX;
            }
        }
        
        if (disposeBmp) scanBmp.Dispose();
        
        yield return "G0 S0"; // Ensure off
    }
}
