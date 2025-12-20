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
    public class PdfImportResult
    {
        public List<LaserObject> Objects { get; set; } = new List<LaserObject>();
        public List<string> Warnings { get; set; } = new List<string>();
        public bool Success => Objects.Count > 0;
    }

    public class PdfImporter
    {
        public static PdfImportResult Import(string filePath)
        {
            var result = new PdfImportResult();
            var reader = new PdfReader(filePath);
            var objects = new List<LaserObject>();
            
            // Collect reader warnings if any (Reader needs to expose them)
            result.Warnings.AddRange(reader.Warnings); 

            try 
            {
                var pages = reader.GetPages();

                foreach (var pageObj in pages)
                {
                    if (pageObj is PdfDictionary page)
                    {
                        // Get Content
                        byte[] contentData = GetPageContent(reader, page, result.Warnings);
                        if (contentData == null || contentData.Length == 0) 
                        {
                            // Already warned in GetPageContent if unexpected type, but if just empty:
                            // continue
                            // Warning is added inside GetPageContent
                            continue;
                        }

                        // Get Resources
                        var resources = reader.Resolve(page.Get("Resources")) as PdfDictionary ?? new PdfDictionary();

                        // Parse Content
                        var parser = new PdfContentParser(reader, resources);
                        var pageObjects = parser.Parse(contentData);
                        
                        // Collect parser warnings
                        result.Warnings.AddRange(parser.Warnings);

                        objects.AddRange(pageObjects);
                    }
                }
            }
            catch(Exception ex)
            {
                result.Warnings.Add($"Critical error during import: {ex.Message}");
            }
            
            result.Objects = objects;
            return result;
        }

        private static byte[] GetPageContent(PdfReader reader, PdfDictionary page, List<string> warnings)
        {
            var contentsRef = page.Get("Contents");
            if (contentsRef == null)
            {
                 // Empty page is valid but worth noting if debugging
                 // warnings.Add("Page has no Contents.");
                 return new byte[0];
            }

            var contents = reader.Resolve(contentsRef);
            
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
                    else
                    {
                        warnings.Add($"Page Content array item was not a stream: {part?.GetType().Name}");
                    }
                }
                return combined.ToArray();
            }
            
            warnings.Add($"Page Contents was neither Stream nor Array: {contents?.GetType().Name}");
            return new byte[0];
        }
    }
}
