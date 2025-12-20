using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;

namespace laser_gui_test.Data.Pdf
{
    public class PdfContentParser
    {
        private readonly PdfReader _reader;
        private readonly PdfDictionary _resources;
        private Matrix _ctm;
        public List<string> Warnings { get; } = new List<string>();
        
        // Graphics State
        private class GraphicsState
        {
            public Matrix CTM { get; set; } = new Matrix();
            public Color StrokeColor { get; set; } = Color.Black;
            public Color FillColor { get; set; } = Color.Black;
            public float LineWidth { get; set; } = 1.0f;
            
            // Text State
            public float TextLeading { get; set; } = 0;
            public float TextRise { get; set; } = 0;
            public float TextCharSpacing { get; set; } = 0;
            public float TextWordSpacing { get; set; } = 0;
            public float TextHScale { get; set; } = 100;
            public PdfName? FontName { get; set; }
            public float FontSize { get; set; } = 12;
            
            public GraphicsState Clone()
            {
                var clone = new GraphicsState()
                {
                    CTM = CTM.Clone(),
                    StrokeColor = StrokeColor,
                    FillColor = FillColor,
                    LineWidth = LineWidth,
                    TextLeading = TextLeading,
                    TextRise = TextRise,
                    TextCharSpacing = TextCharSpacing,
                    TextWordSpacing = TextWordSpacing,
                    TextHScale = TextHScale,
                    FontName = FontName,
                    FontSize = FontSize
                };
                return clone;
            }
        }
        
        private Stack<GraphicsState> _graphicsStack = new Stack<GraphicsState>();
        private GraphicsState _state;
        
        // Path Construction
        private PointF _currentPoint;
        private GraphicsPath _currentPath;
        
        // Text Object State (Transient between BT...ET)
        private Matrix _textMatrix;
        private Matrix _textLineMatrix;
        
        public PdfContentParser(PdfReader reader, PdfDictionary resources)
        {
            _reader = reader;
            _resources = resources;
            _state = new GraphicsState();
            _ctm = new Matrix(); // Identity
            _currentPath = new GraphicsPath();
            _textMatrix = new Matrix();
            _textLineMatrix = new Matrix();
        }

        public List<LaserObject> Parse(byte[] contentBytes)
        {
            var tokenizer = new PdfTokenizer(contentBytes);
            var objects = new List<LaserObject>();
            var operands = new List<PdfObject>();

            while (!tokenizer.IsEOF)
            {
                var obj = tokenizer.ReadNextObject();
                if (obj == null) break;

                if (obj is PdfKeyword kw)
                {
                    ProcessOperator(kw.Keyword, operands, objects);
                    operands.Clear();
                }
                else
                {
                    operands.Add(obj);
                }
            }
            return objects;
        }

