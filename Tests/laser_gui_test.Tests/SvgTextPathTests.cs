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
                Assert.NotEmpty(result);
                var textObj = result.FirstOrDefault(r => r is LaserText);
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
            string xml = @"
                <text>
                    <textPath path=""M 0 0 L 100 100"">Direct</textPath>
                </text>";

            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                Assert.NotEmpty(result);
                var lp = result.OfType<LaserText>().FirstOrDefault();
                Assert.NotNull(lp);
                Assert.True(lp.Position.X >= 0 || lp.Size.Width > 0);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TestSideAttribute()
        {
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

                var lp1 = res1.OfType<LaserText>().First();
                var lp2 = res2.OfType<LaserText>().First();

                Assert.True(lp1.Position.X < 15, $"Normal: Expected X < 15, got {lp1.Position.X}");
                Assert.True(lp2.Position.X > 15, $"Right: Expected X > 15, got {lp2.Position.X}");
            }
            finally
            {
                if (File.Exists(path1)) File.Delete(path1);
                if (File.Exists(path2)) File.Delete(path2);
            }
        }
        
        [Fact]
        public void TestStartOffset()
        {
             string xml = @"
                <text>
                    <textPath path=""M 0 0 L 100 0"" startOffset=""50%"">A</textPath>
                </text>";

            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                var lp = result.OfType<LaserText>().First();
                Assert.True(lp.Position.X >= 10, $"Expected X >= 10, got {lp.Position.X}");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TestPathTransform()
        {
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
                var lt = res.OfType<LaserText>().FirstOrDefault();
                Assert.NotNull(lt);

                float totalHeight = lt.Size.Height;
                float totalWidth = lt.Size.Width;
                
                Assert.True(totalHeight > totalWidth, $"Expected H > W, got H={totalHeight}, W={totalWidth}");
                Assert.True(totalHeight > 10, $"Expected H > 10, got {totalHeight}");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TestLeadingSpaces()
        {
             string xmlNoSpace = @"
                <text>
                    <textPath path=""M 0 0 L 100 0"">A</textPath>
                </text>";
             
             string xmlSpaces = @"
                <text xml:space=""preserve"">
                    <textPath path=""M 0 0 L 100 0"">   A</textPath>
                </text>";

            string path1 = CreateTempSvg(xmlNoSpace);
            string path2 = CreateTempSvg(xmlSpaces);
            
            try
            {
                var res1 = SvgImporter.Import(path1);
                var lp1 = res1.OfType<LaserText>().First();
                
                var res2 = SvgImporter.Import(path2);
                var lp2 = res2.OfType<LaserText>().First();
                
                Assert.True(lp1.Position.X < 5, $"Expected near 0, got {lp1.Position.X}");
                Assert.True(lp2.Position.X > lp1.Position.X + 2, $"Expected shifted, got {lp2.Position.X}");
            }
            finally
            {
                if (File.Exists(path1)) File.Delete(path1);
                if (File.Exists(path2)) File.Delete(path2);
            }
        }
    }
}
