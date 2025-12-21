using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization;

namespace laser_gui_test.Data.Pdf
{
    public class PdfTokenizer
    {
        private readonly byte[] _data;
        private int _pos;
        private readonly int _len;

        public PdfTokenizer(byte[] data)
        {
            _data = data;
            _len = data.Length;
            _pos = 0;
        }

        public int Position => _pos;
        public void Seek(int pos) { _pos = pos; }

        public bool IsEOF => _pos >= _len;

        public PdfObject? ReadNextObject()
        {
            SkipWhitespaceAndComments();
            if (IsEOF) return null;

            byte b = _data[_pos];
            char c = (char)b;

            switch (c)
            {
                case '(':
                    return ReadString();
                case '<':
                    // Could be HexString <...> or Dictionary <<...>>
                    if (_pos + 1 < _len && _data[_pos + 1] == '<')
                    {
                        _pos += 2;
                        return ReadDictionary();
                    }
                    else
                    {
                        return ReadHexString();
                    }
                case '[':
                    return ReadArray();
                case '/':
                    return ReadName();
                default:
                    if (IsDigit(c) || c == '-' || c == '+' || c == '.')
                    {
                        var obj1 = ReadNumber();
                        
                        // Check for Reference (Integer Integer R)
                        // Only if numObj is positive integer
                        if (obj1 is PdfNumber numObj && numObj.IsInteger && numObj.IntValue >= 0)
                        {
                            int savePos = _pos;
                            SkipWhitespaceAndComments();
                            if (_pos < _len)
                            {
                                // Check for second integer
                                char next1 = (char)_data[_pos];
                                if (IsDigit(next1))
                                {
                                    var obj2 = ReadNumber();
                                    if (obj2 is PdfNumber num2 && num2.IsInteger && num2.IntValue >= 0)
                                    {
                                        SkipWhitespaceAndComments();
                                        // Check for 'R'
                                        if (_pos < _len && _data[_pos] == 'R' && (_pos+1 >= _len || IsDelimiter((char)_data[_pos+1]) || IsWhitespace(_data[_pos+1])))
                                        {
                                            _pos++; // Consume R
                                            return new PdfReference((int)numObj.IntValue, (int)num2.IntValue);
                                        }
                                    }
                                }
                            }
                            // Restore if match failed
                            _pos = savePos;
                        }
                        
                        return obj1;
                    }
                    else
                    {
                         // Keyword (true, false, null, R, startxref, xref, trailer, etc.)
                        string keyword = ReadKeyword();
                        if (keyword == "true") return new PdfBoolean(true);
                        if (keyword == "false") return new PdfBoolean(false);
                        if (keyword == "null") return PdfNull.Value;
                        // For 'R' (Reference) or 'obj' or 'endobj' etc., we might return KeyWord as a Name or distinct type?
                        // But strictly PdfObject hierarchy doesn't natively support "keywords" as objects unless wrapped.
                        // Let's treat them as special Names or Strings for the parser to handle.
                        // Actually, 'R' usually follows two numbers. The Parser handles this context.
                        // Here we just return what we find.
                        // For simplicity, return as PdfName but prefixed? or just check context in Parser.
                        // The Tokenizer should returns Objects. Keywords are problematic if we don't have a PdfKeyword type.
                        // But 'R' is not a standalone object.
                        // Let's fallback: Tokenizer returns PdfName for keywords?? No that's ambiguous.
                        // Let's add a PdfKeyword type or handle it.
                        // Wait, 'ReadNextObject' implies it consumes a full object.
                        // '1 0 R' are 3 tokens. '1', '0', 'R'.
                        // So ReadNextObject returning '1' is fine.
                        // Then 'R'.
                        // We will return a special PdfName or a wrapper for keyword.
                        // Let's use PdfName with a special flag? Or just string?
                        // Let's return a special PdfName with "KEYWORD_" prefix? No.
                        // Let's assume the CALLER handles logic. We just return a "Token".
                        // But our return type is PdfObject.
                        // Let's use a custom type `PdfOperator` or similar?
                        // Let's add PdfKeyword to PdfObject.cs?
                        // Actually, let's just return it as a PdfName-like object but distinct.
                        // For now, I'll return a PdfName with a property "IsKeyword" if I modify PdfName.
                        // Or simpler: Just return a PdfName. The parser checks "if (obj is PdfName n && n.Name == "R")".
                        // Warning: A real name /R is different from keyword R.
                        // So we MUST distinguish.
                        // Let's add PdfKeyword class to PdfObject.cs, or here locally?
                        // Modify PdfObject.cs first.
                        return new PdfKeyword(keyword);
                    }
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _len)
            {
                byte b = _data[_pos];
                if (IsWhitespace(b))
                {
                    _pos++;
                    continue;
                }
                if (b == '%')
                {
                    // Comment until EOL
                    while (_pos < _len && !IsEOL(_data[_pos]))
                    {
                        _pos++;
                    }
                    continue;
                }
                break;
            }
        }
        
