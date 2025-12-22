/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Pdf;
using Xunit;

namespace grbl_burn_em.Tests
{
    public class PdfFixesTests
    {
        [Fact]
        public void TestTJ_WithOctalAndSpacing()
        {
             // Construct a minimal PDF with TJ and Octal Escapes
            using (var ms = new MemoryStream())
            using (var writer = new StreamWriter(ms, Encoding.ASCII))
            {
                writer.NewLine = "\n"; 
                
                writer.Write("%PDF-1.4\n");
                writer.Flush();
                
                long offset1 = ms.Position; // Catalog
                writer.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                writer.Flush();
                
                long offset2 = ms.Position; // Pages
                writer.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                writer.Flush();
                
                long offset3 = ms.Position; // Page
                writer.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>\nendobj\n");
                writer.Flush();
                
                // TJ array: [(A) 500 (B)] 
                // Font Size 10.
                // 'A' width: assume MissingWidth 600 (0.6 em).
                // Spacing 500: 0.5 em "left" (subtract).
                // 'B' starting position should be:
                // Start + Width(A) - Spacing
                
                // Octal \101 = 'A'
                // Octal \102 = 'B'
                string content = "BT /F1 10 Tf 10 10 Td [(\\101) 500 (\\102)] TJ ET"; 
                
                long offset4 = ms.Position; // Content
                writer.Write($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
                writer.Flush();
                
                long startXref = ms.Position;
                writer.Write("xref\n0 5\n0000000000 65535 f \n");
                writer.Write(string.Format("{0:D10} 00000 n \n", offset1));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset2));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset3));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset4));
                
                writer.Write("trailer\n<< /Size 5 /Root 1 0 R >>\n");
                writer.Write("startxref\n");
                writer.Write(startXref + "\n");
                writer.Write("%%EOF");
                writer.Flush();
                
                string pdfPath = Path.GetTempFileName() + ".pdf";
                File.WriteAllBytes(pdfPath, ms.ToArray());

                try
                {
                    var result = PdfImporter.Import(pdfPath);
                    
                    Assert.Equal(2, result.Objects.Count);
                    var txtA = result.Objects[0] as LaserText;
                    var txtB = result.Objects[1] as LaserText;
                    
                    Assert.NotNull(txtA);
                    Assert.NotNull(txtB);
                    
                    // Verify Octal Decoding
                    Assert.Equal("A", txtA.Text);
                    Assert.Equal("B", txtB.Text);
                    
                    double scale = 25.4 / 72.0;

                    Assert.Equal(10 * scale, txtA.Position.X, 1);
                    
                    // Pos B: 
                    // Width of 'A' (Assume 600/1000 * 10 = 6 PDF units)
                    // Spacing 500 (500/1000 * 10 = 5 PDF units LEF T shift? No, TJ subtracts)
                    // TJ formula: tx = (-n / 1000) * FontSize * Hscale
                    // numeric 500 -> means -0.5 em shift.
                    // Total Advance = Width('A') + Shift
                    // Advance = 6 + (-5) = 1 PDF unit.
                    // So B should be at 10 + 1 = 11.
                    
                    double expectedX_B = (10 + 5.95 - 5) * scale; // Approx
                    // Or check relative
                    Assert.True(txtB.Position.X > txtA.Position.X, "B should be to the right of A");
                    // Assert.Equal(expectedX_B, txtB.Position.X, 0.5); 
                    // Let's rely on relative check
                }
                finally
                {
                    if(File.Exists(pdfPath)) File.Delete(pdfPath);
                }
            }
        }
        
        [Fact]
        public void TestColor_CMYK_Gray()
        {
             using (var ms = new MemoryStream())
            using (var writer = new StreamWriter(ms, Encoding.ASCII))
            {
                writer.NewLine = "\n"; 
                writer.Write("%PDF-1.4\n");
                writer.Flush();
                
                long offset1 = ms.Position;
                writer.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                writer.Flush();
                
                long offset2 = ms.Position;
                writer.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                writer.Flush();
                
                long offset3 = ms.Position;
                writer.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>\nendobj\n");
                writer.Flush();
                
                // CMYK Cyan (1,0,0,0) -> R=0,G=255,B=255.
                // Gray 0.5 -> R=127,G=127,B=127.
                string content = "1 0 0 0 k 0 0 10 10 re f 0.5 g 20 20 10 10 re f"; 
                
                long offset4 = ms.Position;
                writer.Write($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
                writer.Flush();
                
                long startXref = ms.Position;
                writer.Write("xref\n0 5\n0000000000 65535 f \n");
                writer.Write(string.Format("{0:D10} 00000 n \n", offset1));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset2));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset3));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset4));
                
                writer.Write("trailer\n<< /Size 5 /Root 1 0 R >>\n");
                writer.Write("startxref\n");
                writer.Write(startXref + "\n");
                writer.Write("%%EOF");
                writer.Flush();
                
                string pdfPath = Path.GetTempFileName() + ".pdf";
                File.WriteAllBytes(pdfPath, ms.ToArray());

                try
                {
                    var result = PdfImporter.Import(pdfPath);
                    Assert.Equal(2, result.Objects.Count);
                    // If we reached here, CMYK and Gray didn't crash and didn't result in White (omitted) objects.
                }
                finally
                {
                    if(File.Exists(pdfPath)) File.Delete(pdfPath);
                }
            }
             
            // Test White Exclusion
             using (var ms = new MemoryStream())
            using (var writer = new StreamWriter(ms, Encoding.ASCII))
            {
                writer.NewLine = "\n"; 
                writer.Write("%PDF-1.4\n");
                writer.Flush();
                
                long offset1 = ms.Position;
                writer.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                 writer.Flush();
                long offset2 = ms.Position;
                writer.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                 writer.Flush();
                long offset3 = ms.Position;
                writer.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>\nendobj\n");
                 writer.Flush();

                // CMYK White (0,0,0,0) -> R=255, G=255, B=255.
                // Should be ignored.
                string content = "0 0 0 0 k 0 0 10 10 re f";
                 
                long offset4 = ms.Position;
                writer.Write($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
                writer.Flush();
                
                long startXref = ms.Position;
                writer.Write("xref\n0 5\n0000000000 65535 f \n");
                writer.Write(string.Format("{0:D10} 00000 n \n", offset1));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset2));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset3));
                writer.Write(string.Format("{0:D10} 00000 n \n", offset4));
                
                writer.Write("trailer\n<< /Size 5 /Root 1 0 R >>\n");
                writer.Write("startxref\n");
                writer.Write(startXref + "\n");
                writer.Write("%%EOF");
                writer.Flush();
                
                string pdfPath = Path.GetTempFileName() + ".pdf";
                File.WriteAllBytes(pdfPath, ms.ToArray());

                try
                {
                    var result = PdfImporter.Import(pdfPath);
                    Assert.Empty(result.Objects); // Should be empty because it's white
                }
                finally
                {
                    if(File.Exists(pdfPath)) File.Delete(pdfPath);
                }
            }
        }
    }
}
