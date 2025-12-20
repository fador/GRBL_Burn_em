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
            public int RenderMode { get; set; } = 0; 
            public float FillAlpha { get; set; } = 1.0f;
            public float StrokeAlpha { get; set; } = 1.0f;
            
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
                    StrokeAlpha = StrokeAlpha
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
                        _textLineMatrix.Translate(tx, ty);
                        _textMatrix = _textLineMatrix.Clone();
                    }
                    break;
                case "Tr": // Text Rendering Mode
                    if (operands.Count == 1)
                    {
                        _state.RenderMode = (int)GetNum(operands[0]);
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
            if (_currentPath.PointCount == 0) return;
            
            // Filter by Color (White Geometry = Invisible/Mask usually)
            // If Filled: Check FillColor
            if (filled && IsWhite(_state.FillColor)) return;
            // If Stroked (not filled): Check StrokeColor
            if (!filled && IsWhite(_state.StrokeColor)) return;
            using var transformedPath = (GraphicsPath)_currentPath.Clone();
            transformedPath.Transform(_state.CTM);
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
            if (string.IsNullOrEmpty(text)) return;
            
            // Calculate Position: (0,0) in Text Space -> Transformed by Tm -> Transformed by CTM
            PointF[] pts = new PointF[] { new PointF(0, 0) };
            _textMatrix.TransformPoints(pts);
            _state.CTM.TransformPoints(pts);

            // Font Information
            string fontName = "Arial";
            bool isWinAnsi = false;
            
            if (_state.FontName != null && _resources != null)
            {
                 var fonts = _reader.Resolve(_resources.Get("Font")) as PdfDictionary;
                 if (fonts != null)
                 {
                     var fontDict = _reader.Resolve(fonts.Get(_state.FontName.Name)) as PdfDictionary;
                     if (fontDict != null)
                     {
                         // Check Encoding
                         var encoding = _reader.Resolve(fontDict.Get("Encoding"));
                         if (encoding is PdfName encName && encName.Name == "WinAnsiEncoding")
                         {
                             isWinAnsi = true;
                         }

                         var baseFont = _reader.Resolve(fontDict.Get("BaseFont")) as PdfName;
                         if (baseFont != null)
                         {
                             fontName = baseFont.Name;
                             if (fontName.Contains("+")) fontName = fontName.Substring(fontName.IndexOf('+') + 1);
                             if (fontName.Contains(",")) fontName = fontName.Split(',')[0];
                             if (fontName.Contains("-")) fontName = fontName.Split('-')[0];
                         }
                     }
                 }
            }

            // Decode Text if needed
            if (isWinAnsi)
            {
                // So checking chars for values > 127 and re-mapping might work, or using the specific encoding.
                
                // Quick hack: Re-encode to bytes then to 1252
                byte[] raw = new byte[text.Length];
                for(int i=0;i<text.Length;i++) raw[i] = (byte)text[i];
                
                try 
                {
                    // Register provider if needed? .NET Core usually needs it.
                    // System.Text.Encoding.CodePages is typically needed. 
                    // Fallback: Manual mapping for common chars or blindly trusting internal conversion?
                    // Let's assume generic "Western" chars for now using ISO-8859-1 which is close to WinAnsi
                    text = Encoding.GetEncoding("iso-8859-1").GetString(raw);
                }
                catch
                {
                    // Fallback if encoding not available
                    // Text might be legible enough
                }
            }
            // User Issue: "two numbers" for UTF-8 chars?
            // If the PDF uses a Identity-H / UTF-8 but we treat as bytes?
            // If text contains sequences like \xc3\xa4 (ä), and we see it as "Ã¤", that's UTF-8 interpreted as Latin1.
            // If user sees "two numbers" maybe they mean the font name or something else?
            // But if text is invisible, we skip it anyway.
            
            if (_state.RenderMode == 3) return; // Invisible
            if (_state.RenderMode == 7) return; // Clip Only (Invisible)
            
            // Check Transparency (Alpha)
            // If Text is Filled (Mode 0, 2, 4...) check FillAlpha.
            // If Text is Stroked (Mode 1, 2, 5...) check StrokeAlpha.
            // Common case: 0 (Fill).
            bool isFilled = (_state.RenderMode == 0 || _state.RenderMode == 2 || _state.RenderMode == 4 || _state.RenderMode == 6);
            bool isStroked = (_state.RenderMode == 1 || _state.RenderMode == 2 || _state.RenderMode == 5 || _state.RenderMode == 6);
            
            if (isFilled && _state.FillAlpha < 0.05f) return; // Effectively transparent
            if (isStroked && !isFilled && _state.StrokeAlpha < 0.05f) return; // Stroked only and transparent
            
            // Check Color (White = Ignore)
            if (isFilled && IsWhite(_state.FillColor)) return;
            if (isStroked && !isFilled && IsWhite(_state.StrokeColor)) return;
            
            // Font Size scaling
            
            // Font Size scaling
            // Text Matrix scale * CTM scale * FontSize
            // We need to extract the "effective" scaling factor from the CTM/TextMatrix.
            // Simplified: The Y-scale of the CTM is a good approximation for uniform scaling.
            // CTM is [m11 m12 m21 m22 dx dy]. Y-scale is roughly sqrt(m21^2 + m22^2) or just m22 if no rotation/skew.
            float ctmScaleY = (float)Math.Sqrt(_state.CTM.Elements[2] * _state.CTM.Elements[2] + _state.CTM.Elements[3] * _state.CTM.Elements[3]);
            if (ctmScaleY == 0) ctmScaleY = 1.0f; // Safety
            
            // Also consider Text Matrix (Tm) scaling if separate? 
            // Currently _textMatrix is applied to Position. 
            // In PDF, Text Size is Tf size parameter. Text Matrix scales coordinate system.
            // So Effective Size = Tf_Size * Tm_Scale * CTM_Scale.
            
            // Re-calculate scale from Text Matrix too
            float tmScaleY = (float)Math.Sqrt(_textMatrix.Elements[2] * _textMatrix.Elements[2] + _textMatrix.Elements[3] * _textMatrix.Elements[3]);
            if (tmScaleY == 0) tmScaleY = 1.0f;

            float effectiveFontSize = _state.FontSize * tmScaleY * ctmScaleY;
            
            var lt = new LaserText();
            lt.Text = text;
            lt.Position = pts[0];
            lt.FontSize = effectiveFontSize; 
            lt.FontName = fontName;
            
            // Filter out empty or whitespace-only text
            if (string.IsNullOrWhiteSpace(lt.Text)) return;

            // Filter out near-zero font sizes. 
            // PDF units are usually points (1/72 inch). 
            // 0.5 effective PDF units is still small (~0.17mm).
            // Let's filter anything smaller than 0.5 effective units.
            if (lt.FontSize > 0.5f)
            {
                 objects.Add(lt);
            }
            else
            {
                 // Warnings.Add($"Skipped zero-size text '{text}' (Size: {lt.FontSize:F2})");
            }
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
                                           unsafe 
                                           {
                                                byte* pPtr = (byte*)ptr;
                                                for(int y=0; y<height; y++)
                                                {
                                                    for(int x=0; x<width; x++)
                                                    {
                                                        int srcIdx = (y * width + x) * bytesPerPixel;
                                                        int dstIdx = y * stride + x * 3;
                                                        
                                                        byte r = pixelData[srcIdx];
                                                        byte g = bytesPerPixel == 3 ? pixelData[srcIdx+1] : r;
                                                        byte b = bytesPerPixel == 3 ? pixelData[srcIdx+2] : r;
                                                        
                                                        // GDI+ 24bpp is BGR
                                                        pPtr[dstIdx] = b;
                                                        pPtr[dstIdx+1] = g;
                                                        pPtr[dstIdx+2] = r;
                                                    }
                                                }
                                           }
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
                                                         
                                                         unsafe 
                                                         {
                                                             byte* srcPtr = (byte*)mainData.Scan0;
                                                             byte* dstPtr = (byte*)resData.Scan0;
                                                             int srcStride = mainData.Stride;
                                                             int dstStride = resData.Stride;
                                                             
                                                             for(int y=0; y<height; y++)
                                                             {
                                                                 for(int x=0; x<width; x++)
                                                                 {
                                                                     byte b = srcPtr[y * srcStride + x * 3];
                                                                     byte g = srcPtr[y * srcStride + x * 3 + 1];
                                                                     byte r = srcPtr[y * srcStride + x * 3 + 2];
                                                                     
                                                                     int alpha = 255;
                                                                     if (maskData.Length >= width*height) alpha = maskData[y * width + x];
                                                                     
                                                                     dstPtr[y * dstStride + x * 4] = b;
                                                                     dstPtr[y * dstStride + x * 4 + 1] = g;
                                                                     dstPtr[y * dstStride + x * 4 + 2] = r;
                                                                     dstPtr[y * dstStride + x * 4 + 3] = (byte)alpha;
                                                                 }
                                                             }
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
            // W, W* = Clipping paths (Ignored for MVP visibility)
            return op == "w" || op == "J" || op == "j" || op == "M" || op == "d" || op == "ri" || op == "i" || op == "gs" || op == "cs" || op == "CS"
                   || op == "BDC" || op == "EMC" || op == "BMC" || op == "DP" || op == "MP" || op == "W" || op == "W*";
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
    }
}
