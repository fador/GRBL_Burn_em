/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace grbl_burn_em.Data.Pdf
{
    public class PdfReader
    {
        private readonly byte[] _data;
        private readonly PdfTokenizer _tokenizer;
        private struct XRefEntry
        {
            public long Offset; 
            public int StreamId; // For Object Streams
            public int Index;    // For Object Streams
            public bool IsCompressed;
        }

        private Dictionary<int, XRefEntry> _xrefTable = new Dictionary<int, XRefEntry>();
        private PdfDictionary _trailer = null!; // Initialized in Initialize()
        
        public List<string> Warnings { get; } = new List<string>();
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

        private void ParseXref(long offset)
        {
            _tokenizer.Seek((int)offset);
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
                                 _xrefTable[(int)startId.IntValue + i] = new XRefEntry 
                                 { 
                                     Offset = (long)offsetObj.IntValue, 
                                     IsCompressed = false 
                                 };
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
                
                // Merge with existing trailer if any (for linearization or updates)
                if (_trailer == null) 
                {
                    _trailer = dict;
                    if (_trailer.ContainsKey("Encrypt"))
                    {
                        Warnings.Add("PDF is encrypted. Import may fail or produce garbage output.");
                    }
                }
                else 
                {
                    // Merge logic can be added here if needed
                    // For now, simpler to just keep the first/last?
                    // Usually we process from latest update backwards.
                    // But here we might be strictly linear. 
                    // Let's assume one trailer or chain.
                }

                if (dict.ContainsKey("Prev"))
                {
                    var prev = dict.Get("Prev") as PdfNumber;
                    if (prev != null) ParseXref((long)prev.IntValue);
                }
            }
            else
            {
               // Check if it is an XRef Stream (PDF 1.5+)
               // The object at offset should be a Stream Object with /Type /XRef (but tokenizer returns "obj" then Dictionary then "stream")
               // Actually we likely read "id gen obj".
                // Rewind to check what we read?
                // Depending on implementation, Tokenizer.ReadNextObject() reads one token.
                // If it was "xref", it's a keyword.
                // If it was "123", it's a number (id).
                
                // If we are at an XRef Stream, the file content is:
                // id gen obj
                // << /Type /XRef ... >>
                // stream ... endstream
                
                // So 'obj' above might be the ID if we just did ReadNextObject().
                // But wait, "xref" is a keyword. "123" is a Number.
                
                if (obj is PdfNumber id)
                {
                    var gen = _tokenizer.ReadNextObject() as PdfNumber;
                    var objKw = _tokenizer.ReadNextObject() as PdfKeyword;
                    if (objKw != null && objKw.Keyword == "obj")
                    {
                        var dictToken = _tokenizer.ReadNextObject();
                        if (dictToken is PdfDictionary dict)
                        {
                            // Check type
                            var type = Resolve(dict.Get("Type")) as PdfName;
                            if (type != null && type.Name == "XRef")
                            {
                                // It is an XRef Stream
                                ParseXrefStream(dict);
                                if (_trailer == null) _trailer = dict; // XRef stream dict doubles as trailer
                                
                                if (dict.ContainsKey("Prev"))
                                {
                                    var prev = dict.Get("Prev") as PdfNumber;
                                    if (prev != null) ParseXref((long)prev.IntValue);
                                }
                                return;
                            }
                        }
                    }
                }
                
                // Unknown
               throw new NotImplementedException($"Unknown XRef format or object type at {offset}: {obj?.GetType().Name} {obj}");
            }
        }

        private void ParseXrefStream(PdfDictionary dict)
        {
            // To be implemented
            // Needs to read 'W' array, 'Index' array, and decode stream
            // Populate _xrefTable
            
            // For now, we need to read the stream data first.
            // The Dictionary is already read.
            // Next should be 'stream' keyword from Tokenizer?
            // Wait, we read the Dictionary in ParseXref. 
            // The PdfTokenizer logic for ReadDictionary does NOT consume 'stream'.
            // But PdfReader.GetObject DOES look for stream.
            
            // We can reuse logic or call manual stream read.
            var next = _tokenizer.ReadNextObject();
            if (next is PdfKeyword kw && kw.Keyword == "stream")
            {
                // Is stream
                 long length = 0;
                 var lenObj = Resolve(dict.Get("Length"));
                 if (lenObj is PdfNumber n) length = (long)n.IntValue;
                 
                var streamData = _tokenizer.ReadStreamBytes((int)length);
                // var filter = Resolve(dict.Get("Filter")); // Handled inside DecodeStream now
                streamData = DecodeStream(streamData ?? new byte[0], dict);
                
                ProcessXRefStreamData(dict, streamData);
            }
        }

        private void ProcessXRefStreamData(PdfDictionary dict, byte[] data)
        {
            // 1. Get 'W' array (Field Widths)
            var wArr = Resolve(dict.Get("W")) as PdfArray;
            if (wArr == null) 
            {
                Warnings.Add("XRef Stream missing 'W' array.");
                return;
            }
            Warnings.Add($"XRef Stream W: [{string.Join(", ", wArr.Items)}]");
            
            // 2. Get 'Index' array (Optional, default [0 Size])
            var indArr = Resolve(dict.Get("Index")) as PdfArray;
            List<int> indices = new List<int>();
            if (indArr != null)
            {
                for(int i=0; i<indArr.Items.Count; i+=2)
                {
                    if (i+1 < indArr.Items.Count)
                    {
                        int start = (int)((PdfNumber)indArr.Items[i]).IntValue;
                        int count = (int)((PdfNumber)indArr.Items[i+1]).IntValue;
                        for(int k=0; k<count; k++) indices.Add(start + k);
                    }
                }
            }
            else
            {
                // Default: 0 to Size-1
                var sizeObj = Resolve(dict.Get("Size")) as PdfNumber;
                if (sizeObj != null)
                {
                    int size = (int)sizeObj.IntValue;
                    for(int k=0; k<size; k++) indices.Add(k);
                }
                else
                {
                     Warnings.Add("XRef Stream missing 'Size' and 'Index'.");
                }
            }

            // 3. Parse Data
            // W usually has 3 entries. 
            // entry len = sum(W)
            int[] widths = wArr.Items.Select(x => (int)((PdfNumber)x).IntValue).ToArray();
            int stride = widths.Sum();
            
            if (data.Length < stride * indices.Count) 
            {
                Warnings.Add($"XRef Stream Data Underflow. Expected {stride * indices.Count} bytes (Stride {stride} * Count {indices.Count}), got {data.Length}.");
                // return; 
            }
            
            int offset = 0;
            foreach(int objId in indices)
            {
                if (offset + stride > data.Length) break;
                
                // Read Fields
                long[] fields = new long[widths.Length];
                for(int k=0; k<widths.Length; k++)
                {
                    long val = 0;
                    for(int b=0; b<widths[k]; b++)
                    {
                        val = (val << 8) + data[offset++];
                    }
                    fields[k] = val;
                }
                
                // Interpret Type
                // First field is Type. If W[0] is 0, default type is 1.
                int type = widths[0] == 0 ? 1 : (int)fields[0];
                
                switch(type)
                {
                    case 0: // Free (Generation is field 2)
                        break;
                    case 1: // Uncompressed (Offset is field 1, Gen is field 2)
                        _xrefTable[objId] = new XRefEntry 
                        { 
                            Offset = fields[1], 
                            IsCompressed = false 
                        };
                        break;
                    case 2: // Compressed (Stream ID is field 1, Index is field 2)
                        _xrefTable[objId] = new XRefEntry
                        {
                            StreamId = (int)fields[1],
                            Index = (int)fields[2],
                            IsCompressed = true
                        };
                        break;
                }
            }
        }

        public PdfObject? GetObject(PdfReference reference)
        {
            return GetObject(reference.ObjectNumber);
        }

        public PdfObject? GetObject(int objectIds)
        {
            if (!_xrefTable.ContainsKey(objectIds)) 
            {
                Warnings.Add($"GetObject({objectIds}) failed. ID not found in XRef table.");
                return null;
            }
            
            var entry = _xrefTable[objectIds];
            
            if (entry.IsCompressed)
            {
                // Compressed Object in Object Stream
                return ReadObjectFromStream(entry.StreamId, entry.Index);
            }
            else
            {
                long offset = entry.Offset;
                _tokenizer.Seek((int)offset);
                
                // Should be "id gen obj"
                var id = _tokenizer.ReadNextObject() as PdfNumber;
                var gen = _tokenizer.ReadNextObject() as PdfNumber;
                var kw = _tokenizer.ReadNextObject() as PdfKeyword;
                
                if (kw == null || kw.Keyword != "obj")
                {
                     Warnings.Add($"GetObject({objectIds}) failed. Expected 'obj' at offset {offset}, found '{kw?.Keyword}' (Type: {kw?.GetType().Name})");
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
                         // We must save position because Resolve(Length) or Resolve(Filter) might Seek!
                         long streamDataStart = _tokenizer.Position;
                         
                         long length = 0;
                         var rawLen = dict.Get("Length");
                         var lenObj = Resolve(rawLen); // This might seek!
                         if (lenObj is PdfNumber n) length = (long)n.IntValue;
                         
                         if (length <= 0)
                         {
                             Warnings.Add($"Stream {objectIds} Length issue. Val: {length}. Raw: {rawLen} ({rawLen?.GetType().Name}). Res: {lenObj} ({lenObj?.GetType().Name})");
                         }
                         
                         // Restore position to start of stream data
                         _tokenizer.Seek((int)streamDataStart);
                         
                         var streamData = _tokenizer.ReadStreamBytes((int)length);
                         
                         if (streamData == null || streamData.Length != length)
                         {
                              long debugOffset = entry.Offset;
                              string startBytes = streamData != null && streamData.Length > 0 ? BitConverter.ToString(streamData, 0, Math.Min(5, streamData.Length)) : "None";
                              Warnings.Add($"Stream {objectIds} Read Issue. Expected {length}, read {streamData?.Length ?? -1}. Offset: {debugOffset}. FileLen: {_data.Length}. Header: {startBytes}");
                         }
                         
                         // Handle Filters (FlateDecode)
                         // var filter = Resolve(dict.Get("Filter")); // Handled in DecodeStream
                         streamData = DecodeStream(streamData ?? new byte[0], dict); 
                         
                         return new PdfStream(dict, streamData);
                    }
                    else
                    {
                        _tokenizer.Seek(pos); // Rewind
                    }
                }
                
                return obj;
            }
        }

        private PdfObject? ReadObjectFromStream(int streamId, int index)
        {
            // Get the Object Stream
            var objStream = GetObject(streamId) as PdfStream;
            if (objStream == null) 
            {
                Warnings.Add($"ReadObjectFromStream: Failed to retrieval Object Stream {streamId}");
                return null;
            }
            
            // Check Type
            var type = Resolve(objStream.Dictionary.Get("Type")) as PdfName;
            if (type == null || type.Name != "ObjStm") 
            {
                Warnings.Add($"ReadObjectFromStream: Object {streamId} is not an ObjStm (Type: {type?.Name})");
                return null;
            }
            
            // Parse First and N
            var nObj = Resolve(objStream.Dictionary.Get("N")) as PdfNumber;
            var firstObj = Resolve(objStream.Dictionary.Get("First")) as PdfNumber;
            if (nObj == null || firstObj == null) 
            {
                Warnings.Add($"ReadObjectFromStream: ObjStm {streamId} missing N or First");
                return null;
            }
            
            int n = (int)nObj.IntValue;
            int first = (int)firstObj.IntValue;
            
            // The data contains: N pairs of integers (objNum, offset)
            // Then the objects starting at 'first' byte offset
            
            // We need to parse just enough to find our index
            var streamTokenizer = new PdfTokenizer(objStream.Data);
             
            int targetOffset = -1;
            int scanned = 0;
            for(int i=0; i<n; i++)
            {
                scanned = i;
                var numObj = streamTokenizer.ReadNextObject() as PdfNumber;
                var offObj = streamTokenizer.ReadNextObject() as PdfNumber;
                
                if (numObj == null || offObj == null) break;
                
                if (i == index) // Index in the stream match (not ObjId matching, strictly index)
                {
                    // Verify logic: "The object number of this object is stored in the stream... but the XRef table gave us the Index."
                    // Yes, entry.Index refers to the i-th object in the stream.
                    targetOffset = (int)offObj.IntValue;
                    break;
                }
            }
            
            if (targetOffset != -1)
            {
                streamTokenizer.Seek(first + targetOffset);
                return streamTokenizer.ReadNextObject();
            }
            
            Warnings.Add($"ReadObjectFromStream: Index {index} not found in ObjStm {streamId} (Count {n}). Scanned {scanned} pairs from {objStream.Data.Length} bytes.");
            return null;
        }

        public PdfObject? Resolve(PdfObject? obj)
        {
            if (obj is PdfReference refObj) return GetObject(refObj);
            return obj;
        }

        private byte[] DecodeStream(byte[] data, PdfDictionary dict)
        {
            if (data == null) return new byte[0];
            
            var filterObj = Resolve(dict.Get("Filter"));
            var parmsObj = Resolve(dict.Get("DecodeParms"));
            
            if (filterObj == null) return data;

            List<string> filters = new List<string>();
            List<PdfDictionary?> decodeParms = new List<PdfDictionary?>();

            if (filterObj is PdfName fn) 
            {
                filters.Add(fn.Name);
                if (parmsObj is PdfDictionary pd) decodeParms.Add(pd);
                else decodeParms.Add(null);
            }
            else if (filterObj is PdfArray fa)
            {
                for(int i=0; i<fa.Items.Count; i++)
                {
                    if (fa.Items[i] is PdfName subn) 
                    {
                        filters.Add(subn.Name);
                        // Parms matches array index
                        if (parmsObj is PdfArray pa && i < pa.Items.Count && pa.Items[i] is PdfDictionary pd)
                            decodeParms.Add(pd);
                        else
                            decodeParms.Add(null);
                    }
                }
            }

            for(int i=0; i<filters.Count; i++)
            {
                var f = filters[i];
                var p = decodeParms[i];
                
                if (f == "FlateDecode")
                {
                     data = FlateDecode(data);
                     if (p != null) data = ApplyPredictor(data, p);
                }
                else
                {
                    // Unsupported filter
                    Warnings.Add($"Unsupported stream filter: {f}");
                    Console.WriteLine($"Warning: Unsupported filter {f}");
                }
            }
            if (data.Length == 0 && filters.Count > 0)
            {
                 Warnings.Add($"DecodeStream produced 0 bytes. Filters: {string.Join(",", filters)}.");
            }
            return data;
        }

        private byte[] ApplyPredictor(byte[] data, PdfDictionary parms)
        {
            var predictorObj = Resolve(parms.Get("Predictor")) as PdfNumber;
            int predictor = predictorObj != null ? (int)predictorObj.IntValue : 1;
            
            if (predictor <= 1) return data; // No prediction
            
            var columnsObj = Resolve(parms.Get("Columns")) as PdfNumber;
            var colorsObj = Resolve(parms.Get("Colors")) as PdfNumber;
            var bitsObj = Resolve(parms.Get("BitsPerComponent")) as PdfNumber; // default 8
            
            int columns = columnsObj != null ? (int)columnsObj.IntValue : 1;
            int colors = colorsObj != null ? (int)colorsObj.IntValue : 1;
            int bpc = bitsObj != null ? (int)bitsObj.IntValue : 8;
            
            if (predictor >= 10)
            {
                return UnfilterPng(data, predictor, columns, colors, bpc);
            }
            
            if (predictor == 2)
            {
                return UnfilterTiff2(data, columns, colors, bpc);
            }
            
            // TIFF Predictor 2 unsupported for now
            Warnings.Add($"Unsupported Predictor {predictor}");
            return data;
        }

        private byte[] UnfilterTiff2(byte[] data, int columns, int colors, int bpc)
        {
            if (bpc != 8) 
            {
                Warnings.Add($"TIFF Predictor 2 only implemented for 8 bpc. Got {bpc}.");
                return data; 
            }
            
            int rowBytes = columns * colors; // For 8 bpc
            
            // Loop rows
            for (int i = 0; i < data.Length; i += rowBytes)
            {
                int rowStart = i;
                int rowEnd = Math.Min(i + rowBytes, data.Length);
                
                // Horizontal Differencing
                for (int j = colors; j < rowEnd - rowStart; j++)
                {
                     // data[rowStart + j] += data[rowStart + j - colors];
                     int idx = rowStart + j;
                     data[idx] = (byte)(data[idx] + data[idx - colors]);
                }
            }
            return data;
        }

        private byte[] UnfilterPng(byte[] data, int predictor, int columns, int colors, int bpc)
        {
            // PNG Predictor logic
            // BytesPerPixel
            int bpp = (colors * bpc + 7) / 8;
            int rowBytes = (columns * colors * bpc + 7) / 8;
            
            using (var memoryStream = new MemoryStream(data))
            using (var outStream = new MemoryStream())
            {
                byte[] prevRow = new byte[rowBytes];
                byte[] currRow = new byte[rowBytes];
                
                while (memoryStream.Position < memoryStream.Length)
                {
                    int filterType = memoryStream.ReadByte();
                    if (filterType == -1) break;
                    
                    int read =0;
                    while(read < rowBytes)
                    {
                        int r = memoryStream.Read(currRow, read, rowBytes - read);
                        if (r <= 0) break;
                        read += r;
                    }
                    
                    // Unfilter
                    byte[] rawRow = new byte[rowBytes];
                    for (int i = 0; i < rowBytes; i++)
                    {
                        byte x = currRow[i];
                        byte a = (i >= bpp) ? rawRow[i - bpp] : (byte)0;
                        byte b = prevRow[i];
                        byte c = (i >= bpp) ? prevRow[i - bpp] : (byte)0;
                        
                        switch (filterType)
                        {
                            case 0: // None
                                rawRow[i] = x;
                                break;
                            case 1: // Sub
                                rawRow[i] = (byte)(x + a);
                                break;
                            case 2: // Up
                                rawRow[i] = (byte)(x + b);
                                break;
                            case 3: // Average
                                rawRow[i] = (byte)(x + (a + b) / 2);
                                break;
                            case 4: // Paeth
                                int p = a + b - c;
                                int pa = Math.Abs(p - a);
                                int pb = Math.Abs(p - b);
                                int pc = Math.Abs(p - c);
                                if (pa <= pb && pa <= pc) rawRow[i] = (byte)(x + a);
                                else if (pb <= pc) rawRow[i] = (byte)(x + b);
                                else rawRow[i] = (byte)(x + c);
                                break;
                            default:
                                rawRow[i] = x; 
                                break;
                        }
                    }
                    
                    outStream.Write(rawRow, 0, rowBytes);
                    Array.Copy(rawRow, prevRow, rowBytes);
                }
                return outStream.ToArray();
            }
        }

        private byte[] FlateDecode(byte[] data)
        {
            // Simple Deflate wrapper
            // Skip ZLIB header (2 bytes: 78 9C usually) if present.
            // PDF FlateDecode expects Zlib stream (RFC 1950). 
            // .NET DeflateStream expects raw deflate stream (RFC 1951).
            
            if (data.Length < 2) return data;
            
            // Check ZLIB header (RFC 1950)
            // CMF = data[0]. Lower 4 bits = Compression Method (8 = Deflate). Upper 4 = Window. 
            // CMF = 0x78 => 0111 1000 => Window=32k, Method=8.
            // FLG = data[1]. 
            // Checksum check: (CMF * 256 + FLG) % 31 == 0.
            
            bool hasHeader = false;
            if ((data[0] & 0x0F) == 8) // Method 8
            {
                int cmf = data[0];
                int flg = data[1];
                if ((cmf * 256 + flg) % 31 == 0)
                {
                    hasHeader = true;
                }
            }
            
            try 
            {
                int start = hasHeader ? 2 : 0;
                // If it's pure raw deflate, it might not have header.
                // If header detection is false positive? Unlikely given checksum.
                
                using (var ms = new MemoryStream(data, start, data.Length - start))
                using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch(Exception ex)
            {
                // If hasHeader was true/false and failed, try the other way as last resort?
                // Or just throw to inform user.
                // Let's try raw if header was assumed but failed (unlikely but safe fallback)
                // If header NOT assumed but failed, maybe it HAD header but checksum check failed?
                
                if (hasHeader)
                {
                     // Retry as raw
                     try
                     {
                         using (var ms = new MemoryStream(data))
                         using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                         using (var outMs = new MemoryStream())
                         {
                             ds.CopyTo(outMs);
                             return outMs.ToArray();
                         }
                     }
                     catch { throw new InvalidDataException("FlateDecode failed (ZLIB Header detected)", ex); }
                }
                
                throw new InvalidDataException("FlateDecode failed", ex);
            }
        }

        public List<PdfObject> GetPages()
        {
             if (_trailer == null) 
             {
                 Warnings.Add("Trailer not found.");
                 return new List<PdfObject>();
             }

             var root = Resolve(_trailer.Get("Root")) as PdfDictionary;
             if (root == null) 
             {
                 var keys = string.Join(", ", _trailer.Entries.Keys);
                 var rootVal = _trailer.Get("Root");
                 Warnings.Add($"Root object not found in Trailer. Keys: [{keys}]. Root Val: {rootVal}. XRef Count: {_xrefTable.Count}");
                 return new List<PdfObject>();
             }
             
             var pagesObj = Resolve(root.Get("Pages")) as PdfDictionary;
             if (pagesObj == null) 
             {
                 Warnings.Add("Pages object not found in Root.");
                 return new List<PdfObject>();
             }
             
             var list = new List<PdfObject>();
             CollectPages(pagesObj, list);
             return list;
        }

        private void CollectPages(PdfDictionary pagesDict, List<PdfObject> acc)
        {
             var type = Resolve(pagesDict.Get("Type")) as PdfName;
             if (type == null) 
             {
                 Warnings.Add("Page node missing Type.");
                 return;
             }
             
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
                         else Warnings.Add("Failed to resolve Page Kid.");
                     }
                 }
                 else
                 {
                     Warnings.Add("Pages node missing Kids.");
                     Warnings.Add($"Unknown Page Node Type: {type.Name}");
                 }
             }
        }
    }
}
