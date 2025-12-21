using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using laser_gui_test.Data;
using laser_gui_test.Data.Pdf;
using Xunit;

namespace laser_gui_test.Tests
{
    public class PdfImporterTests
    {
        [Fact]
        public void TestTokenizer_Primitives()
        {
            var data = Encoding.ASCII.GetBytes("  123 -4.5 true false null /Name (String) <ABCD> [ 1 2 ] << /K 1 >> ");
            var tokenizer = new PdfTokenizer(data);

            var obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfNumber>(obj);
            Assert.Equal(123, ((PdfNumber)obj).IntValue);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfNumber>(obj);
            Assert.Equal(-4.5, ((PdfNumber)obj).RealValue);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfBoolean>(obj);
            Assert.True(((PdfBoolean)obj).Value);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfBoolean>(obj);
            Assert.False(((PdfBoolean)obj).Value);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfNull>(obj);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfName>(obj);
            Assert.Equal("Name", ((PdfName)obj).Name);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfString>(obj);
            Assert.Equal("String", ((PdfString)obj).Value);
            Assert.True(((PdfString)obj).IsGeneric);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfString>(obj); // Hex string
            Assert.False(((PdfString)obj).IsGeneric);
            // AB=171, CD? 0xAB 0xCD.
            Assert.Equal(2, ((PdfString)obj).Bytes.Length); 
            Assert.Equal(0xAB, ((PdfString)obj).Bytes[0]);
            Assert.Equal(0xCD, ((PdfString)obj).Bytes[1]);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfArray>(obj);
            Assert.Equal(2, ((PdfArray)obj).Items.Count);

            obj = tokenizer.ReadNextObject();
            Assert.IsType<PdfDictionary>(obj);
            Assert.Single(((PdfDictionary)obj).Entries);
        }


        [Fact]
        public void TestSimplePdfImport()
        {
            // Construct a minimal PDF in memory using ASCII encoding for byte precision
            using (var ms = new MemoryStream())
            using (var writer = new StreamWriter(ms, Encoding.ASCII))
            {
                writer.NewLine = "\n"; // Force LF
                
                writer.Write("%PDF-1.4\n");
                writer.Flush();
                
                // 1 0 obj: Catalog
                long offset1 = ms.Position;
                writer.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                writer.Flush();
                
                // 2 0 obj: Pages
                long offset2 = ms.Position;
                writer.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                writer.Flush();
                
                // 3 0 obj: Page
                long offset3 = ms.Position;
                writer.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>\nendobj\n");
                writer.Flush();
                
                // Content Stream Data
                string streamContent = "1 0 0 1 50 50 cm\n0 0 10 10 re\nS\n"; 
                
                // 4 0 obj: Content Stream
                long offset4 = ms.Position;
                writer.Write($"4 0 obj\n<< /Length {streamContent.Length} >>\nstream\n{streamContent}\nendstream\nendobj\n");
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
                    
                    Assert.Single(result.Objects);
                    var lp = result.Objects[0] as LaserPath;
                    Assert.NotNull(lp);
                    
                    double scale = 25.4 / 72.0;
                    
                    Assert.Equal(50 * scale, lp.Position.X, 1);
                    Assert.Equal(50 * scale, lp.Position.Y, 1);
                    Assert.Equal(10 * scale, lp.Size.Width, 1);
                    Assert.Equal(10 * scale, lp.Size.Height, 1);
                }
                finally
                {
                    if(File.Exists(pdfPath)) File.Delete(pdfPath);
                }
            }
        }
        
        [Fact]
        public void TestTextImport()
        {
             // Construct a minimal PDF with Text
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
                
                string streamContent = "BT /F1 12 Tf 10 20 Td (Hello) Tj ET"; 
                
                long offset4 = ms.Position; // Content
                writer.Write($"4 0 obj\n<< /Length {streamContent.Length} >>\nstream\n{streamContent}\nendstream\nendobj\n");
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
                    
                    Assert.Single(result.Objects);
                    var lt = result.Objects[0] as LaserText;
                    Assert.NotNull(lt);
                    Assert.Equal("Hello", lt.Text);
                    
                    double scale = 25.4 / 72.0;
                    Assert.Equal(10 * scale, lt.Position.X, 1);
                    // Updated Expectation: Position is back to Baseline.
                    // LaserText.Draw logic has been fixed to draw text UP from Baseline (inside the box).
                    Assert.Equal(20 * scale, lt.Position.Y, 1);
                    Assert.Equal(12 * scale, lt.FontSize, 1); // Check scaled font size
                }
                finally
                {
                    if(File.Exists(pdfPath)) File.Delete(pdfPath);
                }
            }
        }

    }
}