        private void ProcessOperator(string op, List<PdfObject> operands, List<LaserObject> objects)
        {
            switch (op)
            {
                // --- Graphics State ---
                case "q": // Push
                    _graphicsStack.Push(_state.Clone());
                    break;
                case "Q": // Pop
                    if (_graphicsStack.Count > 0) _state = _graphicsStack.Pop();
                    break;
                case "cm": // Concat CTM
                    if (operands.Count == 6)
                    {
                        float a = (float)GetNum(operands[0]);
                        float b = (float)GetNum(operands[1]);
                        float c = (float)GetNum(operands[2]);
                        float d = (float)GetNum(operands[3]);
                        float e = (float)GetNum(operands[4]);
                        float f = (float)GetNum(operands[5]);
                        using (var m = new Matrix(a, b, c, d, e, f))
                        {
                            _state.CTM.Multiply(m, MatrixOrder.Prepend); // PDF appends, .NET prepends? Check docs.
                            // PDF: new_CTM = matrix x old_CTM. (Prepend)
                            // .NET Multiply order: MatrixOrder.Prepend means new * old. Correct.
                        }
                    }
                    break;
                    
                // --- Path Construction ---
                case "m": // MoveTo
                    if (operands.Count == 2)
                    {
                        float x = (float)GetNum(operands[0]);
                        float y = (float)GetNum(operands[1]);
                        _currentPoint = new PointF(x, y);
                        _currentPath.StartFigure();
                    }
                    break;
                case "l": // LineTo
                     if (operands.Count == 2)
                    {
                        float x = (float)GetNum(operands[0]);
                        float y = (float)GetNum(operands[1]);
                        _currentPath.AddLine(_currentPoint, new PointF(x, y));
                        _currentPoint = new PointF(x, y);
                    }
                    break;
                case "c": // CurveTo
                     if (operands.Count == 6)
                     {
                         float x1 = (float)GetNum(operands[0]);
                         float y1 = (float)GetNum(operands[1]);
                         float x2 = (float)GetNum(operands[2]);
                         float y2 = (float)GetNum(operands[3]);
                         float x3 = (float)GetNum(operands[4]);
                         float y3 = (float)GetNum(operands[5]);
                         _currentPath.AddBezier(_currentPoint, new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3));
                         _currentPoint = new PointF(x3, y3);
                     }
                    break;
                case "v": // CurveTo (current point is first control)
                     if (operands.Count == 4)
                     {
                         float x2 = (float)GetNum(operands[0]);
                         float y2 = (float)GetNum(operands[1]);
                         float x3 = (float)GetNum(operands[2]);
                         float y3 = (float)GetNum(operands[3]);
                         _currentPath.AddBezier(_currentPoint, _currentPoint, new PointF(x2, y2), new PointF(x3, y3));
                         _currentPoint = new PointF(x3, y3);
                     }
                    break;
                case "y": // CurveTo (final point is second control)
                     if (operands.Count == 4)
                     {
                         float x1 = (float)GetNum(operands[0]);
                         float y1 = (float)GetNum(operands[1]);
                         float x3 = (float)GetNum(operands[2]);
                         float y3 = (float)GetNum(operands[3]);
                         _currentPath.AddBezier(_currentPoint, new PointF(x1, y1), new PointF(x3, y3), new PointF(x3, y3));
                         _currentPoint = new PointF(x3, y3);
                     }
                    break;
                case "re": // Rectangle
                     if (operands.Count == 4)
                     {
                         float x = (float)GetNum(operands[0]);
                         float y = (float)GetNum(operands[1]);
                         float w = (float)GetNum(operands[2]);
                         float h = (float)GetNum(operands[3]);
                         _currentPath.StartFigure();
                         _currentPath.AddRectangle(new RectangleF(x, y, w, h));
                         _currentPoint = new PointF(x, y); // Spec says "updates current point to x,y"? No, usually closed.
                     }
                    break;
                case "h": // ClosePath
                    _currentPath.CloseFigure();
                    break;

                // --- Path Painting ---
                case "S": // Stroke
                case "s": // Close and Stroke
                    if (op == "s") _currentPath.CloseFigure();
                    AddPathObject(objects, false);
                    _currentPath.Reset();
                    break;
                case "f": // Fill
                case "F": 
                case "f*": // EvenOdd
                case "B": // Fill and Stroke
                case "B*":
                case "b": // Close, Fill, Stroke
                case "b*":
                    if (op == "b" || op == "b*") _currentPath.CloseFigure();
                    AddPathObject(objects, true); // Treat all fills as filled paths
                    _currentPath.Reset();
                    break;
                case "n": // End path no op
                    _currentPath.Reset();
                    break;

                // --- Text Objects ---
                case "BT": // Begin Text
                    _textMatrix = new Matrix();
                    _textLineMatrix = new Matrix();
                    break;
                case "ET": // End Text
                    break;
                case "Tf": // Set Font
                    if (operands.Count == 2)
                    {
                        var fontName = operands[0] as PdfName;
                        float size = (float)GetNum(operands[1]);
                        _state.FontName = fontName;
                        _state.FontSize = size;
                    }
                    break;
                case "Td": // Move Text
                    if (operands.Count == 2)
                    {
                        float tx = (float)GetNum(operands[0]);
                        float ty = (float)GetNum(operands[1]);
                        _textLineMatrix.Translate(tx, ty, MatrixOrder.Prepend);
                        _textMatrix = _textLineMatrix.Clone();
                    }
                    break;
                case "Tm": // Set Text Matrix
                    if (operands.Count == 6)
                    {
                        float a = (float)GetNum(operands[0]);
                        float b = (float)GetNum(operands[1]);
                        float c = (float)GetNum(operands[2]);
                        float d = (float)GetNum(operands[3]);
                        float e = (float)GetNum(operands[4]);
                        float f = (float)GetNum(operands[5]);
                        _textLineMatrix = new Matrix(a, b, c, d, e, f);
                        _textMatrix = _textLineMatrix.Clone();
                    }
                    break;
                case "Tj": // Show Text
                    if (operands.Count == 1)
                    {
                        var str = operands[0] as PdfString;
                        if (str != null)
                        {
                            AddTextObject(str.Value, objects);
                        }
                    }
                    break;
                case "TJ": // Show Text with spacing
                    if (operands.Count == 1 && operands[0] is PdfArray arr)
                    {
                        // Simplified: concatenate string parts
                        StringBuilder sb = new StringBuilder();
                        foreach(var item in arr.Items)
                        {
                            if (item is PdfString s) sb.Append(s.Value);
                            // Numbers are spacing adjustments, ignoring for MVP
                        }
                        AddTextObject(sb.ToString(), objects);
                    }
                    break;
                    
                // --- Images / XObjects ---
                case "Do":
                    if (operands.Count == 1 && operands[0] is PdfName name)
                    {
                        ProcessXObject(name, objects);
                    }
                    break;

                default:
                    // Only warn for significant operators? 
                    // Many are state operators (gs, cs, CS, w, J, j, M, d, ri, i...) 
                    // Warnings for everything might be noisy but safer for "No output" debugging.
                    // Let's filter common state operators we ignore.
                    if (IsIgnoredStateOperator(op)) { }
                    else if (op == "g" || op == "G" || op == "rg" || op == "RG" || op == "k" || op == "K") 
                    {
                        // Color operators, maybe warn once? "Color not supported"
                        // Or just ignore silently for MVP?
                        // User sees "No supported objects", so seeing "Color operator ignored" is helpful context.
                         Warnings.Add($"Ignored Color/State operator: {op}");
                    }
                    else
                    {
                         Warnings.Add($"Unhandled operator: {op}");
                    }
                    break;
            }
        }

