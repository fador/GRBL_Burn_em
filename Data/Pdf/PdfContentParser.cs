using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;


namespace laser_gui_test.Data.Pdf
{
    public class PdfContentParser
    {
        private readonly PdfReader _reader;
        private readonly PdfDictionary _resources;
        private Matrix _ctm;
        private Dictionary<string, PdfFontInfo> _fontCache = new Dictionary<string, PdfFontInfo>();
        
        private class PdfFontInfo
        {
             public string BaseFont = ""; // Initialize to empty string to satisfy non-nullable
             public bool IsWinAnsi;
             public int[]? Widths; // Make nullable
             public int FirstChar;
             public int LastChar;
             public int MissingWidth = 600; 
        }
        public List<string> Warnings { get; } = new List<string>();
        
        // OCG State
        private bool _ocPropertiesLoaded = false;
        private HashSet<PdfReference> _hiddenOCGs = new HashSet<PdfReference>();
        // We might also need a set of "Visible OCGs" if we want to handle BaseState=OFF?
        // Spec: BaseState (default ON).
        // D -> ON [list], OFF [list].
        // Visibility = (BaseState == ON) ? (!OFF.Contains(ocg)) : (ON.Contains(ocg))
        private bool _ocBaseStateOn = true;
        private HashSet<PdfReference> _ocOnList = new HashSet<PdfReference>();
        private HashSet<PdfReference> _ocOffList = new HashSet<PdfReference>();
        
        // Graphics State
        
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
            public int RenderMode { get; set; } = 0; 
            public float FillAlpha { get; set; } = 1.0f;
            public float StrokeAlpha { get; set; } = 1.0f;
            public Region? ClipRegion { get; set; } = null; // Region supports complex shapes

            
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
                    FontSize = FontSize,
                    RenderMode = RenderMode,
                    FillAlpha = FillAlpha,
                    StrokeAlpha = StrokeAlpha,
                    ClipRegion = ClipRegion?.Clone() // Deep clone required for Regions
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
        private bool _isClipping = false; // Flag if next painting op should update clip
        private Stack<bool> _visibilityStack = new Stack<bool>();
        private bool _currentVisibility = true;
        
