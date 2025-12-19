using Xunit;
using laser_gui_test.Data;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Drawing;

namespace laser_gui_test.Tests
{
    public class SvgTextPathTests
    {
        private string CreateTempSvg(string content)
        {
            string path = Path.GetTempFileName() + ".svg";
            File.WriteAllText(path, $@"<svg xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"">{content}</svg>");
            return path;
        }

        [Fact]
        public void TestTextPathWithHref()
        {
            // Case: textPath references a path by ID
            // Path is a horizontal line (0,0) to (100,0)
            // Text should be warped along it (effectively unchanged if 'align', but stretched/flattened by PathWarp)
            
            string xml = @"
                <defs>
                    <path id=""MyPath"" d=""M 0 0 L 100 0"" />
                </defs>
                <text>
                    <textPath href=""#MyPath"">Hello</textPath>
                </text>";

            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                
                // Should contain at least one LaserPath (the text converted to paths)
                // Note: The 'path' element in defs is usually not imported unless visible/used directly. 
                // SvgImporter loops over doc.Root.Elements(). Defs might be skipped or parsed.
                // Depending on SvgImporter loop...
                // If it loops all elements and ignores defs? 
                
                // Let's check result count.
                // We expect the 'text' to produce multiple LaserPaths (one for each letter component maybe? or one unified?)
                // PathWarp.CreateWarpedPath returns ONE GraphicsPath.
                // AddGraphicsPath splits it into subpaths (contours).
                
                Assert.NotEmpty(result);
                
                // The text "Hello" has multiple contours.
                var textObj = result.FirstOrDefault(r => r is LaserPath);
                Assert.NotNull(textObj);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TestTextPathWithDirectPath()
        {
            // Case: textPath has 'path' attribute
            string xml = @"
                <text>
                    <textPath path=""M 0 0 L 100 100"">Direct</textPath>
                </text>";

            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                Assert.NotEmpty(result);
                var lp = result.OfType<LaserPath>().FirstOrDefault();
                Assert.NotNull(lp);
                
                // Check bounds roughly
                // Path goes 0,0 to 100,100.
                // Text 'Direct' should be along diagonal.
                // Max X should be > 0. Max Y should be > 0.
                Assert.True(lp.Position.X >= 0 || lp.Size.Width > 0);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TestSideAttribute()
        {
            // Case: side="right" reverses the path
            // Normal path: Left->Right (0,0 to 100,0)
            // Reversed: Right->Left (100,0 to 0,0)
            
            string xmlNormal = @"
                <text>
                    <textPath path=""M 0 0 L 100 0"">A</textPath>
                </text>";
            
            string xmlRight = @"
                <text>
                    <textPath path=""M 0 0 L 100 0"" side=""right"">A</textPath>
                </text>";

            string path1 = CreateTempSvg(xmlNormal);
            string path2 = CreateTempSvg(xmlRight);
            
            try
            {
                var res1 = SvgImporter.Import(path1);
                var res2 = SvgImporter.Import(path2);

                var lp1 = res1.OfType<LaserPath>().First();
                var lp2 = res2.OfType<LaserPath>().First();

                Assert.True(lp1.Position.X < 15, $"Normal: Expected X < 15 (scaled to mm), got {lp1.Position.X}");
                
                // Reversed path should move text towards the new start (which was old end, 100).
                // 100px = 26.458mm. Text at end ~ 24mm.
                Assert.True(lp2.Position.X > 20, $"Right (Reversed): Expected X > 20, got {lp2.Position.X}");
            }
            finally
            {
                File.Delete(path1);
                File.Delete(path2);
            }
        }
        
        [Fact]
        public void TestStartOffset()
        {
             // Case: startOffset="50%"
             // Path 0,0 to 100,0.
             // Text 'A' should start at 50,0.
             string xml = @"
                <text>
                    <textPath path=""M 0 0 L 100 0"" startOffset=""50%"">A</textPath>
                </text>";

            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                var lp = result.OfType<LaserPath>().First();
                
                // 'A' should be roughly at X=50 in SVG units (pixels).
                // SvgImporter converts to mm: 50 * (25.4/96) = 13.2 mm.
                // Expected X >= 10.
                Assert.True(lp.Position.X >= 10, $"Expected X around 13 (mm), got {lp.Position.X}");
            }
            finally
            {
                File.Delete(path);
            }
        }
        [Fact]
        public void TestPathTransform()
        {
             // Case: The referenced path has a transform. 
             // Path is horizontal 0,0 to 100,0.
             // Transformed: Rotate 90 deg around 0,0 -> 0,0 to 0,100.
             // Text should follow the vertical line.
             
             string xml = @"
                <defs>
                    <path id=""RotatePath"" d=""M 0 0 L 100 0"" transform=""rotate(90)"" />
                </defs>
                <text>
                    <textPath href=""#RotatePath"">VerticalTextIsLonger</textPath>
                </text>";

            string path = CreateTempSvg(xml);
            try
            {
                var res = SvgImporter.Import(path);
                
                // "VerticalTextIsLonger" produces multiple paths
                var lps = res.OfType<LaserPath>().ToList();
                Assert.NotEmpty(lps);

                float minX = lps.Min(p => p.Position.X);
                float maxX = lps.Max(p => p.Position.X + p.Size.Width);
                float minY = lps.Min(p => p.Position.Y);
                float maxY = lps.Max(p => p.Position.Y + p.Size.Height);
                float totalHeight = maxY - minY;
                float totalWidth = maxX - minX;

                // Original path max X is 100 (approx 26mm).
                // Rotated path is vertical 0,0 to 0,100.
                // Text should follow the vertical line (Y axis).
                
                // Assert it is taller than it is wide (vertical orientation)
                Assert.True(totalHeight > totalWidth, $"Expected Vertical Orientation (Height > Width), got Height={totalHeight}, Width={totalWidth}");
                
                // Assert reasonable height (> 10mm)
                Assert.True(totalHeight > 10, $"Expected TotalHeight > 10 (vertical), got {totalHeight}");
            }
            finally
            {
                File.Delete(path);
            }
        }
        [Fact]
        public void TestLeadingSpaces()
        {
             // Case: Leading spaces should shift the text if xml:space="preserve" is used or if we handle it correctly.
             // User wants to displace text using spaces.
             
             string xmlNoSpace = @"
                <text>
                    <textPath path=""M 0 0 L 100 0"">A</textPath>
                </text>";
             
             // Using xml:space='preserve' to ensure spaces are kept.
             // Note: The Importer must respect this.
             string xmlSpaces = @"
                <text xml:space=""preserve"">
                    <textPath path=""M 0 0 L 100 0"">   A</textPath>
                </text>";

            string path1 = CreateTempSvg(xmlNoSpace);
            string path2 = CreateTempSvg(xmlSpaces);
            
            try
            {
                var res1 = SvgImporter.Import(path1);
                var lp1 = res1.OfType<LaserPath>().First();
                
                var res2 = SvgImporter.Import(path2);
                var lp2 = res2.OfType<LaserPath>().First();
                
                // lp1 'A' should be near 0.
                Assert.True(lp1.Position.X < 5, $"Expected 'A' near 0, got {lp1.Position.X}");
                
                // lp2 '   A' should be shifted right relative to lp1.
                // If trimmed, they would be identical.
                // 3 spaces width depends on font, but definitely > 0.
                
                Assert.True(lp2.Position.X > lp1.Position.X + 2, 
                    $"Expected shifted text with spaces. NoSpace X: {lp1.Position.X}, Space X: {lp2.Position.X}");
            }
            finally
            {
                File.Delete(path1);
                File.Delete(path2);
            }
        }
    }
}
