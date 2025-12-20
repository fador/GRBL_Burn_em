using System;
using System.Collections.Generic;
using System.Text;

namespace laser_gui_test.Data.Pdf
{
    public enum PdfObjectType
    {
        Null,
        Boolean,
        Integer,
        Real,
        String,
        Name,
        Array,
        Dictionary,
        Stream,
        Reference
    }

    public abstract class PdfObject
    {
        public abstract PdfObjectType Type { get; }

        public virtual int MainType => (int)Type;
        
        public override string ToString() => Type.ToString();
    }

    public class PdfNull : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Null;
        public static readonly PdfNull Value = new PdfNull();
    }

    public class PdfBoolean : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Boolean;
        public bool Value { get; }

        public PdfBoolean(bool value)
        {
            Value = value;
        }

        public override string ToString() => Value ? "true" : "false";
    }

    public class PdfNumber : PdfObject
    {
        public override PdfObjectType Type => IsInteger ? PdfObjectType.Integer : PdfObjectType.Real;
        
        public bool IsInteger { get; }
        public long IntValue { get; }
        public double RealValue { get; }

        public PdfNumber(long value)
        {
            IsInteger = true;
            IntValue = value;
            RealValue = value;
        }

        public PdfNumber(double value)
        {
            IsInteger = false;
            RealValue = value;
            IntValue = (long)value;
        }
        
        public override string ToString() => IsInteger ? IntValue.ToString() : RealValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    public class PdfName : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Name;
        public string Name { get; }

        public PdfName(string name)
        {
            if (name.StartsWith("/")) Name = name.Substring(1);
            else Name = name;
        }

        public override string ToString() => "/" + Name;
        
        public override bool Equals(object? obj)
        {
            if (obj is PdfName other) return Name == other.Name;
            return false;
        }
        
        public override int GetHashCode() => Name.GetHashCode();
    }

    public class PdfString : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.String;
        public string Value { get; } // Raw string content
        public byte[] Bytes { get; }
        public bool IsGeneric { get; } // true if (...) format, false if <...> hex format

        public PdfString(string value)
        {
            Value = value;
            IsGeneric = true;
            // Simplistic Encoding for now
            Bytes = Encoding.Latin1.GetBytes(value); 
        }

        public PdfString(byte[] bytes, bool isGeneric = true)
        {
            Bytes = bytes;
            IsGeneric = isGeneric;
            // Try to decode as generic chars
            Value = Encoding.Latin1.GetString(bytes);
        }

        public override string ToString() => $"({Value})";
    }

    public class PdfArray : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Array;
        public List<PdfObject> Items { get; } = new List<PdfObject>();

        public void Add(PdfObject obj) => Items.Add(obj);
        
        public override string ToString() => $"[{Items.Count} items]";
    }

    public class PdfDictionary : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Dictionary;
        public Dictionary<string, PdfObject> Entries { get; } = new Dictionary<string, PdfObject>();

        public void Add(string key, PdfObject value) => Entries[key] = value;
        public bool ContainsKey(string key) => Entries.ContainsKey(key);
        public PdfObject? Get(string key) => Entries.TryGetValue(key, out var val) ? val : null;
        
        public override string ToString() => $"<<{Entries.Count} entries>>";
    }

    public class PdfReference : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Reference;
        public int ObjectNumber { get; }
        public int GenerationNumber { get; }

        public PdfReference(int objNum, int genNum)
        {
            ObjectNumber = objNum;
            GenerationNumber = genNum;
        }

        public override string ToString() => $"{ObjectNumber} {GenerationNumber} R";
    }

    public class PdfStream : PdfObject
    {
        public override PdfObjectType Type => PdfObjectType.Stream;
        public PdfDictionary Dictionary { get; }
        public byte[] Data { get; set; }

        public PdfStream(PdfDictionary dict, byte[] data)
        {
            Dictionary = dict;
            Data = data;
        }
    }
}