        public PdfContentParser(PdfReader reader, PdfDictionary resources, RectangleF mediaBox)
        {
            _reader = reader;
            _resources = resources;
            _state = new GraphicsState();
            if (mediaBox.Width > 0 && mediaBox.Height > 0) 
            {
                _state.ClipRegion = new Region(mediaBox);
            }
            
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

            int opCount = 0;
            while (!tokenizer.IsEOF)
            {
                var obj = tokenizer.ReadNextObject();
                if (obj == null) break;

                if (obj is PdfKeyword kw)
                {
                    opCount++;
                    ProcessOperator(kw.Keyword, operands, objects);
                    operands.Clear();
                }
                else
                {
                    operands.Add(obj);
                }
            }
            
            if (objects.Count == 0 && opCount > 0)
            {
                Warnings.Add($"Parsed {opCount} operators but created 0 objects. (Possible invisible content, clipping, or unhandled painting ops).");
            }
            else if (objects.Count == 0 && operands.Count > 0 && opCount == 0)
            {
                 Warnings.Add($"Parsed {operands.Count} tokens (numbers/names) but found 0 operators. Content stream might be malformed or missing keywords.");
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
                    if (_isClipping) { UpdateClipBounds(); }
                    else { AddPathObject(objects, true); } // Treat all fills as filled paths
                    _currentPath.Reset();
                    break;
                case "W": // Set Clipping Path (Non-zero winding)
                    _isClipping = true;
                    _currentPath.FillMode = FillMode.Winding;
                    break;
                case "W*": // Set Clipping Path (Even-Odd)
                    _isClipping = true;
                    _currentPath.FillMode = FillMode.Alternate;
                    break;
                    
                case "n": // End path no op
                    if (_isClipping) { UpdateClipBounds(); }
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
                        // PDF Spec: Tm = [1 0 0 1 tx ty] x Tlm_old
                        // This corresponds to Prepend in GDI+ (Translation * Matrix)
                        _textLineMatrix.Translate(tx, ty, MatrixOrder.Prepend); 
                        _textMatrix = _textLineMatrix.Clone();
                    }
                    break;
                case "TD": // Move Text and Set Leading
                    if (operands.Count == 2)
                    {
                        float tx = (float)GetNum(operands[0]);
                        float ty = (float)GetNum(operands[1]);
                        _state.TextLeading = -ty; // TD sets TL to -ty
                        _textLineMatrix.Translate(tx, ty, MatrixOrder.Prepend);
                        _textMatrix = _textLineMatrix.Clone();
                    }
                    break;
                case "T*": // Move to Next Line
                    // Move by (0, -Tl)
                    _textLineMatrix.Translate(0, -_state.TextLeading, MatrixOrder.Prepend);
                    _textMatrix = _textLineMatrix.Clone();
                    break;
                case "Tc": // Set Character Spacing
                     if (operands.Count == 1) _state.TextCharSpacing = (float)GetNum(operands[0]);
                     break;
                case "Tw": // Set Word Spacing
                     if (operands.Count == 1) _state.TextWordSpacing = (float)GetNum(operands[0]);
                     break;
                case "Ts": // Set Text Rise
                     if (operands.Count == 1) _state.TextRise = (float)GetNum(operands[0]);
                     break;
                 case "\"": // Set spacing, move to next line, show text
                     if (operands.Count == 3)
                     {
                         _state.TextWordSpacing = (float)GetNum(operands[0]);
                         _state.TextCharSpacing = (float)GetNum(operands[1]);
                         // Move by (0, -Tl)
                         _textLineMatrix.Translate(0, -_state.TextLeading, MatrixOrder.Prepend);
                         _textMatrix = _textLineMatrix.Clone();
                         if (operands[2] is PdfString s) AddTextObject(s.Value, objects);
                     }
                     break;
                 case "'": // Move to next line, show text
                     if (operands.Count == 1 && operands[0] is PdfString s2)
                     {
                         _textLineMatrix.Translate(0, -_state.TextLeading, MatrixOrder.Prepend);
                         _textMatrix = _textLineMatrix.Clone();
                         AddTextObject(s2.Value, objects);
                     }
                     break;
                case "Tr": // Text Rendering Mode
                    if (operands.Count == 1)
                    {
                        _state.RenderMode = (int)GetNum(operands[0]);
                    }
                    break;
                case "Tz": // Horizontal Scaling
                     if (operands.Count == 1) _state.TextHScale = (float)GetNum(operands[0]);
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
                        foreach(var item in arr.Items)
                        {
                            if (item is PdfString s) 
                            { 
                                AddTextObject(s.Value, objects);
                            }
                            else if (item is PdfNumber n)
                            {
                                // Adjust text position defined by n
                                // "Subtracting" the amount (Move Left)
                                // Units: thousandths of text space
                                // tx = (-n / 1000) * FontSize * HScale
                                float spacing = (float)n.RealValue;
                                float tx = (-spacing / 1000f) * _state.FontSize * (_state.TextHScale / 100f);
                                _textLineMatrix.Translate(tx, 0, MatrixOrder.Prepend); // Wait, TJ updates Text Matrix (Tm), not Text Line Matrix (Tlm) usually?
                                // Actually TJ updates Tm. Tlm is start of line.
                                // In this parser: _textMatrix is Tm. _textLineMatrix is Tlm.
                                // Td/TD updates Tlm. TJ updates Tm. 
                                // My code uses _textMatrix = _textLineMatrix.Clone() at start of line.
                                // So here we must update _textMatrix.
                                _textMatrix.Translate(tx, 0, MatrixOrder.Prepend);
                            }
                        }
                    }
                    else if (operands.Count == 1 && operands[0] is PdfString strOne)
                    {
                         // Handle case where TJ is called with single string (unlikely but possible error)
                         AddTextObject(strOne.Value, objects);
                    }
                    break;
                
                // --- Color Operators ---
                case "g": // Set non-stroking gray
                    if (operands.Count == 1) SetColor(operands, false, "Gray");
                    break;
                case "G": // Set stroking gray
                    if (operands.Count == 1) SetColor(operands, true, "Gray");
                    break;
                case "rg": // Set non-stroking RGB
                    if (operands.Count == 3) SetColor(operands, false, "RGB");
                    break;
                case "RG": // Set stroking RGB
                    if (operands.Count == 3) SetColor(operands, true, "RGB");
                    break;
                case "k": // Set non-stroking CMYK
                    if (operands.Count == 4) SetColor(operands, false, "CMYK");
                    break;
                case "K": // Set stroking CMYK
                    if (operands.Count == 4) SetColor(operands, true, "CMYK");
                    break;

                case "gs": // Set Graphics State (Transparency etc.)
                    if (operands.Count == 1 && operands[0] is PdfName gsName)
                    {
                        ProcessExtGState(gsName);
                    }
                    break;

                // --- Images / XObjects ---
                case "Do":
                    if (operands.Count == 1 && operands[0] is PdfName name)
                    {
                        ProcessXObject(name, objects);
                    }
                    break;

                // --- Optional Content (Layers) ---
                case "BDC": // Begin Marked Content
                    if (operands.Count >= 2 && operands[1] is PdfDictionary props) 
                    {
                        // Check for /OC
                        ProcessBDC(props); 
                    }
                    else 
                    {
                        // Default to visible if no OC, just push current state
                        _visibilityStack.Push(_currentVisibility);
                    }
                    break;
                case "EMC": // End Marked Content
                    if (_visibilityStack.Count > 0)
                    {
                         _currentVisibility = _visibilityStack.Pop();
                    }
                    break;