        private void AddPathObject(List<LaserObject> objects, bool filled)
        {
            if (_currentPath.PointCount == 0) return;
            
            // Create LaserPath
            // Flatten path
            // Transform path by CTM
            using var transformedPath = (GraphicsPath)_currentPath.Clone();
            transformedPath.Transform(_state.CTM);
            transformedPath.Flatten();
            
            if (transformedPath.PointCount < 2) return;
            
            var lp = new LaserPath();
            lp.Points = transformedPath.PathPoints.ToList(); // Converts PointF[] to List<PointF>
            lp.UpdateBounds();
            // TODO: Color, Power, Speed mappings from GraphicsState?
            // For now default.
            
            objects.Add(lp);
        }

        private void AddTextObject(string text, List<LaserObject> objects)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            // Calculate Position: (0,0) in Text Space -> Transformed by Tm -> Transformed by CTM
            PointF[] pts = new PointF[] { new PointF(0, 0) };
            _textMatrix.TransformPoints(pts);
            _state.CTM.TransformPoints(pts);
            
            // Extract Font Info from Resources if possible
            string fontName = "Arial";
            if (_state.FontName != null)
            {
                // Resolve font resource... complex.
                // For MVP, use the name as is (minus /)
               fontName = _state.FontName.Name; 
            }
            
            // Font Size scaling
            // Text Matrix scale * CTM scale * FontSize
            // Rough approximation of height
            float fontSize = _state.FontSize; 
            // We should apply scaling e.g. from CTM Y-scale?
            // Let's assume FontSize is in abstract units, CTM converts to Device (MM?)
            
            var lt = new LaserText();
            lt.Text = text;
            lt.Position = pts[0];
            lt.FontSize = fontSize; // Needs checking scale
            lt.FontName = fontName;
            
            // Should apply transforms?
            // LaserText has Position, Rotation.
            // Extract Rotation from CTM * Tm?
            
