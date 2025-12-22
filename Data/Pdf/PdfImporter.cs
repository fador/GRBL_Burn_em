using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Added for Enumerable.Select etc used in code if needed
using System.Text;
using System.Drawing; // For PointF etc
using System.Drawing.Drawing2D; // For Matrix

// Add missing usings if references valid
// Provided context implies System.Drawing is available (LaserObject uses it)

namespace grbl_burn_em.Data.Pdf
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
            
            try 
            {
                var pages = reader.GetPages();
                if (pages.Count == 0)
                {
                    result.Warnings.Add("No pages found in PDF document (GetPages returned empty).");
                }

                foreach (var pageObj in pages)
                {
                    if (pageObj is PdfDictionary page)
                    {
                        // Get Content
                        byte[] contentData = GetPageContent(reader, page, result.Warnings);
                        if (contentData == null || contentData.Length == 0) 
                        {
                            result.Warnings.Add("Page content stream is empty.");
                            continue;
                        }

                        // Get Resources
                        var resources = reader.Resolve(page.Get("Resources")) as PdfDictionary ?? new PdfDictionary();

                        // Get MediaBox (Default Clip)
                        var mediaBoxArr = reader.Resolve(page.Get("MediaBox")) as PdfArray;
                        RectangleF mediaBox = RectangleF.Empty; 
                        if (mediaBoxArr != null && mediaBoxArr.Items.Count >= 4)
                        {
                            float mx = (float)((mediaBoxArr.Items[0] as PdfNumber)?.RealValue ?? 0);
                            float my = (float)((mediaBoxArr.Items[1] as PdfNumber)?.RealValue ?? 0);
                            float mw = (float)((mediaBoxArr.Items[2] as PdfNumber)?.RealValue ?? 0) - mx;
                            float mh = (float)((mediaBoxArr.Items[3] as PdfNumber)?.RealValue ?? 0) - my;
                            mediaBox = new RectangleF(mx, my, mw, mh);
                        }

                        // Parse Content
                        var parser = new PdfContentParser(reader, resources, mediaBox);
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
            
            
            // Collect all reader warnings (including those from GetPages)
            result.Warnings.AddRange(reader.Warnings);
            
            // Convert Units: PDF is 72 DPI (Points). Laser is usually MM.
            // 1 Point = 1/72 Inch. 1 Inch = 25.4 mm.
            // Scale = 25.4 / 72.0 = 0.3527777...
            float scale = 25.4f / 72.0f;
            
            foreach (var obj in objects)
            {
                obj.Position = new PointF(obj.Position.X * scale, obj.Position.Y * scale);
                obj.Size = new SizeF(obj.Size.Width * scale, obj.Size.Height * scale);
                
                if (obj is LaserPath lp)
                {
                    for(int i=0; i<lp.Points.Count; i++)
                    {
                        lp.Points[i] = new PointF(lp.Points[i].X * scale, lp.Points[i].Y * scale);
                    }
                }
                // Text font size?
                if (obj is LaserText lt)
                {
                    lt.FontSize *= scale;
                    // Ensure Size matches scaled FontSize
                    obj.Size = new SizeF(obj.Size.Width * scale, lt.FontSize); 
                }
            }
            
            // Final Safety Filter: Remove objects that are effectively zero-dimensional
            // User reported "text box width and height are 0" for invisible text.
            // Be careful not to remove lines (W>0, H=0) or Vertical lines (W=0, H>0).
            // Filter only if BOTH are roughly zero.
            objects.RemoveAll(o => Math.Abs(o.Size.Width) < 0.001f && Math.Abs(o.Size.Height) < 0.001f);
            
            result.Objects = objects;
            return result;
        }

        private static byte[] GetPageContent(PdfReader reader, PdfDictionary page, List<string> warnings)
        {
            var contentsRef = page.Get("Contents");
            if (contentsRef == null)
            {
                 // Empty page is valid but worth noting if debugging
                 warnings.Add("Page has no Contents (Key not found).");
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