                default:
                    // Only warn for significant operators? 
                    // Many are state operators (gs, cs, CS, w, J, j, M, d, ri, i...) 
                    // Warnings for everything might be noisy but safer for "No output" debugging.
                    // Let's filter common state operators we ignore.
                    if (IsIgnoredStateOperator(op)) { }
                    else if (op == "q" || op == "Q" || op == "cm")
                    {
                        // These should be handled! Why are they falling through?
                        // Ah, they are handled in specific cases above, but if they fell through it means I missed them in the main switch?
                        // No, 'q', 'Q', 'cm' are handled in the switch case "q": etc.
                        // So they shouldn't be here.
                        Warnings.Add($"Operator {op} hit default case - Logic Error?");
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
            if (!_currentVisibility) return;
            if (_currentPath.PointCount == 0) return;
            
            // Filter by Color (White Geometry = Invisible/Mask usually)
            // If Filled: Check FillColor
            if (filled && IsWhite(_state.FillColor)) return;
            // If Stroked (not filled): Check StrokeColor
            if (!filled && IsWhite(_state.StrokeColor)) return;
            
            using var transformedPath = (GraphicsPath)_currentPath.Clone();
            transformedPath.Transform(_state.CTM);

            // Check Clipping
            if (_state.ClipRegion != null)
            {
                RectangleF bounds = transformedPath.GetBounds();
                if (!_state.ClipRegion.IsVisible(bounds)) return; // Completely outside region
            }

            // Flatten to convert curves to lines for simple LaserPath processing
            transformedPath.Flatten();
            
            if (transformedPath.PointCount < 2) return;
            
            // Extract subpaths (handling movements and closures)
            var points = transformedPath.PathPoints;
            var types = transformedPath.PathTypes;
            
            List<PointF> currentSubpath = new List<PointF>();
            
            for (int i = 0; i < points.Length; i++)
            {
                byte type = types[i];
                bool isStart = (type & 0x07) == 0; // PathPointType.Start = 0
                bool isClose = (type & 0x80) != 0; // PathPointType.CloseSubpath = 0x80
                
                if (isStart)
                {
                    // If we have a previous subpath, finish it
                    if (currentSubpath.Count > 1)
                    {
                        var lp = new LaserPath();
                        lp.Points = new List<PointF>(currentSubpath);
                        lp.UpdateBounds();
                        objects.Add(lp);
                    }
                    currentSubpath.Clear();
                }
                
                currentSubpath.Add(points[i]);
                
                if (isClose)
                {
                    // Close the subpath: Add line to start if strictly needed
                    // GraphicsPath usually implies the line, but LaserPath needs points.
                    if (currentSubpath.Count > 0)
                    {
                         var start = currentSubpath[0];
                         var end = currentSubpath[currentSubpath.Count - 1];
                         if (start != end) // Float comparison?
                         {
                             // Explicitly close
                             currentSubpath.Add(start);
                         }
                    }
                    
                    // Finish this closed subpath immediately? 
                    // Usually Close ends the subpath, next point should be Start (or Move).
                    // But Flatten might produce [Start, Line, Line|Close].
                    // Next point (if any) will be Start.
                }
            }
            
            // Add final subpath
            if (currentSubpath.Count > 1)
            {
                var lp = new LaserPath();
                lp.Points = new List<PointF>(currentSubpath);
                lp.UpdateBounds();
                objects.Add(lp);
            }
        }