        private bool IsWhitespace(byte b)
        {
            return b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32;
        }

        private bool IsEOL(byte b)
        {
             return b == 10 || b == 13;
        }

        private bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
        
        private bool IsDelimiter(char c)
        {
            return c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%';
        }

        private PdfObject ReadName()
        {
            _pos++; // Skip '/'
            int start = _pos;
            while (_pos < _len)
            {
                byte b = _data[_pos];
                char c = (char)b;
                if (IsWhitespace(b) || IsDelimiter(c)) break;
                // Handle #xx ? Not critical for basic implementation but good to have
                // Basic: characters
                _pos++;
            }
            string name = Encoding.UTF8.GetString(_data, start, _pos - start);
            return new PdfName(name);
        }

        private PdfObject ReadString()
        {
            _pos++; // Skip '('
            int start = _pos;
            int depth = 1;
            List<byte> buffer = new List<byte>();
            
            // Need to handle escaped chars: \n \r \t \b \f \( \) \\ \ddd
            while (_pos < _len)
            {
                byte b = _data[_pos++];
                if (b == '\\') 
                {
                    if (_pos >= _len) break;
                    byte next = _data[_pos++];
                    if (next == 'n') buffer.Add((byte)'\n');
                    else if (next == 'r') buffer.Add((byte)'\r');
                    else if (next == 't') buffer.Add((byte)'\t');
                    else if (next == 'b') buffer.Add((byte)'\b');
                    else if (next == 'f') buffer.Add((byte)'\f');
                    else if (next == '(') buffer.Add((byte)'(');
                    else if (next == ')') buffer.Add((byte)')');
                    else if (next == '\\') buffer.Add((byte)'\\');
                    else if (IsDigit((char)next))
                    {
                         // Octal \ddd
                         int val = next - '0';
                         // Check up to 2 more digits
                         for (int k = 0; k < 2; k++)
                         {
                             if (_pos < _len)
                             {
                                 byte nextByte = _data[_pos];
                                 if (nextByte >= '0' && nextByte <= '7')
                                 {
                                     val = (val << 3) + (nextByte - '0');
                                     _pos++;
                                 }
                                 else break;
                             }
                         }
                         buffer.Add((byte)(val & 0xFF));
                    }
                    else
                    {
                        // Ignore backslash (unless it was essentially escaped self? no, self escaped is \\)
                        // Spec: If char is not one of above, backslash is ignored.
                        buffer.Add(next);
                    }
                }
                else if (b == '(')
                {
                    depth++;
                    buffer.Add(b);
                }
                else if (b == ')')
                {
                    depth--;
                    if (depth == 0) break;
                    buffer.Add(b);
                }
                else
                {
                    buffer.Add(b);
                }
            }
            return new PdfString(buffer.ToArray(), true);
        }

        private PdfObject ReadHexString()
        {
            _pos++; // Skip '<'
            List<byte> bytes = new List<byte>();
            // Read until '>'
            // Ignore whitespace
            while (_pos < _len)
            {
                byte b = _data[_pos++];
                if (b == '>') break;
                if (IsWhitespace(b)) continue;
                
                // Read next char
                byte b2 = 0; // Default if missing is 0 (spec)
                
                // We need high nybble
                int high = GetHexVal((char)b);
                
                // Search for low nybble
                while (_pos < _len)
                {
                     byte n = _data[_pos];
                     if (n == '>') 
                     {
                         // Odd number of hex digits, last is 0
                         break;
                     }
                     if (!IsWhitespace(n))
                     {
                         b2 = n;
                         _pos++;
                         break;
                     }
                     _pos++;
                }

                int low = GetHexVal((char)b2);
                bytes.Add((byte)((high << 4) | low));
            }
            return new PdfString(bytes.ToArray(), false);
        }

