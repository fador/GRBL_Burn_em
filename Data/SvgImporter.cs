using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Svg;
using Svg.Transforms;
using System.IO;

namespace laser_gui_test.Data;

public class SvgImporter
{
    public static List<LaserObject> Import(string filePath)
    {
        var laserObjects = new List<LaserObject>();
        var doc = SvgDocument.Open(filePath);
        
        // Calculate document height to flip Y axis
        // Prefer ViewBox height if available, else Document Height
        float docH = 0;
        if (doc.ViewBox.Height > 0)
        {
             docH = doc.ViewBox.Height;
        }
        else
        {
             docH = doc.Height.ToDeviceValue(null, UnitRenderingType.Vertical, doc);
        }

        // Create transform: Scale(1, -1) then Translate(0, docH)
        // Matrix(m11, m12, m21, m22, dx, dy)
        // x' = x
        // y' = -y + docH
        using var mat = new Matrix(1, 0, 0, -1, 0, docH);
        
        ImportElement(doc, laserObjects, mat);
        
        return laserObjects;
    }

    private static void ImportElement(SvgElement elem, List<LaserObject> list, Matrix parentTransform)
    {
        using var currentTransform = parentTransform.Clone();
        
        if (elem.Transforms != null)
        {
            // Get Svg Matrix and convert to System.Drawing.Matrix
            // The Svg library's Transforms.GetMatrix() returns a System.Drawing.Drawing2D.Matrix
            using var localMat = elem.Transforms.GetMatrix();
            currentTransform.Multiply(localMat);
        }

        if (elem is SvgVisualElement)
        {
            var visual = (SvgVisualElement)elem;
            if (visual is SvgImage image)
            {
                 // Basic Image Support
                 // Images in Svg are often base64 or linked. Svg library handles this into .Image property.
                 try
                 {
                     Bitmap? bitmap = null;
                     if (!string.IsNullOrEmpty(image.Href))
                     {
                         if (image.Href.StartsWith("data:image"))
                         {
                             var idx = image.Href.IndexOf(",");
                             if (idx > 0)
                             {
                                 var base64 = image.Href.Substring(idx + 1);
                                 var bytes = Convert.FromBase64String(base64);
                                 using var ms = new MemoryStream(bytes);
                                 bitmap = new Bitmap(ms);
                             }
                         }
                         else
                         {
                             var uri = new Uri(image.Href, UriKind.RelativeOrAbsolute);
                             string fullPath = image.Href;
                             if (!uri.IsAbsoluteUri)
                             {
                                 // SvgDocument has BaseUri usually, but we passed filePath to Import.
                                 // SvgDocument.Open sets BaseUri.
                                 if (elem.OwnerDocument != null && elem.OwnerDocument.BaseUri != null)
                                 {
                                     var baseUri = elem.OwnerDocument.BaseUri;
                                     fullPath = new Uri(baseUri, image.Href).LocalPath;
                                 }
                             }
                             
                             if (File.Exists(fullPath))
                             {
                                 bitmap = new Bitmap(fullPath);
                             }
                         }
                     }

                     if (bitmap != null)
                     {
                         // Calculate position (approximation, ignoring rotation for image bitmap itself for now)
                         // But we can find the top-left point.
                         
                         float x = image.X.ToDeviceValue(null, UnitRenderingType.Horizontal, image);
                         float y = image.Y.ToDeviceValue(null, UnitRenderingType.Vertical, image);
                         float w = image.Width.ToDeviceValue(null, UnitRenderingType.Horizontal, image);
                         float h = image.Height.ToDeviceValue(null, UnitRenderingType.Vertical, image);
                         
                         var pts = new PointF[] { new PointF(x, y) };
                         currentTransform.TransformPoints(pts);
                         
                         // We do not apply full affine transform to the bitmap content in LaserImage yet
                         // So we just place it at the transformed origin.
                         
                         var lImg = new LaserImage();
                         lImg.Name = image.ID ?? "Image";
                         lImg.Image = new Bitmap(bitmap);
                         
                         // Extract scale from matrix
                         float scaleX = (float)Math.Sqrt(currentTransform.Elements[0] * currentTransform.Elements[0] + currentTransform.Elements[1] * currentTransform.Elements[1]);
                         float scaleY = (float)Math.Sqrt(currentTransform.Elements[2] * currentTransform.Elements[2] + currentTransform.Elements[3] * currentTransform.Elements[3]); 
                         
                         float finalW = w * scaleX;
                         float finalH = h * scaleX;
                         
                         lImg.Size = new SizeF(finalW, finalH);
                         // Shift Y down by Height to map Top-Left to Bottom-Left
                         lImg.Position = new PointF(pts[0].X, pts[0].Y - finalH);

                         list.Add(lImg);
                     }
                 }
                 catch { /* Ignore image load errors */ }
            }
            else 
            {
                // Try to get GraphicsPath
                GraphicsPath? path = null;
                
                // Allow specific types to help us
                if (visual is SvgPath svgPath)
                {
                    path = (GraphicsPath)svgPath.Path(null).Clone();
                }
                else if (visual is SvgRectangle rect)
                {
                    path = new GraphicsPath();
                    float x = rect.X.ToDeviceValue(null, UnitRenderingType.Horizontal, rect);
                    float y = rect.Y.ToDeviceValue(null, UnitRenderingType.Vertical, rect);
                    float w = rect.Width.ToDeviceValue(null, UnitRenderingType.Horizontal, rect);
                    float h = rect.Height.ToDeviceValue(null, UnitRenderingType.Vertical, rect);
                    // Check corner radius
                    float rx = rect.CornerRadiusX.ToDeviceValue(null, UnitRenderingType.Horizontal, rect);
                    float ry = rect.CornerRadiusY.ToDeviceValue(null, UnitRenderingType.Vertical, rect);
                    
                    if (rx > 0 || ry > 0)
                    {
                        // Rounded rect simplified: just rect for now or specialized logic
                        // GraphicsPath doesn't have easy RoundRect, need to manual add arcs.
                        // MVP: Just add Rect
                         path.AddRectangle(new RectangleF(x, y, w, h));
                    }
                    else
                    {
                        path.AddRectangle(new RectangleF(x, y, w, h));
                    }
                }
                else if (visual is SvgCircle circle)
                {
                    path = new GraphicsPath();
                    float cx = circle.CenterX.ToDeviceValue(null, UnitRenderingType.Horizontal, circle);
                    float cy = circle.CenterY.ToDeviceValue(null, UnitRenderingType.Vertical, circle);
                    float r = circle.Radius.ToDeviceValue(null, UnitRenderingType.Other, circle);
                    path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                }
                else if (visual is SvgEllipse ellipse)
                {
                    path = new GraphicsPath();
                    float cx = ellipse.CenterX.ToDeviceValue(null, UnitRenderingType.Horizontal, ellipse);
                    float cy = ellipse.CenterY.ToDeviceValue(null, UnitRenderingType.Vertical, ellipse);
                    float rx = ellipse.RadiusX.ToDeviceValue(null, UnitRenderingType.Horizontal, ellipse);
                    float ry = ellipse.RadiusY.ToDeviceValue(null, UnitRenderingType.Vertical, ellipse);
                    path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                }
                else if (visual is SvgLine line)
                {
                    path = new GraphicsPath();
                    path.AddLine(
                        line.StartX.ToDeviceValue(null, UnitRenderingType.Horizontal, line),
                        line.StartY.ToDeviceValue(null, UnitRenderingType.Vertical, line),
                        line.EndX.ToDeviceValue(null, UnitRenderingType.Horizontal, line),
                        line.EndY.ToDeviceValue(null, UnitRenderingType.Vertical, line)
                    );
                }
                else if (visual is SvgPolygon polygon)
                {
                    path = new GraphicsPath();
                    if (polygon.Points.Count > 1)
                    {
                        var pts = new PointF[polygon.Points.Count / 2];
                        for(int i=0; i<pts.Length; i++)
                        {
                            pts[i] = new PointF(polygon.Points[i*2].ToDeviceValue(null, UnitRenderingType.Horizontal, polygon),
                                                polygon.Points[i*2+1].ToDeviceValue(null, UnitRenderingType.Vertical, polygon));
                        }
                        path.AddPolygon(pts);
                    }
                }
                else if (visual is SvgPolyline polyline)
                {
                    path = new GraphicsPath();
                    if (polyline.Points.Count > 1)
                    {
                        var pts = new PointF[polyline.Points.Count / 2];
                        for(int i=0; i<pts.Length; i++)
                        {
                            pts[i] = new PointF(polyline.Points[i*2].ToDeviceValue(null, UnitRenderingType.Horizontal, polyline),
                                                polyline.Points[i*2+1].ToDeviceValue(null, UnitRenderingType.Vertical, polyline));
                        }
                        path.AddLines(pts);
                    }
                }

                else if (visual is SvgText svgText)
                {
                    // Basic text support
                    var txt = new LaserText
                    {
                        Text = svgText.Text.Trim(),
                        Name = svgText.ID ?? "Text",
                        // Map Svg info. 
                        // SvgText properties: X, Y, FontSize, FontFamily
                    };
                    
                    float x = (svgText.X.Count > 0) ? svgText.X[0].ToDeviceValue(null, UnitRenderingType.Horizontal, svgText) : 0f;
                    float y = (svgText.Y.Count > 0) ? svgText.Y[0].ToDeviceValue(null, UnitRenderingType.Vertical, svgText) : 0f;
                    
                    var pts = new PointF[] { new PointF(x, y) };
                    currentTransform.TransformPoints(pts);
                    
                    txt.Position = pts[0];
                    
                    if (svgText.FontSize != SvgUnit.None)
                        txt.FontSize = svgText.FontSize.ToDeviceValue(null, UnitRenderingType.Other, svgText);
                        
                    if(string.IsNullOrEmpty(txt.Text) && svgText.Children.Count > 0)
                    {
                        // Handle tspan or nested content simple concatenation
                        txt.Text = svgText.Content; // Svg library often puts content here
                    }
                    
                    list.Add(txt);
                }

                if (path != null)
                {
                    // Apply Transform
                    path.Transform(currentTransform);
                    // Flatten to line segments
                    path.Flatten(null, 0.1f);
                    
                    // Create LaserPaths
                    var iterator = new GraphicsPathIterator(path);
                    iterator.Rewind();
                    int subPathCount = iterator.SubpathCount;
                    
                    for(int i=0; i<subPathCount; i++)
                    {
                        iterator.NextSubpath(out int startIndex, out int endIndex, out bool isClosed);
                        
                        // Limit to bounds
                        if (endIndex < startIndex) continue;
                        
                        var count = endIndex - startIndex + 1;
                        var points = new PointF[count];
                        Array.Copy(path.PathPoints, startIndex, points, 0, count);
                        
                        if (points.Length < 2) continue;
                        
                        var lp = new LaserPath();
                        lp.Name = (elem.ID ?? visual.GetType().Name) + (subPathCount > 1 ? $"_{i}" : "");
                        lp.Points = new List<PointF>(points);
                        if (isClosed && points.Length > 2)
                        {
                             // Ensure start and end meet if not already
                             if (points[0] != points[points.Length-1])
                                lp.Points.Add(points[0]);
                        }
                        
                        // Calculate Bounds for Position/Size (used by LaserObject)
                        // This is optional as LaserPath logic might ignore Position, but good for bounding box
                        float minX = points.Min(p => p.X);
                        float minY = points.Min(p => p.Y);
                        float maxX = points.Max(p => p.X);
                        float maxY = points.Max(p => p.Y);
                        
                        lp.Position = new PointF(minX, minY);
                        lp.Size = new SizeF(maxX - minX, maxY - minY);
                        
                        list.Add(lp);
                    }
                }
            }
        }
        
        // Recurse
        foreach(var child in elem.Children)
        {
            ImportElement(child, list, currentTransform);
        }
    }
}
