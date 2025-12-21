using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.IO;

using System.Runtime.Versioning;

namespace laser_gui_test.Data;

[SupportedOSPlatform("windows")]
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Possible null reference argument.
public class SvgImporter
{
    public static List<LaserObject> Import(string filePath)
    {
        var laserObjects = new List<LaserObject>();
        
        try
        {
            var xml = XDocument.Load(filePath);
            var svg = xml.Root;
            if (svg == null || svg.Name.LocalName != "svg") return laserObjects;

            // Parse document dimensions to establish coordinate system
            // Parse document dimensions to establish coordinate system
            // ParseDimension returns Pixels (96 DPI) for units like mm/cm/in/pt.
            // We want the internal system to be in Millimeters (Target Units).
            float pxToMm = 25.4f / 96.0f;
            float width = ParseDimension(svg.Attribute("width")?.Value, 0) * pxToMm;
            float height = ParseDimension(svg.Attribute("height")?.Value, 0) * pxToMm;
            
            var viewBoxAttr = svg.Attribute("viewBox");
            float vx = 0, vy = 0, vw = 0, vh = 0;
            bool hasViewBox = false;

            if (viewBoxAttr != null)
            {
                var parts = ParseNumbers(viewBoxAttr.Value);
                if (parts.Length == 4)
                {
                    vx = parts[0]; vy = parts[1];
                    vw = parts[2]; vh = parts[3];
                    hasViewBox = true;

                    if (width == 0) width = vw;
                    if (height == 0) height = vh;
                }
            }

            // Default fallback if nothing specified (rare)
            if (height == 0) height = 1000;
            if (width == 0) width = (hasViewBox && vw > 0) ? vw : 1000;

            // Prepare Global Transform
            // 1. ViewBox Transform (if applicable): Map ViewBox to (0,0)-(width,height)
            // 2. Laser Flip: Map (0,0)-(width,height) Y-Down to Y-Up
            
            using var globalTransform = new Matrix(); // Identity

            if (hasViewBox && vw > 0 && vh > 0)
            {
                // Translate SVG content so ViewBox top-left (vx, vy) is at (0,0)
                globalTransform.Translate(-vx, -vy);
                // Scale so ViewBox size (vw, vh) matches Document size (width, height)
                globalTransform.Scale(width / vw, height / vh, MatrixOrder.Append);
            }
            else
            {
                // No ViewBox: coordinate system is pixels (96 DPI).
                // Scale to Millimeters.
                globalTransform.Scale(pxToMm, pxToMm, MatrixOrder.Append);
            }

            // Laser coordinate system flip: Y-Up vs SVG Y-Down
            // Transform: Scale(1, -1) then Translate(0, height)
            globalTransform.Scale(1, -1, MatrixOrder.Append);
            globalTransform.Translate(0, height, MatrixOrder.Append);

            ParseElement(svg, laserObjects, globalTransform);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing SVG: {ex.Message}");
        }

        return laserObjects;
    }

    private static void ParseElement(XElement elem, List<LaserObject> list, Matrix parentTransform)
    {
        // Handle transforms
        using var currentTransform = parentTransform.Clone();
        var transformAttr = elem.Attribute("transform");
        if (transformAttr != null)
        {
            using var localMat = ParseTransform(transformAttr.Value);
            currentTransform.Multiply(localMat);
        }

        bool isVisible = IsVisible(elem);
        // We traverse children even if "g" has clear visibility issues?
        // SVG 'display="none"' applies to children. 
        // 'visibility="hidden"' elements still take space but don't draw. 
        // For laser, hidden usually means ignore.
        
        if (!isVisible) return;

        string name = elem.Name.LocalName.ToLower();

        switch (name)
        {
            case "g":
            case "svg":
            case "a": // anchor, treat as group
                foreach (var child in elem.Elements())
                {
                    ParseElement(child, list, currentTransform);
                }
                break;
                
            case "path":
                ParsePath(elem, list, currentTransform);
                break;
                
            case "rect":
                ParseRect(elem, list, currentTransform);
                break;
                
            case "circle":
                ParseCircle(elem, list, currentTransform);
                break;
                
            case "ellipse":
                ParseEllipse(elem, list, currentTransform);
                break;
                
            case "line":
                ParseLine(elem, list, currentTransform);
                break;
                
            case "polyline":
                ParsePolyline(elem, list, currentTransform, false);
                break;
                
            case "polygon":
                ParsePolyline(elem, list, currentTransform, true);
                break;
                
            case "image":
                ParseImage(elem, list, currentTransform);
                break;
                
            case "text":
                ParseText(elem, list, currentTransform);
                break;
        }
    }

    private static bool IsVisible(XElement elem)
    {
        var display = GetStyleOrAttribute(elem, "display");
        if (display == "none") return false;

        var visibility = GetStyleOrAttribute(elem, "visibility");
        if (visibility == "hidden" || visibility == "collapse") return false;

        return true;
    }