            objects.Add(lt);
        }

        private void ProcessXObject(PdfName name, List<LaserObject> objects)
        {
            if (_resources == null) return;
            var xobjDict = _reader.Resolve(_resources.Get("XObject")) as PdfDictionary;
            if (xobjDict == null) return;
            
            var objRef = _reader.Resolve(xobjDict.Get(name.Name)); // Use string key
            // Start reading the stream... 
            // Wait, we need the *Resolved* object which is a Stream
            // _reader.Resolve returns PdfStream if fully resolved?
            // My PdfReader.Resolve handles Ref -> Object.
            // If it's a stream, it returns PdfStream.
            
            if (objRef is PdfStream stream)
            {
                 var subtype = _reader.Resolve(stream.Dictionary.Get("Subtype")) as PdfName;
                 if (subtype != null && subtype.Name == "Image")
                 {
                     // It is an image
                     // Get Width, Height from dictionary
                     // ColorSpace?
                     // BitsPerComponent?
                     
                     // Decode bytes?
                     // We already decoded filter in GetObject if implemented.
                     
                     // Create Bitmap?
                     // Simple formats: DeviceRGB, DeviceGray.
                     // 8 bpc.
                     // If complex, skip.
                     
                     try 
                     {
                         // Basic RGB 8bit or Gray 8bit support
                         long w = ((PdfNumber)_reader.Resolve(stream.Dictionary.Get("Width"))!).IntValue;
                         long h = ((PdfNumber)_reader.Resolve(stream.Dictionary.Get("Height"))!).IntValue;
                         
                         // Create LaserImage
                         var lImg = new LaserImage();
                         
                         // Map 1x1 rect at (0,0) to CTM
                         // Image XObject draws in unit square 0..1, 0..1
                         PointF[] pts = new PointF[] { new PointF(0, 0), new PointF(1, 1) };
                         _state.CTM.TransformPoints(pts);
                         
                         float x = Math.Min(pts[0].X, pts[1].X);
                         float y = Math.Min(pts[0].Y, pts[1].Y);
                         float width = Math.Abs(pts[1].X - pts[0].X);
                         float height = Math.Abs(pts[1].Y - pts[0].Y);
                         
                         lImg.Position = new PointF(x, y);
                         lImg.Size = new SizeF(width, height);
                         
                         // Populate Image...
                         // Need conversion from raw bytes to Bitmap
                         // Skipping actual bitmap creation for MVP if complex, or putting placeholder?
                         // "Without external libraries" -> Use Sys.Draw.Bitmap
                         // Can create Bitmap from pixel data.
                         
                         // Simplified:
                         lImg.Name = name.Name;
                         // Add logic to CreateBitmap(stream.Data, w, h, space)
                         
                         objects.Add(lImg);
                     }
                     catch {}
                 }
                 else if (subtype != null && subtype.Name == "Form")
                 {
                      // Recursion! Form XObject has its own Stream content.
                      // Push State (q)
                      // _graphicsStack.Push(_state.Clone());
                      
                      // Update Matrix (Form Matrix * CTM)
                      // var matrixObj = _reader.Resolve(stream.Dictionary.Get("Matrix")) as PdfArray;
                      // if (matrixObj != null && matrixObj.Items.Count == 6) { ... }
                      
                      // Extract Resources (Form Resources)
                      // var res = _reader.Resolve(stream.Dictionary.Get("Resources")) as PdfDictionary ?? _resources;
                      
                      // var subParser = new PdfContentParser(_reader, res);
                      // var subObjects = subParser.Parse(stream.Data);
                      
                      // Add them to our list (transformed)
                      
                      // For MVP: Skip Form XObjects
                      Warnings.Add($"Skipped Form XObject '{name.Name}' (recursion/forms not fully supported yet).");
                 }
                 else
                 {
                     Warnings.Add($"Skipped XObject '{name.Name}' of unknown subtype '{(subtype?.Name ?? "null")}'.");
                 }
            }
        }

        private bool IsIgnoredStateOperator(string op)
        {
            // Common state operators that don't affect shape geometry directly for MVP
            // w = line width (we track it but ignoring it here is fine as we don't warn)
            // J, j = line cap/join
            // M = miter limit
            // d = dash pattern
            // ri = rendering intent
            // i = flatness
            // gs = graphics state dictionary (ext state)
            // cs, CS = color space
            return op == "w" || op == "J" || op == "j" || op == "M" || op == "d" || op == "ri" || op == "i" || op == "gs" || op == "cs" || op == "CS"
                   || op == "BDC" || op == "EMC" || op == "BMC" || op == "DP" || op == "MP";
        }
        
        private double GetNum(PdfObject obj)
        {
            if (obj is PdfNumber n) return n.RealValue;
            return 0;
        }
    }
}