        private int GetHexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return 0;
        }

        private PdfObject ReadNumber()
        {
            int start = _pos;
            while (_pos < _len)
            {
                byte b = _data[_pos];
                char c = (char)b;
                if (IsDigit(c) || c == '.' || c == '-' || c == '+') { _pos++; }
                else break;
            }
            string numStr = Encoding.ASCII.GetString(_data, start, _pos - start);
            
            if (numStr.Contains("."))
            {
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                    return new PdfNumber(d);
            }
            else
            {
                if (long.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out long i))
                    return new PdfNumber(i);
            }
            return new PdfNumber(0); // Fallback
        }

        private string ReadKeyword()
        {
            int start = _pos;
            while (_pos < _len)
            {
                byte b = _data[_pos];
                char c = (char)b;
                if (IsDelimiter(c) || IsWhitespace(b)) break;
                _pos++;
            }
            return Encoding.ASCII.GetString(_data, start, _pos - start);
        }

        private PdfObject ReadArray()
        {
            _pos++; // Skip '['
            var arr = new PdfArray();
            while (true)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _len) break;
                if (_data[_pos] == ']') 
                {
                    _pos++;
                    break;
                }
                var obj = ReadNextObject();
                if (obj != null) arr.Add(obj);
            }
            return arr;
        }

        private PdfObject ReadDictionary()
        {
            // Already consumed '<<'
            var dict = new PdfDictionary();
            while (true)
            {
                SkipWhitespaceAndComments();
                if (_pos >= _len) break;
                if (_data[_pos] == '>' && _pos + 1 < _len && _data[_pos+1] == '>')
                {
                    _pos += 2;
                    break;
                }
                
                var keyObj = ReadNextObject();
                if(keyObj is PdfName name)
                {
                    var valObj = ReadNextObject();
                    if (valObj != null)
                        dict.Add(name.Name, valObj);
                }
                else if (keyObj is PdfKeyword k && k.Keyword == ">>")
                {
                    // Should be caught by '>>' check simpler, but just in case
                     break; 
                }
                else
                {
                    // Error or unexpected end
                    break; 
                }
            }
            return dict;
        }

        // Helper to read raw bytes for Stream
        // Helper to read raw bytes for Stream
        public byte[]? ReadStreamBytes(int length)
        {
            // Usually stream follows 'stream' keyword AND EOL.
            // The keyword stream that marks the beginning of the stream content shall be followed by either an end-of-line marker or a single space and an end-of-line marker.
            
             if (_pos < _len && _data[_pos] == 32) _pos++; // Skip optional space
             if (_pos < _len && _data[_pos] == '\r') _pos++;
             if (_pos < _len && _data[_pos] == '\n') _pos++;
            
            if (_pos + length > _len) 
            {
                 // Truncated?
                 int available = _len - _pos;
                 if (available <= 0) 
                 {
                     Console.WriteLine($"ReadStreamBytes Failed: Zero bytes available at {_pos}.");
                     return null; 
                 }
                 
                 Console.WriteLine($"ReadStreamBytes Warning: Requested {length}, available {available}. Truncating read.");
                 // Read what we can
                 byte[] buf = new byte[available];
                 Array.Copy(_data, _pos, buf, 0, available);
                 _pos += available;
                 return buf;
            }
            
            byte[] buffer = new byte[length];
            Array.Copy(_data, _pos, buffer, 0, length);
            _pos += length;
            return buffer;
            
            // Optional: consume 'endstream'
            // We don't strictly need to checks "endstream" but it helps validation
            // But saving _pos references might be safer if we just return content.
            

        }
    }

    // Helper for Keyword
    public class PdfKeyword : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Name; // Masquerade? No, unique type internally
        public string Keyword { get; }
        public PdfKeyword(string keyword) { Keyword = keyword; }
        public override string ToString() => Keyword;
    }
}
