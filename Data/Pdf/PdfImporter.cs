using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Added for Enumerable.Select etc used in code if needed
using System.Text;
using System.Drawing; // For PointF etc
using System.Drawing.Drawing2D; // For Matrix

// Add missing usings if references valid
// Provided context implies System.Drawing is available (LaserObject uses it)

namespace laser_gui_test.Data.Pdf
{
    public class PdfImporter
    {
        public static List<LaserObject> Import(string filePath)
        {
            var objects = new List<LaserObject>();
            try
            {
                var reader = new PdfReader(filePath);
                var pages = reader.GetPages();

                foreach (var pageObj in pages)
                {
                    if (pageObj is PdfDictionary page)
                    {
                        // Get Content
                        byte[] contentData = GetPageContent(reader, page);
                        if (contentData == null || contentData.Length == 0) continue;

                        // Get Resources
                        var resources = reader.Resolve(page.Get("Resources")) as PdfDictionary ?? new PdfDictionary();

                        // Parse Content
                        var parser = new PdfContentParser(reader, resources);
                        var pageObjects = parser.Parse(contentData);

                        // Apply Page MediaBox / offset?
                        // Usually parsing returns objects in Page Space (User Space).
                        // If we want to stack pages or just import all?
                        // Import all into same list.
                        
                        // CropBox / MediaBox
                        // If MediaBox is [0 0 595 842], content is usually there.
                        // We assume default origin.
                        
                        objects.AddRange(pageObjects);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error importing PDF: {ex.Message}");
                // In production might want to throw or log
            }
            return objects;
        }

        private static byte[] GetPageContent(PdfReader reader, PdfDictionary page)
        {
            var contents = reader.Resolve(page.Get("Contents"));
            
            if (contents is PdfStream stream)
            {
                return stream.Data;
            }
            else if (contents is PdfArray arr)
            {
                // Concatenate streams
                List<byte> combined = new List<byte>();
                foreach (var item in arr.Items)
                {
                    var part = reader.Resolve(item);
                    if (part is PdfStream ps)
                    {
                        combined.AddRange(ps.Data);
                        // Add whitespace separator safe measure
                        combined.Add((byte)' '); 
                    }
                }
                return combined.ToArray();
            }
            return new byte[0];
        }
    }
}