        private void AddTextObject(string text, List<LaserObject> objects)
        {
            if (!_currentVisibility) return;
            // Resolve Font info
            PdfFontInfo fontInfo = ResolveCurrentFont();
            
            // Decoded text if needed
            string decodedText = text;
            if (fontInfo.IsWinAnsi)
            {
                 try 
                 {
                    byte[] raw = new byte[text.Length];
                    for(int i=0;i<text.Length;i++) raw[i] = (byte)text[i];
                    decodedText = Encoding.GetEncoding("iso-8859-1").GetString(raw);
                 }
                 catch {}
            }
            
            // Calculate Width & Update Matrix
            float totalWidth = 0;
            if (!string.IsNullOrEmpty(text))
            {
                totalWidth = GetTextWidth(text, fontInfo, _state.FontSize, _state.TextHScale);
            }
            
            // Current Position
            PointF[] pts = new PointF[] { new PointF(0, _state.TextRise) };
            _textMatrix.TransformPoints(pts);
            _state.CTM.TransformPoints(pts);
            
            // Update Matrix for NEXT text
            // Translate Tm by (width, 0)
            _textMatrix.Translate(totalWidth, 0, MatrixOrder.Prepend);

            if (string.IsNullOrEmpty(decodedText)) return;
            
             // Visibility Checks
            if (_state.RenderMode == 3) return; // Invisible
            if (_state.RenderMode == 7) return; // Clip Only
            
            bool isFilled = (_state.RenderMode == 0 || _state.RenderMode == 2 || _state.RenderMode == 4 || _state.RenderMode == 6);
            bool isStroked = (_state.RenderMode == 1 || _state.RenderMode == 2 || _state.RenderMode == 5 || _state.RenderMode == 6);
            
            if (isFilled && _state.FillAlpha < 0.05f) return;
            if (isStroked && !isFilled && _state.StrokeAlpha < 0.05f) return;
            if (isFilled && IsWhite(_state.FillColor)) return;
            if (isStroked && !isFilled && IsWhite(_state.StrokeColor)) return;
            if (Math.Abs(_state.TextHScale) < 0.1f) return;
            
            if (_state.ClipRegion != null && !_state.ClipRegion.IsVisible(pts[0])) return;

            // Effective Size
            float ctmScaleY = (float)Math.Sqrt(_state.CTM.Elements[2] * _state.CTM.Elements[2] + _state.CTM.Elements[3] * _state.CTM.Elements[3]);
            if (ctmScaleY == 0) ctmScaleY = 1.0f;
            float tmScaleY = (float)Math.Sqrt(_textMatrix.Elements[2] * _textMatrix.Elements[2] + _textMatrix.Elements[3] * _textMatrix.Elements[3]); 
            if (tmScaleY == 0) tmScaleY = 1.0f;

            float effectiveFontSize = _state.FontSize * tmScaleY * ctmScaleY;
            if (effectiveFontSize < 0.5f) return;

             // Create Object
            var lt = new LaserText();
            lt.Text = decodedText;
            lt.Position = pts[0];
            lt.FontSize = effectiveFontSize;
            lt.FontName = fontInfo.BaseFont ?? "Arial";
            
            // Helper for estimated visual width
            float estVisualWidth = decodedText.Length * effectiveFontSize * 0.5f; 
            lt.Size = new SizeF(estVisualWidth, effectiveFontSize);
            
            float rotation = (float)Math.Atan2(_state.CTM.Elements[1], _state.CTM.Elements[0]) * (180f / (float)Math.PI);
            lt.Rotation = rotation;
            
            if (!string.IsNullOrWhiteSpace(lt.Text))
            {
                objects.Add(lt);
            }
        }

        private PdfFontInfo ResolveCurrentFont()
        {
            if (_state.FontName == null) return new PdfFontInfo { MissingWidth = 600 };
            
            string name = _state.FontName.Name;
            if (_fontCache.ContainsKey(name)) return _fontCache[name];
            
            var info = new PdfFontInfo { BaseFont = "Arial", MissingWidth = 600 };
            _fontCache[name] = info; 
            
            if (_resources == null) return info;
            var fonts = _reader.Resolve(_resources.Get("Font")) as PdfDictionary;
            if (fonts == null) return info;
            
            var fontDict = _reader.Resolve(fonts.Get(name)) as PdfDictionary;
            if (fontDict == null) return info;
            
            var baseFont = _reader.Resolve(fontDict.Get("BaseFont")) as PdfName;
            if (baseFont != null) 
            {
                string fn = baseFont.Name;
                if (fn.Contains("+")) fn = fn.Substring(fn.IndexOf('+') + 1);
                info.BaseFont = fn;
            }
            
            var encoding = _reader.Resolve(fontDict.Get("Encoding"));
            if (encoding is PdfName encName && encName.Name == "WinAnsiEncoding")
            {
                info.IsWinAnsi = true;
            }
            
            // Widths
            var firstCharObj = _reader.Resolve(fontDict.Get("FirstChar")) as PdfNumber;
            var lastCharObj = _reader.Resolve(fontDict.Get("LastChar")) as PdfNumber;
            var widthsObj = _reader.Resolve(fontDict.Get("Widths")) as PdfArray;
            
            if (firstCharObj != null && lastCharObj != null && widthsObj != null)
            {
                info.FirstChar = (int)firstCharObj.IntValue;
                info.LastChar = (int)lastCharObj.IntValue;
                info.Widths = widthsObj.Items.Select(x => (int)((PdfNumber)x).IntValue).ToArray();
            }
            
            return info;
        }
        
