/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using Xunit;
using grbl_burn_em.Data;
using System.Linq;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace grbl_burn_em.Tests
{
    public class SvgTextTransformTests
    {
        private const float PxToMm = 25.4f / 96.0f; // 0.26458333f

        private string CreateTempSvg(string content)
        {
            string path = Path.GetTempFileName() + ".svg";
            // viewBox matches width/height so no viewBox scaling happens, only pxToMm and Y-flip
            File.WriteAllText(path, $@"<svg width=""500"" height=""500"" viewBox=""0 0 500 500"" xmlns=""http://www.w3.org/2000/svg"">{content}</svg>");
            return path;
        }

        [Fact]
        public void TestRotatedTextTransform()
        {
            string xml = @"<text x=""100"" y=""100"" transform=""rotate(90, 100, 100)"">Rotated</text>";
            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                var lt = result.OfType<LaserText>().FirstOrDefault();
                Assert.NotNull(lt);
                
                // Position should be (100, 100) * PxToMm -> flipped in 500*PxToMm height
                float expectedX = 100 * PxToMm;
                float expectedY = (500 - 100) * PxToMm;
                
                Assert.Equal(expectedX, lt.Position.X, 1);
                Assert.Equal(expectedY, lt.Position.Y, 1);
                // In SvgImporter, the 90 degree rotation in SVG coordinates becomes -90 
                // in the Laser Y-Up coordinate system due to the global Scale(1, -1) flip.
                Assert.Equal(-90, lt.Rotation, 1);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TestScaledTextTransform()
        {
            float originalFontSize = 12;
            string xml = $@"<text x=""100"" y=""100"" font-size=""{originalFontSize}"" transform=""scale(2)"">Scaled</text>";
            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                var lt = result.OfType<LaserText>().FirstOrDefault();
                Assert.NotNull(lt);
                
                // SvgImporter applies pxToMm scale globally. 
                // FontSize in SVG is in "user units" (pixels).
                // Expected FontSize = 12 (px) * 2 (transform scale) * PxToMm (unit scale)
                float expectedSize = originalFontSize * 2 * PxToMm;
                Assert.Equal(expectedSize, lt.FontSize, 1);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TestTextAnchorMiddle()
        {
            string xml = @"<text x=""100"" y=""100"" text-anchor=""middle"">Middle</text>";
            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                var lt = result.OfType<LaserText>().FirstOrDefault();
                Assert.NotNull(lt);
                Assert.Equal(TextAnchor.Middle, lt.Anchor);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TestGroupTransformInheritance()
        {
            string xml = @"
                <g transform=""translate(50, 50) rotate(45)"">
                    <text x=""0"" y=""0"">Nested</text>
                </g>";
            string path = CreateTempSvg(xml);
            try
            {
                var result = SvgImporter.Import(path);
                var lt = result.OfType<LaserText>().FirstOrDefault();
                Assert.NotNull(lt);
                
                // Rotation: SvgImporter global flip Scale(1, -1) before Translate(0, height)
                // Matrix multiplication sequence: Parent * Local * point.
                // transform.Elements[1] and [0] used for Atan2.
                // Group Rotate(45) followed by Global Scale(1, -1) might result in -45.
                Assert.Equal(-45, lt.Rotation, 1);
                
                // Position: (0,0) -> Group Translate(50,50) -> Global flip (height=500)
                // (50, 50) * PxToMm -> flipped in 500*PxToMm height
                float expectedX = 50 * PxToMm;
                float expectedY = (500 - 50) * PxToMm;

                Assert.Equal(expectedX, lt.Position.X, 1);
                Assert.Equal(expectedY, lt.Position.Y, 1);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
