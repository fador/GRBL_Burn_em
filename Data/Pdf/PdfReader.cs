using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace laser_gui_test.Data.Pdf
{
    public class PdfReader
    {
        private readonly byte[] _data;
        private readonly PdfTokenizer _tokenizer;
        private Dictionary<int, long> _xrefTable = new Dictionary<int, long>();
        private PdfDictionary _trailer = null!; // Initialized in Initialize()
        
        public PdfDictionary Trailer => _trailer;

        public PdfReader(string filePath)
        {
            _data = File.ReadAllBytes(filePath);
            _tokenizer = new PdfTokenizer(_data);
            Initialize();
        }

        public PdfReader(byte[] data)
        {
            _data = data;
            _tokenizer = new PdfTokenizer(_data);
            Initialize();
        }

        private void Initialize()
        {
            // 1. Check Header
            string header = Encoding.ASCII.GetString(_data, 0, Math.Min(_data.Length, 10));
            if (!header.StartsWith("%PDF-"))
                throw new InvalidDataException("Not a valid PDF file");

            // 2. Find Trailer
            // Search backwards for "startxref"
            int startXrefPos = FindStartXref();
            if (startXrefPos == -1) throw new InvalidDataException("Could not find startxref");
            
            _tokenizer.Seek(startXrefPos);
            // Expect "startxref"
            var kw = _tokenizer.ReadNextObject() as PdfKeyword;
            if (kw == null || kw.Keyword != "startxref") throw new InvalidDataException("Expected startxref");
            
            var num = _tokenizer.ReadNextObject() as PdfNumber;
            if (num == null) throw new InvalidDataException("Expected xref offset");
            
            int xrefOffset = (int)num.IntValue;
            
            ParseXref(xrefOffset);
        }

        private int FindStartXref()
        {
            // Scan backwards from end
            // Usually within last 1024 bytes
            int scanLen = Math.Min(_data.Length, 1024);
            int end = _data.Length;
            
            for (int i = end - 1; i >= end - scanLen; i--)
            {
                if (_data[i] == 's' && MatchAt(i, "startxref"))
                {
                    return i;
                }
            }
            return -1;
        }

        private bool MatchAt(int index, string text)
        {
            if (index + text.Length > _data.Length) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (_data[index + i] != text[i]) return false;
            }
            return true;
        }

        private void ParseXref(int offset)
        {
            _tokenizer.Seek(offset);
            var obj = _tokenizer.ReadNextObject();
            
            if (obj is PdfKeyword kw && kw.Keyword == "xref")
            {
                // Classic XRef
                while (true)
                {
                    var next = _tokenizer.ReadNextObject();
                    if (next is PdfKeyword tVal && tVal.Keyword == "trailer")
                    {
                        break;
                    }
                    
                    if (next is PdfNumber startId)
                    {
                        var countObj = _tokenizer.ReadNextObject() as PdfNumber;
                        if (countObj == null) break;
                        int count = (int)countObj.IntValue;
                        
                        for (int i = 0; i < count; i++)
                        {
                            // Each entry is 20 bytes: "nnnnnnnnnn ggggg n \r\n"
                             var offsetObj = _tokenizer.ReadNextObject() as PdfNumber;
                             var genObj = _tokenizer.ReadNextObject() as PdfNumber;
                             var typeObj = _tokenizer.ReadNextObject() as PdfKeyword; // 'n' or 'f'
                             
                             if (offsetObj != null && typeObj != null && typeObj.Keyword == "n")
                             {
                                 _xrefTable[(int)startId.IntValue + i] = offsetObj.IntValue;
                             }
                        }
                    }
                    else
                    {
                        // formatting error?
                         break;
                    }
                }
                
                // Read Trailer Dictionary
                var dict = _tokenizer.ReadNextObject() as PdfDictionary;
                if (dict == null) throw new InvalidDataException("Trailer dictionary missing");
                _trailer = dict;
            }
            else
            {
               // Might be XRef Stream (PDF 1.5)
               // The object at offset is a Stream Object with /Type /XRef
               // Currently not implementing XRefStream for simplicity.
               throw new NotImplementedException("XRef Stream not supported yet (PDF 1.5+) - please save as older PDF version.");
            }
        }

        public PdfObject? GetObject(PdfReference reference)
        {
            return GetObject(reference.ObjectNumber);
        }

        public PdfObject? GetObject(int objectIds)
        {
            if (!_xrefTable.ContainsKey(objectIds)) return null;
            
            long offset = _xrefTable[objectIds];
            _tokenizer.Seek((int)offset);
            
            // Should be "id gen obj"
            var id = _tokenizer.ReadNextObject() as PdfNumber;
            var gen = _tokenizer.ReadNextObject() as PdfNumber;
            var kw = _tokenizer.ReadNextObject() as PdfKeyword;
            
            if (kw == null || kw.Keyword != "obj")
            {
                 // Maybe Error
                 return null;
            }

            var obj = _tokenizer.ReadNextObject();
            
            if (obj is PdfDictionary dict)
            {
                // Check if Stream
                // Peek next keyword?
                var pos = _tokenizer.Position;
                var next = _tokenizer.ReadNextObject();
                if (next is PdfKeyword k && k.Keyword == "stream")
                {
                     // It is a stream
                     // Length is in dictionary
                     long length = 0;
                     var lenObj = Resolve(dict.Get("Length"));
                     if (lenObj is PdfNumber n) length = n.IntValue;
                     
                     var streamData = _tokenizer.ReadStreamBytes((int)length);
                     
                     // Handle Filters (FlateDecode)
                     var filter = Resolve(dict.Get("Filter"));
                     streamData = DecodeStream(streamData ?? new byte[0], filter); // We will implement basic decode
                     
                     return new PdfStream(dict, streamData);
                }
                else
                {
                    _tokenizer.Seek(pos); // Rewind
                }
            }
            
            return obj;
        }

        public PdfObject? Resolve(PdfObject? obj)
        {
            if (obj is PdfReference refObj) return GetObject(refObj);
            return obj;
        }

        private byte[] DecodeStream(byte[] data, PdfObject? filter)
        {
            if (data == null) return new byte[0];
            if (filter == null) return data;

            List<string> filters = new List<string>();
            if (filter is PdfName fn) filters.Add(fn.Name);
            else if (filter is PdfArray fa)
            {
                foreach(var item in fa.Items)
                    if (item is PdfName subn) filters.Add(subn.Name);
            }

            foreach(var f in filters)
            {
                if (f == "FlateDecode")
                {
                     data = FlateDecode(data);
                }
                else
                {
                    // Unsupported filter
                    Console.WriteLine($"Warning: Unsupported filter {f}");
                }
            }
            return data;
        }

        private byte[] FlateDecode(byte[] data)
        {
            // Simple Deflate wrapper
            // Skip ZLIB header (2 bytes: 78 9C usually) if present?
            // PDF FlateDecode expects Zlib stream. 
            // .NET DeflateStream expects raw deflate stream (no zlib header).
            // Need to strip header.
            
            if (data.Length < 2) return data;
            
            // Check for ZLIB header (RFC 1950)
            // CM = 8 (Deflate), CINFO = 7 (32K window). 8 << 4 | 7 = 0x78 (120) -> 0x78
            // Flags.
            // Usual header: 0x78 0x9C (Default compression) or 0x78 0xDA (Best), 0x78 0x01 (No/Low).
            
            // Simplistic strip (2 bytes)
            // This is hacky. 
            // Better: use a proper Zlib library or MemoryStream with DeflateStream?
            // DeflateStream in .NET Standard 2.0+ does NOT handle Zlib header.
            
            using (var ms = new MemoryStream(data, 2, data.Length - 2)) 
            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                try
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
                catch
                {
                    // Fallback: maybe no header?
                     using (var ms2 = new MemoryStream(data))
                     using (var ds2 = new System.IO.Compression.DeflateStream(ms2, System.IO.Compression.CompressionMode.Decompress))
                     using (var outMs2 = new MemoryStream())
                     {
                         try { ds2.CopyTo(outMs2); return outMs2.ToArray(); }
                         catch { return data; } // Failed
                     }
                }
            }
        }

        public List<PdfObject> GetPages()
        {
             var root = Resolve(_trailer.Get("Root")) as PdfDictionary;
             if (root == null) return new List<PdfObject>();
             
             var pagesObj = Resolve(root.Get("Pages")) as PdfDictionary;
             if (pagesObj == null) return new List<PdfObject>();
             
             var list = new List<PdfObject>();
             CollectPages(pagesObj, list);
             return list;
        }

        private void CollectPages(PdfDictionary pagesDict, List<PdfObject> acc)
        {
             var type = Resolve(pagesDict.Get("Type")) as PdfName;
             if (type == null) return;
             
             if (type.Name == "Page")
             {
                 acc.Add(pagesDict);
                 return;
             }
             if (type.Name == "Pages")
             {
                 var kids = Resolve(pagesDict.Get("Kids")) as PdfArray;
                 if (kids != null)
                 {
                     foreach(var kidRef in kids.Items)
                     {
                         var kid = Resolve(kidRef) as PdfDictionary;
                         if (kid != null) CollectPages(kid, acc);
                     }
                 }
             }
        }
    }
}