        private float GetTextWidth(string text, PdfFontInfo font, float fontSize, float hScale)
        {
            float total = 0;
            float charSpace = _state.TextCharSpacing;
            float wordSpace = _state.TextWordSpacing;

            foreach(char c in text)
            {
                int w = font.MissingWidth;
                if (font.Widths != null)
                {
                    int code = (int)c;
                    if (code >= font.FirstChar && code <= font.LastChar)
                    {
                        int idx = code - font.FirstChar;
                        if (idx >= 0 && idx < font.Widths.Length) w = font.Widths[idx];
                    }
                }
                
                // Convert glyph width to text space
                float glyphWidth = w * (fontSize / 1000f);
                
                // Add CharSpacing to every char
                glyphWidth += (charSpace * (fontSize / 1000f)); // Wait, Ts/Tc units are valid in unscaled text space.
                // Spec: Tc is added to the horizontal displacement.
                // "The value is added to the horizontal displacement... units are unscaled text space units."
                // So if FontSize is 12, and Tc is 0.5. Displacement += 0.5 * text_space_scale?
                // Actually: "expressed in unscaled text space units". This means it depends on Tfs (FontSize)? No. "Unscaled text space" means BEFORE FontSize scaling?
                // Spec 1.7 5.2.1: "Tc parameter is a number... expressed in unscaled text space units."
                // The position Update: tx_new = tx_old + ((w0 - Tj/1000)*Tfs + Tc + (tw_if_space))*Th
                // w0 = Width from font (thousandths)
                // Tj = Kernel (thousandths)
                // Tfs = Font Size
                // Tc = Char Spacing
                // Th = Horiz Scale / 100
                
                // So: Width = (w0/1000 * Size) + Tc
                // My existing code: total += w * (fontSize/1000f) * (hScale/100f)
                
                // Correct Formula per glyph:
                // width = ( (w0 / 1000.0 * fontSize) + charSpace + (c == ' ' ? wordSpace : 0) ) * (hScale / 100.0);
                
                float glyphW = (w / 1000.0f * fontSize) + charSpace;
                if (c == 32) glyphW += wordSpace; // 32 is Space
                
                total += glyphW;
            }
            return total * (hScale / 100f);
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
                     int width = 0;
                     int height = 0;
                     var wObj = _reader.Resolve(stream.Dictionary.Get("Width")) as PdfNumber;
                     var hObj = _reader.Resolve(stream.Dictionary.Get("Height")) as PdfNumber;
                     
                     if (wObj != null) width = (int)wObj.IntValue;
                     if (hObj != null) height = (int)hObj.IntValue;
                     
                     if (width > 0 && height > 0)
                     {
                          Warnings.Add($"Found Image {width}x{height}. Filter: {_reader.Resolve(stream.Dictionary.Get("Filter"))}");
                          
                          // Calculate Bounds in User Space
                          PointF[] corners = new PointF[] { new PointF(0, 0), new PointF(1, 0), new PointF(1, 1), new PointF(0, 1) };
                          _state.CTM.TransformPoints(corners);
                          
                          float minX = corners.Min(p => p.X);
                          float minY = corners.Min(p => p.Y);
                          float maxX = corners.Max(p => p.X);
                          float maxY = corners.Max(p => p.Y);
                          
                          var li = new LaserImage();
                          li.Position = new PointF(minX, minY);
                          li.Size = new SizeF(maxX - minX, maxY - minY);
                          li.Rotation = 0;
                          
                          // TODO: Load Actual Data
                         if (stream.Data != null && stream.Data.Length > 0)
                          {
                               // Try to decode basic formats:
                               var colorSpace = _reader.Resolve(stream.Dictionary.Get("ColorSpace"));
                               var bitsPerComponent = _reader.Resolve(stream.Dictionary.Get("BitsPerComponent")) as PdfNumber;
                               int bpc = (int)(bitsPerComponent?.IntValue ?? 8);
                               
                               string csName = "DeviceRGB";
                               if (colorSpace is PdfName n) csName = n.Name;
                               // Note: ColorSpace can be an array [ /Indexed /DeviceRGB ... ] - simplistic check for now
                               
                               if ((csName == "DeviceRGB" || csName == "DeviceGray") && bpc == 8)
                               {
                                   try 
                                   {
                                        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                                        var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
                                        
                                        // Copy data
                                        // PDF RGB is R G B. 24bppRgb in GDI+ is usually B G R... need to swap?
                                        // Let's copy row by row.
                                        
                                        byte[] pixelData = stream.Data;
                                        int stride = bmpData.Stride;
                                        IntPtr ptr = bmpData.Scan0;
                                        int bytesPerPixel = csName == "DeviceRGB" ? 3 : 1; // Basic assumption
                                        
                                        // Safety check
                                        if (pixelData.Length >= width * height * bytesPerPixel)
                                        {
                                            byte[] dstBytes = new byte[height * stride];
                                            for (int y = 0; y < height; y++)
                                            {
                                                for (int x = 0; x < width; x++)
                                                {
                                                    int srcIdx = (y * width + x) * bytesPerPixel;
                                                    int dstIdx = y * stride + x * 3;

                                                    byte r = pixelData[srcIdx];
                                                    byte g = bytesPerPixel == 3 ? pixelData[srcIdx + 1] : r;
                                                    byte b = bytesPerPixel == 3 ? pixelData[srcIdx + 2] : r;

                                                    // GDI+ 24bpp is BGR
                                                    dstBytes[dstIdx] = b;
                                                    dstBytes[dstIdx + 1] = g;
                                                    dstBytes[dstIdx + 2] = r;
                                                }
                                            }
                                            Marshal.Copy(dstBytes, 0, ptr, dstBytes.Length);

                                           bmp.UnlockBits(bmpData); // Unlock RGB data before flip/mask
                                           
                                           // Re-apply RotateFlip as user confirmed it's needed (PDF vs GDI+ coordinates)
                                           // lb.Bitmap assignment removed
                                           // bmp.RotateFlip(RotateFlipType.RotateNoneFlipY); // Removed prior flip too 
                                           
                                           // Check for SMask (Soft Mask) for transparency
                                           var smaskObj = _reader.Resolve(stream.Dictionary.Get("SMask"));
                                           var maskObj = _reader.Resolve(stream.Dictionary.Get("Mask"));
                                           
                                           PdfStream? maskStream = smaskObj as PdfStream;
                                           if (maskStream == null && maskObj is PdfStream ms) maskStream = ms;
                                           
                                           if (maskStream != null)
                                           {
                                                System.Drawing.Imaging.BitmapData? mainData = null;
                                                System.Drawing.Imaging.BitmapData? resData = null;
                                                Bitmap? builder = null;
                                                
                                                try 
                                                {
                                                    // Need to flip mask too if we flipped the main image!
                                                    // Or better: Apply mask FIRST, then Flip result?
                                                    // Flipping is lossy/slow? No, just data move.
                                                    // Let's Flip at the END.
                                                    // Undo the flip for now to apply mask? 
                                                    // Actually, if we flip 'bmp', we must flip 'mask' data row access or just assume mask is also upside down?
                                                    // Usually Mask is in same coord system as Image.
                                                    // So if we Flip bmp, we must Flip mask logic or access mask inverted.
                                                    // Easier: Apply Mask FIRST, Then Flip FINAL result.
                                                    
                                                    // Revert previous flip for a moment (or just don't do it yet)
                                                    // bmp.RotateFlip(RotateFlipType.RotateNoneFlipY); // Removed (No need to revert if we didn't flip)
                                                    
                                                    // Decode SMask (Usually DeviceGray 8bpc)
                                                    var mwObj = _reader.Resolve(maskStream.Dictionary.Get("Width")) as PdfNumber;
                                                    var mhObj = _reader.Resolve(maskStream.Dictionary.Get("Height")) as PdfNumber;
                                                    int mw = (int)(mwObj?.IntValue ?? width);
                                                    int mh = (int)(mhObj?.IntValue ?? height);
                                                    
                                                    if (mw == width && mh == height && maskStream.Data != null)
                                                    {
                                                         // Unlock bmp if it was locked? In this scope, it is NOT locked.
                                                         // The previous rgb-decoding block has finished and we are outside it.
                                                    
                                                         // Apply Alpha Channel
                                                         builder = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                                                         mainData = bmp.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
                                                         var maskData = maskStream.Data;
                                                         resData = builder.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, builder.PixelFormat);
                                                         
                                                         int srcStride = mainData.Stride;
                                                         int dstStride = resData.Stride;
                                                         byte[] srcLine = new byte[srcStride];
                                                         byte[] dstLine = new byte[dstStride];

                                                         for (int y = 0; y < height; y++)
                                                         {
                                                             Marshal.Copy((IntPtr)(mainData.Scan0.ToInt64() + y * srcStride), srcLine, 0, srcStride);
                                                             for (int x = 0; x < width; x++)
                                                             {
                                                                 byte b = srcLine[x * 3];
                                                                 byte g = srcLine[x * 3 + 1];
                                                                 byte r = srcLine[x * 3 + 2];

                                                                 int alpha = 255;
                                                                 if (maskData.Length >= width * height) alpha = maskData[y * width + x];

                                                                 dstLine[x * 4] = b;
                                                                 dstLine[x * 4 + 1] = g;
                                                                 dstLine[x * 4 + 2] = r;
                                                                 dstLine[x * 4 + 3] = (byte)alpha;
                                                             }
                                                             Marshal.Copy(dstLine, 0, (IntPtr)(resData.Scan0.ToInt64() + y * dstStride), dstLine.Length);
                                                         }

                                                         
                                                         Warnings.Add("Applied Transparency Mask (SMask).");
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Warnings.Add($"Failed to apply SMask: {ex.Message}");
                                                    builder?.Dispose(); // Fail safe
                                                    builder = null;
                                                }
                                                finally
                                                {
                                                    if (mainData != null) bmp.UnlockBits(mainData);
                                                    if (resData != null && builder != null) builder.UnlockBits(resData);
                                                }
                                                
                                                if (builder != null)
                                                {
                                                    bmp.Dispose();
                                                    bmp = builder;
                                                }
                                           }
                                           
                                           // Coordinate System Flip:
                                           // LaserImage.Draw applies a Scale(1, -1) which flips the Y-axis.
                                           // Therefore, we do NOT need to flip the bitmap here. Standard Top-Down bitmap matches.
                                           // bmp.RotateFlip(RotateFlipType.RotateNoneFlipY); // Removed
                                           
                                           li.Image = bmp; // Assign the processed bitmap to the LaserImage object
                                           
                                           Warnings.Add($"Image decoded successfully ({csName}).");
                                        }
                                        else
                                        {
                                            bmp.UnlockBits(bmpData);
                                            bmp.Dispose();
                                            Warnings.Add($"Image data length mismatch. Expected {width*height*bytesPerPixel}, got {pixelData.Length}.");
                                        }
                                   }
                                   catch(Exception ex)
                                   {
                                       Warnings.Add($"Failed to decode bitmap: {ex.Message}");
                                   }
                               }
                               else
                               {
                                   Warnings.Add($"Image Data Present but format not supported for decoding: {csName} {bpc}bpc.");
                               }
                          }
                          
                          objects.Add(li); // Add here to support placeholders if decoding failed/skipped
                     }
                     else
                     {
                         Warnings.Add($"Image XObject found but invalid Size: {width}x{height}");
                     }
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

        private void ProcessExtGState(PdfName name)
        {
            if (_resources == null) return;
            var extGState = _reader.Resolve(_resources.Get("ExtGState")) as PdfDictionary;
            if (extGState == null) return;
            
            var dictionary = _reader.Resolve(extGState.Get(name.Name)) as PdfDictionary;
            if (dictionary == null) return;
            
            // Handle Alpha
            // ca = Non-Stroking Alpha (Fill)
            // CA = Stroking Alpha
            if (dictionary.ContainsKey("ca"))
            {
                var ca = _reader.Resolve(dictionary.Get("ca")) as PdfNumber;
                if (ca != null) _state.FillAlpha = (float)ca.RealValue;
            }
            if (dictionary.ContainsKey("CA"))
            {
                var CA = _reader.Resolve(dictionary.Get("CA")) as PdfNumber;
                if (CA != null) _state.StrokeAlpha = (float)CA.RealValue;
            }
        }

        private void ProcessBDC(PdfDictionary props)
        {
            bool isVisible = _currentVisibility; // Inherit parent

            if (isVisible && props.ContainsKey("OC"))
            {
                var ocRef = props.Get("OC"); // Don't resolve yet if we want the Reference itself
                // Actually Resolve() returns the Object. If it was a Ref, we lose the Ref info?
                // PdfReader.Resolve DOES return the Object, masking the Reference.
                // But IsOCGVisible expects Reference for equality check!
                // We need the Reference to check against our OFF List.
                
                // We need 'GetRaw' or check if item is PdfReference before Resolving?
                // PdfDictionary.Get returns the raw object (which might be PdfReference).
                var rawOC = props.Get("OC");
                
                if (rawOC is PdfReference refObj)
                {
                     // Use Ref ID
                     if (!IsOCGVisible(refObj)) isVisible = false;
                }
                else
                {
                    // It's a direct object (Dictionary?) or Name?
                    // Resolve it to be sure
                    var resolved = _reader.Resolve(rawOC);
                    if (resolved != null && !IsOCGVisible(resolved)) isVisible = false; // Check for null
                }
            }

            _visibilityStack.Push(isVisible); 
            _currentVisibility = isVisible;
        }

        private void UpdateClipBounds()
        {
             if (_currentPath.PointCount == 0) return;
             
             // Transform path to device space
             using var tempPath = (GraphicsPath)_currentPath.Clone();
             tempPath.Transform(_state.CTM);
             
             if (_state.ClipRegion == null)
             {
                 _state.ClipRegion = new Region(tempPath);
             }
             else
             {
                 _state.ClipRegion.Intersect(tempPath);
             }
             _isClipping = false;
        }

        private bool IsIgnoredStateOperator(string op)
        {
            // Common state operators that don't affect shape geometry directly for MVP
            // w, J, j, M, d, ri, i, gs, cs, CS...
            // REMOVED W, W*, BDC, EMC from ignore list
            return op == "w" || op == "J" || op == "j" || op == "M" || op == "d" || op == "ri" || op == "i" || op == "gs" || op == "cs" || op == "CS"
                   || op == "BMC" || op == "DP" || op == "MP";
        }
        
        private double GetNum(PdfObject obj)
        {
            if (obj is PdfNumber n) return n.RealValue;
            return 0;
        }

        private void SetColor(List<PdfObject> operands, bool stroke, string space)
        {
            float[] vals = operands.Select(o => (float)GetNum(o)).ToArray();
            Color c = Color.Black;
            
            if (space == "Gray" && vals.Length >= 1)
            {
                int v = (int)(vals[0] * 255);
                v = Math.Clamp(v, 0, 255);
                c = Color.FromArgb(v, v, v);
            }
            else if (space == "RGB" && vals.Length >= 3)
            {
                int r = (int)(vals[0] * 255);
                int g = (int)(vals[1] * 255);
                int b = (int)(vals[2] * 255);
                c = Color.FromArgb(Math.Clamp(r,0,255), Math.Clamp(g,0,255), Math.Clamp(b,0,255));
            }
            else if (space == "CMYK" && vals.Length >= 4)
            {
                float C = vals[0];
                float M = vals[1];
                float Y = vals[2];
                float K = vals[3];
                // Simple CMYK to RGB
                int r = (int)(255 * (1 - C) * (1 - K));
                int g = (int)(255 * (1 - M) * (1 - K));
                int b = (int)(255 * (1 - Y) * (1 - K));
                 c = Color.FromArgb(Math.Clamp(r,0,255), Math.Clamp(g,0,255), Math.Clamp(b,0,255));
            }
            
            if (stroke) _state.StrokeColor = c;
            else _state.FillColor = c;
        }
        
        private bool IsWhite(Color c)
        {
            return c.R > 250 && c.G > 250 && c.B > 250; // Near White
        }

        private void EnsureOCPropertiesLoaded()
        {
            if (_ocPropertiesLoaded) return;
            _ocPropertiesLoaded = true;

            var trailer = _reader.Trailer;
            if (trailer == null) return;
            
            var root = _reader.Resolve(trailer.Get("Root")) as PdfDictionary;
            if (root == null) return;
            
            var ocProps = _reader.Resolve(root.Get("OCProperties")) as PdfDictionary;
            if (ocProps == null) return;
            
            // Default Config
            var d = _reader.Resolve(ocProps.Get("D")) as PdfDictionary;
            if (d == null) return;
            
            // Base State
            var baseState = _reader.Resolve(d.Get("BaseState")) as PdfName;
            if (baseState != null && baseState.Name == "OFF") _ocBaseStateOn = false;
            
            // OFF Array
            var offArr = _reader.Resolve(d.Get("OFF")) as PdfArray;
            if (offArr != null)
            {
                foreach(var item in offArr.Items)
                {
                    if (item is PdfReference refObj) _ocOffList.Add(refObj);
                }
            }

            // ON Array
            var onArr = _reader.Resolve(d.Get("ON")) as PdfArray;
            if (onArr != null)
            {
                foreach(var item in onArr.Items)
                {
                    if (item is PdfReference refObj) _ocOnList.Add(refObj);
                }
            }
        }

        private bool IsOCGVisible(PdfObject ocgOrName)
        {
            EnsureOCPropertiesLoaded();
            
            if (ocgOrName is PdfReference refObj)
            {
                // Simple Check: is it in OFF list or ON list vs BaseState
                if (_ocBaseStateOn)
                {
                   // ON by default, unless in OFF list
                   if (_ocOffList.Contains(refObj)) return false;
                   return true;
                }
                else
                {
                   // OFF by default, unless in ON list
                   if (_ocOnList.Contains(refObj)) return true;
                   return false;
                }
            }
             else if (ocgOrName is PdfDictionary ocmd)
             {
                 // Handle OCMD (Membership Dictionary)
                 // /OCGs [ ... ] or single dict
                 // /P (Policy): AllOn, AnyOn, AnyOff, AllOff
                 // Implement basic "AnyOn" logic for default?
                 
                 // Get OCGs
                 var ocgsObj = _reader.Resolve(ocmd.Get("OCGs"));
                 List<PdfReference> list = new List<PdfReference>();
                 if (ocgsObj is PdfReference singleRef) list.Add(singleRef);
                 else if (ocgsObj is PdfArray arr)
                 {
                     foreach(var item in arr.Items)
                     {
                         if (item is PdfReference r) list.Add(r);
                     }
                 }
                 
                 if (list.Count == 0) return true; // No OCGs? Visible.
                 
                 var policy = _reader.Resolve(ocmd.Get("P")) as PdfName;
                 string pName = policy?.Name ?? "AnyOn";
                 
                 if (pName == "AllOn")
                 {
                     return list.All(r => IsOCGVisible(r));
                 }
                 else if (pName == "AnyOff") // Visible if AT LEAST ONE is OFF
                 {
                      return list.Any(r => !IsOCGVisible(r));
                 }
                 else if (pName == "AllOff")
                 {
                     return list.All(r => !IsOCGVisible(r));
                 }
                 else // AnyOn (Default)
                 {
                     return list.Any(r => IsOCGVisible(r));
                 }
             }

            // Fallback: If it's a Name (not standard for OCG usage in BDC but possible?), visible.
            return true;
        }
    }
}