    private static bool HasStrokeOrFill(XElement elem)
    {
        // For Laser, we care if it exists visually.
        // Fill defaults to black in SVG if not specified? Actually defaults to black.
        // But if fill="none" and stroke="none", ignore.
        
        string? fill = GetStyleOrAttribute(elem, "fill");
        string? stroke = GetStyleOrAttribute(elem, "stroke");
        
        bool noFill = fill == "none" || fill == "transparent";
        bool noStroke = stroke == "none" || stroke == "transparent";

        // If both are explicitly none, then it's invisible (clipping path etc)
        // Note: Default fill is black, Default stroke is none.
        // So if fill is missing, it is black (visible).
        
        return !(noFill && (noStroke || string.IsNullOrEmpty(stroke)));
    }

    private static void ParsePath(XElement elem, List<LaserObject> list, Matrix transform)
    {
        if (!HasStrokeOrFill(elem)) return;

        var d = elem.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(d)) return;

        using var gp = ParsePathData(d);
        AddGraphicsPath(gp, elem, list, transform);
    }

    private static void ParseRect(XElement elem, List<LaserObject> list, Matrix transform)
    {
        if (!HasStrokeOrFill(elem)) return;

        float x = ParseDimension(elem.Attribute("x")?.Value, 0);
        float y = ParseDimension(elem.Attribute("y")?.Value, 0);
        float w = ParseDimension(elem.Attribute("width")?.Value, 0);
        float h = ParseDimension(elem.Attribute("height")?.Value, 0);
        float rx = ParseDimension(elem.Attribute("rx")?.Value, 0);
        float ry = ParseDimension(elem.Attribute("ry")?.Value, 0);

        if (w <= 0 || h <= 0) return;

        using var gp = new GraphicsPath();
        if (rx > 0 || ry > 0)
        {
            // Simplified rounded rect
            // Precise implementation would add arcs
            // For now, just add rect or approximate?
            // Let's do simple rect for robustness unless requested otherwise
            gp.AddRectangle(new RectangleF(x, y, w, h));
        }
        else
        {
            gp.AddRectangle(new RectangleF(x, y, w, h));
        }

        AddGraphicsPath(gp, elem, list, transform);
    }

    private static void ParseCircle(XElement elem, List<LaserObject> list, Matrix transform)
    {
        if (!HasStrokeOrFill(elem)) return;

        float cx = ParseDimension(elem.Attribute("cx")?.Value, 0);
        float cy = ParseDimension(elem.Attribute("cy")?.Value, 0);
        float r = ParseDimension(elem.Attribute("r")?.Value, 0);

        if (r <= 0) return;

        using var gp = new GraphicsPath();
        gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);

        AddGraphicsPath(gp, elem, list, transform);
    }

    private static void ParseEllipse(XElement elem, List<LaserObject> list, Matrix transform)
    {
        if (!HasStrokeOrFill(elem)) return;

        float cx = ParseDimension(elem.Attribute("cx")?.Value, 0);
        float cy = ParseDimension(elem.Attribute("cy")?.Value, 0);
        float rx = ParseDimension(elem.Attribute("rx")?.Value, 0);
        float ry = ParseDimension(elem.Attribute("ry")?.Value, 0);

        if (rx <= 0 || ry <= 0) return;

        using var gp = new GraphicsPath();
        gp.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);

        AddGraphicsPath(gp, elem, list, transform);
    }

    private static void ParseLine(XElement elem, List<LaserObject> list, Matrix transform)
    {
        if (!HasStrokeOrFill(elem)) return;

        float x1 = ParseDimension(elem.Attribute("x1")?.Value, 0);
        float y1 = ParseDimension(elem.Attribute("y1")?.Value, 0);
        float x2 = ParseDimension(elem.Attribute("x2")?.Value, 0);
        float y2 = ParseDimension(elem.Attribute("y2")?.Value, 0);

        using var gp = new GraphicsPath();
        gp.AddLine(x1, y1, x2, y2);

        AddGraphicsPath(gp, elem, list, transform);
    }

    private static void ParsePolyline(XElement elem, List<LaserObject> list, Matrix transform, bool closed)
    {
        if (!HasStrokeOrFill(elem)) return;

        var pointsAttr = elem.Attribute("points")?.Value;
        if (string.IsNullOrWhiteSpace(pointsAttr)) return;

        var nums = ParseNumbers(pointsAttr);
        if (nums.Length < 4) return;

        var points = new PointF[nums.Length / 2];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new PointF(nums[i * 2], nums[i * 2 + 1]);
        }

        using var gp = new GraphicsPath();
        if (closed)
            gp.AddPolygon(points);
        else
            gp.AddLines(points);

        AddGraphicsPath(gp, elem, list, transform);
    }

    private static void ParseImage(XElement elem, List<LaserObject> list, Matrix transform)
    {
         var href = elem.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href")?.Value 
                   ?? elem.Attribute("href")?.Value;

         if (string.IsNullOrEmpty(href)) return;

         float x = ParseDimension(elem.Attribute("x")?.Value, 0);
         float y = ParseDimension(elem.Attribute("y")?.Value, 0);
         float w = ParseDimension(elem.Attribute("width")?.Value, 0);
         float h = ParseDimension(elem.Attribute("height")?.Value, 0);

         // Load Image Logic
         Bitmap? bitmap = null;
         try 
         {
             if (href.StartsWith("data:image"))
             {
                 var idx = href.IndexOf(",");
                 if (idx > 0)
                 {
                     var base64 = href.Substring(idx + 1);
                     var bytes = Convert.FromBase64String(base64);
                     using var ms = new MemoryStream(bytes);
                     bitmap = new Bitmap(ms);
                 }
             }
             else
             {
                 // Handle relative paths if needed, assuming absolute for now or strictly strictly relative to CWD?
                 // Original logic tried to be smart about paths.
                 // For now, simple check.
                 if (File.Exists(href))
                 {
                     bitmap = new Bitmap(href);
                 }
                 else
                 {
                     // Try relative to the generic workspace? 
                     // Since we don't have original filepath easily here (passed into Import, but static).
                     // We can assume href is absolute or valid.
                 }
             }

             if (bitmap != null)
             {
                 var lImg = new LaserImage();
                 lImg.Name = elem.Attribute("id")?.Value ?? "Image";
                 lImg.Image = new Bitmap(bitmap);
                 lImg.Size = new SizeF(w, h);
                 
                  // Transform position
                 var pts = new PointF[] { new PointF(x, y) };
                 transform.TransformPoints(pts);
                 
                 // Used scale from matrix
                 float scaleX = (float)Math.Sqrt(transform.Elements[0] * transform.Elements[0] + transform.Elements[1] * transform.Elements[1]);
                 
                 lImg.Size = new SizeF(w * scaleX, h * scaleX);
                 // Y-flip correction: SVG Top-Left becomes Laser Bottom-Left
                 lImg.Position = new PointF(pts[0].X, pts[0].Y - lImg.Size.Height);

                 list.Add(lImg);
             }
         }
         catch { }
    }
    
    private static void ParseText(XElement elem, List<LaserObject> list, Matrix transform)
    {
        // Check for textPath
        var textPath = elem.Elements().FirstOrDefault(e => e.Name.LocalName == "textPath");
        if (textPath != null)
        {
            ParseTextPath(elem, textPath, list, transform);
            return;
        }

        // Minimal text support
        var txt = elem.Value?.Trim();
        if (string.IsNullOrEmpty(txt))
        {
             // Check tspan
             var tspan = elem.Element(XNamespace.Get("http://www.w3.org/2000/svg") + "tspan");
             if (tspan != null) txt = tspan.Value?.Trim();
        }
        
        if (string.IsNullOrEmpty(txt)) return;

        float x = ParseDimension(elem.Attribute("x")?.Value, 0);
        float y = ParseDimension(elem.Attribute("y")?.Value, 0);
        float fontSize = ParseDimension(GetStyleOrAttribute(elem, "font-size"), 12);
        string fontFamily = GetStyleOrAttribute(elem, "font-family") ?? "Arial";
        string textAnchor = GetStyleOrAttribute(elem, "text-anchor")?.ToLower() ?? "start";

        var lText = new LaserText()
        {
            Name = elem.Attribute("id")?.Value ?? "Text",
            Text = txt,
            FontSize = fontSize,
            FontName = fontFamily,
            FontStyle = ParseFontStyle(elem)
        };

        if (textAnchor == "middle") lText.Anchor = TextAnchor.Middle;
        else if (textAnchor == "end") lText.Anchor = TextAnchor.End;
        else lText.Anchor = TextAnchor.Start;

        // Extract transform components (Rotation and Scale)
        // Extract rotation: atan2(m12, m11)
        float rotation = (float)(Math.Atan2(transform.Elements[1], transform.Elements[0]) * 180 / Math.PI);
        // Extract scale: sqrt(m11^2 + m12^2)
        float scale = (float)Math.Sqrt(transform.Elements[0] * transform.Elements[0] + transform.Elements[1] * transform.Elements[1]);

        lText.Rotation = rotation;
        lText.FontSize *= scale;

        var pts = new PointF[] { new PointF(x, y) };
        transform.TransformPoints(pts);
        lText.Position = pts[0];
        
        lText.UpdateTextSize();
        list.Add(lText);
    }
    
    private static void ParseTextPath(XElement textElem, XElement textPathElem, List<LaserObject> list, Matrix transform)
    {
        // 1. Resolve Path Data & Transform
        string pathData = textPathElem.Attribute("d")?.Value ?? textPathElem.Attribute("path")?.Value;
        using var refTransform = new Matrix();

        if (string.IsNullOrEmpty(pathData))
        {
            // Try href
            string href = textPathElem.Attribute("href")?.Value ?? 
                          textPathElem.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href")?.Value;
            
            if (!string.IsNullOrEmpty(href))
            {
                if (href!.StartsWith("#")) href = href.Substring(1);
                var pathElem = GetElementById(textElem.Document, href);
                if (pathElem != null)
                {
                    pathData = pathElem.Attribute("d")?.Value;
                    
                    // Apply transform from referenced element
                    var tStr = pathElem.Attribute("transform")?.Value;
                    if (!string.IsNullOrEmpty(tStr))
                    {
                        using var t = ParseTransform(tStr!);
                        refTransform.Multiply(t);
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(pathData)) return;

        // 2. Create the Backbone Path (LaserPath)
        // Referenced path is usually not visible unless explicitly listed elsewhere.
        // But for 'textPath', we might need to store the path if we want dynamic updates.
        // We create a hidden LaserPath for the text to follow.
        
        var backbonePath = new LaserPath();
        backbonePath.Name = (textElem.Attribute("id")?.Value ?? "TextPath") + "_Backbone";
        backbonePath.IsEnabled = false; // Hidden/Support path
        backbonePath.LayerId = Guid.Empty; // Or same layer?
        
        using (var gp = ParsePathData(pathData!))
        {
            if (!refTransform.IsIdentity)
            {
                gp.Transform(refTransform);
            }
            
            // Apply current transform (group transform) to the backbone?
            // SVG spec: "The 'text' element's user coordinate system... is applied to the text... 
            // The 'textPath' refers to a 'path'... The glyphs are rendered along the path..."
            // Usually the path definition is in its own coordinate space. 
            // If the path is referenced by href, it's usually defined in defs or elsewhere.
            // The text element's transform applies to the whole result.
            // BUT: We want the LaserPath to be in World Coordinates so that LaserText (which has its own position logic) can follow it.
            // If we put the LaserPath in the list, we usually apply 'transform' to it (as done in AddGraphicsPath).
            // So yes, we should apply 'transform' to the backbone so it sits incorrectly in the final world.
            
            gp.Transform(transform);
            
            // Flatten for our internal logic (points list)
            gp.Flatten(null, AppConfiguration.Instance.SvgCurveQuality);
            backbonePath.Points = new List<PointF>(gp.PathPoints);
            backbonePath.UpdateBounds();
        }
        
        List<PointF> backbonePoints = backbonePath.Points;
        
        list.Add(backbonePath); // Add as hidden object

        // 3. Create LaserText
        // Check xml:space
        string xmlSpace = textElem.Attribute(XNamespace.Xml + "space")?.Value ?? 
                          textPathElem.Attribute(XNamespace.Xml + "space")?.Value ?? 
                          "default";
        
        string rawTxt = textPathElem.Value ?? "";
        string txt;
        if (xmlSpace == "preserve")
        {
            txt = rawTxt; 
        }
        else
        {
            txt = System.Text.RegularExpressions.Regex.Replace(rawTxt, @"\s+", " ").Trim();
        }

        if (string.IsNullOrEmpty(txt)) return;

        float fontSize = ParseDimension(GetStyleOrAttribute(textElem, "font-size"), 12);
        string fontFamily = GetStyleOrAttribute(textElem, "font-family") ?? "Arial";
        
        FontFamily ffm;
        try { ffm = new FontFamily(fontFamily); } catch { ffm = FontFamily.GenericSansSerif; }
        
        var lText = new LaserText()
        {
            Name = textElem.Attribute("id")?.Value ?? "TextPathObj",
            Text = txt,
            FontSize = fontSize,
            FontName = fontFamily,
            FontStyle = ParseFontStyle(textElem),
            PathId = backbonePath.Id,
            IsEnabled = true
        };

        // Attributes
        string startOffsetStr = textPathElem.Attribute("startOffset")?.Value;
        if (!string.IsNullOrEmpty(startOffsetStr))
        {
            if (startOffsetStr!.EndsWith("%"))
            {
                 float pct = float.Parse(startOffsetStr.TrimEnd('%'), CultureInfo.InvariantCulture) / 100f;
                 // Calculate length
                 float totalLen = 0;
                 for(int i=0; i<backbonePath.Points.Count-1; i++) 
                    totalLen += (float)Math.Sqrt(Math.Pow(backbonePath.Points[i+1].X - backbonePath.Points[i].X, 2) + Math.Pow(backbonePath.Points[i+1].Y - backbonePath.Points[i].Y, 2));
                 lText.PathOffset = totalLen * pct;
            }
            else
            {
                 lText.PathOffset = ParseDimension(startOffsetStr, 0);
            }
        }
        
        // Side
        string side = textPathElem.Attribute("side")?.Value?.ToLower() ?? "left";
        if (side == "right")
        {
            lText.ReversePath = true;
        }
        
        // Method
        string method = textPathElem.Attribute("method")?.Value?.ToLower() ?? "align"; // Default is align per spec, stretch is distinct? 
        // SVG 1.1 doesn't strictly have 'method' on textPath (it was proposed/SVG 2).
        // Common Attributes: startOffset. 'method' is align | stretch.
        // Default is 'align'.
        if (method == "stretch") lText.WarpMethod = TextWarpMethod.Stretch;
        else lText.WarpMethod = TextWarpMethod.Align;

        // Position/Size?
        // LaserText attached to path derives position from path.
        // But we might want initial bounds.
        // To get accurate bounds, we need to simulate the warp.
        
        // Create temporary text path
        using var tempTextGp = new GraphicsPath();
        var sf = StringFormat.GenericTypographic;
        tempTextGp.AddString(txt, ffm, (int)lText.FontStyle, fontSize, new PointF(0, 0), sf); 
        
        float emHeight = ffm.GetEmHeight(lText.FontStyle);
        float cellAscent = ffm.GetCellAscent(lText.FontStyle);
        float baselineY = (fontSize * cellAscent) / emHeight;

        var mat = new Matrix();
        mat.Translate(0, -baselineY);
        tempTextGp.Transform(mat);
        
        RectangleF finalBounds = RectangleF.Empty;
        
        var effectiveBackbone = new List<PointF>(backbonePoints);
        if (lText.ReversePath)
        {
            effectiveBackbone.Reverse();
        }

        if (lText.WarpMethod == TextWarpMethod.Stretch)
        {
             // Debug
             // throw new Exception($"DEBUG: STRETCH METHOD USED. Rev={lText.ReversePath}");

             using var warpedGp = PathWarp.CreateWarpedPath(tempTextGp, effectiveBackbone, lText.PathOffset);
             // Apply element transform (if any distinct from path? No, we applied transform to backbone).
             // Backbone is in World space. Warped GP is in World Space.
             finalBounds = warpedGp.GetBounds();
        }
        else
        {
            // Simulate Align Bounds
            // Reuse logic from LaserText.Draw or simplified approximation?
            // We should try to be somewhat accurate.
            // Simplified: Iterate chars.
             float curX = 0;
             PathWarp.ComputeBackboneProperties(effectiveBackbone, out var lengths, out var normals);
             float totalPathLen = lengths.Last();
             
             float minX = float.MaxValue, minY = float.MaxValue;
             float maxX = float.MinValue, maxY = float.MinValue;
             bool hasPoints = false;

             // Accurate simulation matching LaserText.Draw
             using (var tmpBmp = new Bitmap(1, 1))
             using (var g = Graphics.FromImage(tmpBmp))
             using (var f = new Font(ffm, fontSize, lText.FontStyle, GraphicsUnit.World))
             {
                 foreach (char ch in txt)
                 {
                     if (char.IsControl(ch)) continue;
                     string s = ch.ToString();
                     using (var charPath = new GraphicsPath())
                     {
                         charPath.AddString(s, ffm, (int)lText.FontStyle, fontSize, new PointF(0, 0), sf);
                         var bounds = charPath.GetBounds();
                         
                         float advance = g.MeasureString(s, f, 1000, sf).Width;
                         if (advance <= 0) advance = fontSize * 0.3f;
                         
                         if (char.IsWhiteSpace(ch)) { curX += advance; continue; }
                         
                         float charMidX = curX + advance / 2f;
                         float targetDist = charMidX + lText.PathOffset;
                         if (totalPathLen > 0.001f) targetDist = ((targetDist % totalPathLen) + totalPathLen) % totalPathLen;

                         PathWarp.GetPointAndNormalAt(targetDist, effectiveBackbone, lengths, normals, out PointF origin, out PointF normal);
                         
                         using(var mChar = new Matrix())
                         {
                             // 1. Center character cell on path point (matching Draw logic)
                             mChar.Translate(-(advance / 2f), -baselineY); 
                             mChar.Scale(1, -1, MatrixOrder.Append);
                             
                             float rotAngle = (float)(Math.Atan2(-normal.X, normal.Y) * 180 / Math.PI);
                             mChar.Rotate(rotAngle, MatrixOrder.Append);
                             
                             // 2. Apply position + VerticalOffset
                             PointF finalPos = new PointF(origin.X + normal.X * lText.VerticalOffset, origin.Y + normal.Y * lText.VerticalOffset);
                             
                             mChar.Translate(finalPos.X, finalPos.Y, MatrixOrder.Append);
                             charPath.Transform(mChar);
                             
                             var cBounds = charPath.GetBounds();
                             if (cBounds.Left < minX) minX = cBounds.Left;
                             if (cBounds.Top < minY) minY = cBounds.Top;
                             if (cBounds.Right > maxX) maxX = cBounds.Right;
                             if (cBounds.Bottom > maxY) maxY = cBounds.Bottom;
                             hasPoints = true;
                         }
                         curX += advance;
                     }
                 }
             }
             
             if (hasPoints)
                finalBounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
             else
                finalBounds = new RectangleF(backbonePath.Position, new SizeF(0,0));
        }

        lText.Position = finalBounds.Location;
        lText.Size = finalBounds.Size;
        
        list.Add(lText);
    }
    
    private static FontStyle ParseFontStyle(XElement elem)
    {
        FontStyle style = FontStyle.Regular;
        string weight = GetStyleOrAttribute(elem, "font-weight")?.ToLower();
        string fstyle = GetStyleOrAttribute(elem, "font-style")?.ToLower();
        
        if (weight == "bold" || weight == "bolder" || weight == "700" || weight == "800" || weight == "900")
            style |= FontStyle.Bold;
        
        if (fstyle == "italic" || fstyle == "oblique")
            style |= FontStyle.Italic;
            
        return style;
    }

    private static void AddGraphicsPath(GraphicsPath gp, XElement elem, List<LaserObject> list, Matrix transform)
    {
        gp.Transform(transform);
        gp.Flatten(null, AppConfiguration.Instance.SvgCurveQuality);

        var iterator = new GraphicsPathIterator(gp);
        iterator.Rewind();
        int subPathCount = iterator.SubpathCount;
        
        for(int i=0; i<subPathCount; i++)
        {
            iterator.NextSubpath(out int startIndex, out int endIndex, out bool isClosed);
            
            if (endIndex < startIndex) continue;
            
            var count = endIndex - startIndex + 1;
            var points = new PointF[count];
            Array.Copy(gp.PathPoints, startIndex, points, 0, count);
            
            if (points.Length < 2) continue;
            
            var lp = new LaserPath();
            lp.Name = (elem.Attribute("id")?.Value ?? elem.Name.LocalName) + (subPathCount > 1 ? $"_{i}" : "");
            lp.Points = new List<PointF>(points);
            
            if (isClosed && points.Length > 2)
            {
                 if (points[0] != points[points.Length-1])
                    lp.Points.Add(points[0]);
            }
            
            float minX = points.Min(p => p.X);
            float minY = points.Min(p => p.Y);
            float maxX = points.Max(p => p.X);
            float maxY = points.Max(p => p.Y);
            
            lp.Position = new PointF(minX, minY);
            lp.Size = new SizeF(maxX - minX, maxY - minY);
            
            list.Add(lp);
        }
    }

    private static string? GetStyleOrAttribute(XElement elem, string name)
    {
        var attr = elem.Attribute(name)?.Value;
        if (!string.IsNullOrEmpty(attr)) return attr;

        var style = elem.Attribute("style")?.Value;
        if (!string.IsNullOrEmpty(style))
        {
            var parts = style.Split(';');
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim();
                }
            }
        }
        return null;
    }

    private static XElement? GetElementById(XDocument? doc, string id)
    {
        if (doc == null || string.IsNullOrEmpty(id)) return null;
        return doc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == id);
    }

    // --- Path Parsing Helpers ---

    private static GraphicsPath ParsePathData(string d)
    {
        var gp = new GraphicsPath();
        var tokens = TokenizePath(d);
        int i = 0;
        PointF currentPoint = new PointF(0, 0);
        PointF startPoint = new PointF(0, 0);
        PointF lastControl = new PointF(0, 0);
        char lastCmd = ' ';

        while (i < tokens.Count)
        {
            char cmd = tokens[i][0];
            if (char.IsLetter(cmd))
            {
                i++;
            }
            else
            {
                // Implicit repeat of last command (with exceptions)
                 if (lastCmd == 'M') cmd = 'L';
                 else if (lastCmd == 'm') cmd = 'l';
                 else cmd = lastCmd;
                 // Don't increment i, token is value
            }

            lastCmd = cmd;
            bool isRel = char.IsLower(cmd);

            switch (char.ToUpper(cmd))
            {
                case 'M':
                    {
                        var p = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        if (isRel) p = Add(currentPoint, p);
                        startPoint = p;
                        currentPoint = p;
                        // M doesn't draw, enables starting new figure implicitly? 
                        // GraphicsPath.StartFigure is implicit on AddLine/Curve if separated.
                        gp.StartFigure();
                    }
                    break;
                case 'L':
                    {
                        var p = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        if (isRel) p = Add(currentPoint, p);
                        gp.AddLine(currentPoint, p);
                        currentPoint = p;
                    }
                    break;
                case 'H':
                    {
                        float val = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        var p = new PointF(isRel ? currentPoint.X + val : val, currentPoint.Y);
                        gp.AddLine(currentPoint, p);
                        currentPoint = p;
                    }
                    break;
                case 'V':
                    {
                        float val = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        var p = new PointF(currentPoint.X, isRel ? currentPoint.Y + val : val);
                        gp.AddLine(currentPoint, p);
                        currentPoint = p;
                    }
                    break;
                case 'C':
                    {
                        var c1 = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        var c2 = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        var p = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        if (isRel) { c1 = Add(currentPoint, c1); c2 = Add(currentPoint, c2); p = Add(currentPoint, p); }
                        gp.AddBezier(currentPoint, c1, c2, p);
                        lastControl = c2; 
                        currentPoint = p;
                    }
                    break;
                case 'S':
                    {
                        // Reflect last control point around current point
                        var c1 = currentPoint;
                        if (lastCmd == 'C' || lastCmd == 'c' || lastCmd == 'S' || lastCmd == 's')
                        {
                            c1 = new PointF(2 * currentPoint.X - lastControl.X, 2 * currentPoint.Y - lastControl.Y);
                        }
                        
                        var c2 = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        var p = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        if (isRel) { c2 = Add(currentPoint, c2); p = Add(currentPoint, p); }
                        
                        gp.AddBezier(currentPoint, c1, c2, p);
                        lastControl = c2;
                        currentPoint = p;
                    }
                    break;
                 case 'Q': // Quadratic
                    {
                        var c1 = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        var p = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        if (isRel) { c1 = Add(currentPoint, c1); p = Add(currentPoint, p); }
                        
                        // Convert Quad to Cubic
                        // CP1 = P0 + 2/3 (C1 - P0)
                        // CP2 = P + 2/3 (C1 - P)
                        var cp1 = new PointF(currentPoint.X + 2.0f/3.0f * (c1.X - currentPoint.X), currentPoint.Y + 2.0f/3.0f * (c1.Y - currentPoint.Y));
                        var cp2 = new PointF(p.X + 2.0f/3.0f * (c1.X - p.X), p.Y + 2.0f/3.0f * (c1.Y - p.Y));
                        
                        gp.AddBezier(currentPoint, cp1, cp2, p);
                        lastControl = c1;
                        currentPoint = p;
                    }
                    break;
                  case 'T': // Smooth Quadratic
                    {
                        var c1 = currentPoint;
                        if (lastCmd == 'Q' || lastCmd == 'q' || lastCmd == 'T' || lastCmd == 't')
                        {
                             c1 = new PointF(2 * currentPoint.X - lastControl.X, 2 * currentPoint.Y - lastControl.Y);
                        }
                        var p = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        if (isRel) { p = Add(currentPoint, p); }

                        var cp1 = new PointF(currentPoint.X + 2.0f/3.0f * (c1.X - currentPoint.X), currentPoint.Y + 2.0f/3.0f * (c1.Y - currentPoint.Y));
                        var cp2 = new PointF(p.X + 2.0f/3.0f * (c1.X - p.X), p.Y + 2.0f/3.0f * (c1.Y - p.Y));
                        
                        gp.AddBezier(currentPoint, cp1, cp2, p);
                        lastControl = c1;
                        currentPoint = p;
                    }
                    break;
                  case 'A':
                    {
                        float rx = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        float ry = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        float angle = float.Parse(tokens[i++], CultureInfo.InvariantCulture);
                        bool largeArc = tokens[i++] == "1";
                        bool sweep = tokens[i++] == "1";
                        var p = new PointF(float.Parse(tokens[i++], CultureInfo.InvariantCulture), float.Parse(tokens[i++], CultureInfo.InvariantCulture));
                        if (isRel) p = Add(currentPoint, p);
                        
                        AddArcToPath(gp, currentPoint, p, rx, ry, angle, largeArc, sweep);
                        currentPoint = p;
                    }
                    break;
                case 'Z':
                    gp.CloseFigure();
                    currentPoint = startPoint;
                    break;
            }
        }
        return gp;
    }
    
    // ARC to Bezier conversion is complex, implementing simplified version or standard algorithm.
    private static void AddArcToPath(GraphicsPath gp, PointF start, PointF end, float rx, float ry, float angle, bool largeArc, bool sweep)
    {
        if (rx == 0 || ry == 0) { gp.AddLine(start, end); return; }
        
        rx = Math.Abs(rx); 
        ry = Math.Abs(ry);
        angle = angle * (float)(Math.PI / 180.0);
        
        float cosAngle = (float)Math.Cos(angle);
        float sinAngle = (float)Math.Sin(angle);

        // Step 1: Transform to local coordinates
        float dx2 = (start.X - end.X) / 2.0f;
        float dy2 = (start.Y - end.Y) / 2.0f;
        float x1 = cosAngle * dx2 + sinAngle * dy2;
        float y1 = -sinAngle * dx2 + cosAngle * dy2;

        // Ensure radii are large enough
        float radiiCheck = x1*x1/(rx*rx) + y1*y1/(ry*ry);
        if (radiiCheck > 1) {
            float scale = (float)Math.Sqrt(radiiCheck);
            rx *= scale;
            ry *= scale;
        }

        // Step 2: Calculate Center
        float sign = (largeArc == sweep) ? -1 : 1;
        float sq = ((rx*rx * ry*ry) - (rx*rx * y1*y1) - (ry*ry * x1*x1)) / ((rx*rx * y1*y1) + (ry*ry * x1*x1));
        sq = (sq < 0) ? 0 : sq;
        float coef = sign * (float)Math.Sqrt(sq);
        float cx1 = coef * ((rx * y1) / ry);
        float cy1 = coef * -((ry * x1) / rx);

        float cx = cosAngle * cx1 - sinAngle * cy1 + (start.X + end.X) / 2.0f;
        float cy = sinAngle * cx1 + cosAngle * cy1 + (start.Y + end.Y) / 2.0f;

        // Step 3: Calculate Angles
        float ux = (x1 - cx1) / rx;
        float uy = (y1 - cy1) / ry;
        float vx = (-x1 - cx1) / rx;
        float vy = (-y1 - cy1) / ry;

        float startAngle = VectorAngle(1, 0, ux, uy);
        float dAngle = VectorAngle(ux, uy, vx, vy);
        
        if (sweep && dAngle < 0) dAngle += (float)(Math.PI * 2);
        if (!sweep && dAngle > 0) dAngle -= (float)(Math.PI * 2);

        // Step 4: Add Segments (approximating with Beziers)
        int segments = (int)Math.Ceiling(Math.Abs(dAngle) / (Math.PI / 2.0));
        float delta = dAngle / segments;
        float t = (float)(8.0 / 3.0 * Math.Sin(delta / 4.0) * Math.Sin(delta / 4.0) / Math.Sin(delta / 2.0));
        
        float cosTheta = (float)Math.Cos(startAngle);
        float sinTheta = (float)Math.Sin(startAngle);
        
        for (int i = 0; i < segments; i++)
        {
            float theta2 = startAngle + delta;
            float cosTheta2 = (float)Math.Cos(theta2);
            float sinTheta2 = (float)Math.Sin(theta2);
            
            var p0 = new PointF(cx + cosAngle * rx * cosTheta - sinAngle * ry * sinTheta,
                                cy + sinAngle * rx * cosTheta + cosAngle * ry * sinTheta);
            
            var pe = new PointF(cx + cosAngle * rx * cosTheta2 - sinAngle * ry * sinTheta2,
                                cy + sinAngle * rx * cosTheta2 + cosAngle * ry * sinTheta2);

            var cp1 = new PointF(p0.X - t * (cosAngle * rx * sinTheta + sinAngle * ry * cosTheta),
                                 p0.Y - t * (sinAngle * rx * sinTheta - cosAngle * ry * cosTheta));
                                 
            var cp2 = new PointF(pe.X + t * (cosAngle * rx * sinTheta2 + sinAngle * ry * cosTheta2),
                                 pe.Y + t * (sinAngle * rx * sinTheta2 - cosAngle * ry * cosTheta2));
            
            gp.AddBezier(p0, cp1, cp2, pe);
            
            startAngle = theta2;
            cosTheta = cosTheta2;
            sinTheta = sinTheta2;
        }
    }

    private static float VectorAngle(float uX, float uY, float vX, float vY)
    {
        float dot = uX * vX + uY * vY;
        float det = uX * vY - uY * vX;
        return (float)Math.Atan2(det, dot);
    }
    
    private static List<string> TokenizePath(string d)
    {
        var tokens = new List<string>();
        // Regex to split by command letter or space/comma
        // Commands: M L H V C S Q T A Z (case insensitive)
        // Numbers: floats
        string pattern = @"([a-zA-Z])|([-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?)";
        
        foreach (Match m in Regex.Matches(d, pattern))
        {
            if (!string.IsNullOrEmpty(m.Value))
            {
                tokens.Add(m.Value);
            }
        }
        return tokens;
    }

    private static PointF Add(PointF a, PointF b) => new PointF(a.X + b.X, a.Y + b.Y);


    // --- Dimension Parsing ---

    private static float ParseDimension(string? s, float def)
    {
        if (string.IsNullOrEmpty(s)) return def;
        s = s.Trim();
        if (s.EndsWith("px")) return float.Parse(s.Substring(0, s.Length - 2), CultureInfo.InvariantCulture);
        if (s.EndsWith("mm")) return float.Parse(s.Substring(0, s.Length - 2), CultureInfo.InvariantCulture) * 3.7795f; // 96 dpi
        if (s.EndsWith("cm")) return float.Parse(s.Substring(0, s.Length - 2), CultureInfo.InvariantCulture) * 37.795f;
        if (s.EndsWith("in")) return float.Parse(s.Substring(0, s.Length - 2), CultureInfo.InvariantCulture) * 96f;
        if (s.EndsWith("pt")) return float.Parse(s.Substring(0, s.Length - 2), CultureInfo.InvariantCulture) * 1.3333f; // 96/72
        if (s.EndsWith("%")) return def; // Not supported properly without context, return default or 0? 
        
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float res)) return res;
        return def;
    }

    private static float[] ParseNumbers(string s)
    {
        if (string.IsNullOrEmpty(s)) return new float[0];
        
        var list = new List<float>();
         string pattern = @"([-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?)";
         foreach (Match m in Regex.Matches(s, pattern))
         {
             if (float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                 list.Add(val);
         }
         return list.ToArray();
    }
    
    // --- Transform Parsing ---
    
    private static Matrix ParseTransform(string t)
    {
        // format: translate(x,y) rotate(a) scale(sx,sy) ...
        var mat = new Matrix();
        if (string.IsNullOrWhiteSpace(t)) return mat;
        
        // Regex to get "func(args)"
        string funcPattern = @"(\w+)\s*\(([^)]+)\)";
        var matches = Regex.Matches(t, funcPattern);
        
        // Transforms are applied: if string is "T R S", effect is T * R * S * point.
        // Matrix multiplication in GDI+ (and accumulation):
        // Matrix order is Prepend by default or Append?
        // Svg: "transform lists are effectively processed right to left".
        // But if I have a matrix M, and I process "translate", I want M' = M * T.
        // Let's stick to standard append order logic.
        
        foreach (Match m in matches)
        {
            string name = m.Groups[1].Value.ToLower();
            string args = m.Groups[2].Value;
            var nums = ParseNumbers(args);
            
            switch (name)
            {
                case "matrix":
                    if (nums.Length == 6)
                        mat.Multiply(new Matrix(nums[0], nums[1], nums[2], nums[3], nums[4], nums[5]));
                    break;
                case "translate":
                    if (nums.Length == 1) mat.Translate(nums[0], 0);
                    else if (nums.Length >= 2) mat.Translate(nums[0], nums[1]);
                    break;
                case "scale":
                    if (nums.Length == 1) mat.Scale(nums[0], nums[0]);
                    else if (nums.Length >= 2) mat.Scale(nums[0], nums[1]);
                    break;
                case "rotate":
                    if (nums.Length == 1) mat.Rotate(nums[0]);
                    else if (nums.Length == 3)
                    {
                        mat.Translate(nums[1], nums[2]);
                        mat.Rotate(nums[0]);
                        mat.Translate(-nums[1], -nums[2]);
                    }
                    break;
                case "skewx":
                    if (nums.Length == 1) mat.Shear((float)Math.Tan(nums[0] * Math.PI / 180.0), 0);
                    break;
                case "skewy":
                    if (nums.Length == 1) mat.Shear(0, (float)Math.Tan(nums[0] * Math.PI / 180.0));
                    break;
            }
        }
        return mat;
    }
}
