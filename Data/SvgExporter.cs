/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Xml.Linq;

namespace grbl_burn_em.Data;

public class SvgExporter
{
    private const float Pad = 10f;
    private const float StrokeWidth = 0.5f;

    private readonly IReadOnlyList<LaserObject> _objects;
    private readonly XNamespace _svgNs = "http://www.w3.org/2000/svg";

    private float _worldLeft;
    private float _worldTop;
    private float _viewWidth;
    private float _viewHeight;

    public static void Export(IReadOnlyList<LaserObject> objects, string filePath)
    {
        var exporter = new SvgExporter(objects);
        var roots = exporter.GetRootObjects();
        if (roots.Count == 0) return;

        exporter.ComputeBounds(roots);
        var svg = exporter.BuildSvg(roots);

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), svg);
        doc.Save(writer);
    }

    private SvgExporter(IReadOnlyList<LaserObject> objects)
    {
        _objects = objects;
    }

    private float ToSvgX(float wx) => wx - _worldLeft;
    private float ToSvgY(float wy) => _worldTop - wy;

    private static string F(float v) => v.ToString("F3");

    private List<LaserObject> GetRootObjects()
    {
        var exportSet = new HashSet<LaserObject>(_objects);
        var roots = new List<LaserObject>();

        foreach (var obj in _objects)
        {
            if (obj.Parent != null && exportSet.Contains(obj.Parent))
                continue;
            roots.Add(obj);
        }

        return roots;
    }

    private void ComputeBounds(List<LaserObject> roots)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        GatherBounds(roots, ref minX, ref minY, ref maxX, ref maxY);

        if (minX == float.MaxValue)
        {
            minX = 0;
            minY = 0;
            maxX = 100;
            maxY = 100;
        }

        _worldLeft = minX - Pad;
        _worldTop = maxY + Pad;
        _viewWidth = maxX - minX + 2f * Pad;
        _viewHeight = maxY - minY + 2f * Pad;
    }

    private static void GatherBounds(IEnumerable<LaserObject> objects, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        foreach (var obj in objects)
        {
            var bounds = obj.GetBounds();
            if (bounds.Width > 0 || bounds.Height > 0)
            {
                minX = Math.Min(minX, bounds.Left);
                minY = Math.Min(minY, bounds.Top);
                maxX = Math.Max(maxX, bounds.Right);
                maxY = Math.Max(maxY, bounds.Bottom);
            }

            if (obj is LaserGroup group)
                GatherBounds(group.Children, ref minX, ref minY, ref maxX, ref maxY);
        }
    }

    private XElement BuildSvg(List<LaserObject> roots)
    {
        var defs = new XElement(_svgNs + "defs");
        var svg = new XElement(_svgNs + "svg",
            new XAttribute("xmlns", _svgNs.NamespaceName),
            new XAttribute("width", $"{_viewWidth:F3}mm"),
            new XAttribute("height", $"{_viewHeight:F3}mm"),
            new XAttribute("viewBox", $"0 0 {F(_viewWidth)} {F(_viewHeight)}"),
            defs);

        foreach (var root in roots)
            ExportObject(svg, defs, root);

        return svg;
    }

    private void ExportObject(XElement container, XElement defs, LaserObject obj)
    {
        switch (obj)
        {
            case LaserGroup group:
                ExportGroup(container, defs, group);
                break;
            case LaserPath path:
                ExportPath(container, path);
                break;
            case LaserRectangle rect:
                ExportRectangle(container, rect);
                break;
            case LaserCircle circle:
                ExportCircle(container, circle);
                break;
            case LaserBezier bezier:
                ExportBezier(container, bezier);
                break;
            case LaserText text:
                ExportText(container, text);
                break;
            case LaserImage image:
                ExportImage(container, defs, image);
                break;
        }
    }

    private void ExportGroup(XElement container, XElement defs, LaserGroup group)
    {
        var gElem = new XElement(_svgNs + "g",
            new XAttribute("id", group.Name ?? "Group"));

        var transform = GetTransform(group);
        if (transform != null)
            gElem.SetAttributeValue("transform", transform);

        foreach (var child in group.Children)
            ExportObject(gElem, defs, child);

        container.Add(gElem);
    }

    private void ExportPath(XElement container, LaserPath path)
    {
        if (path.Points.Count < 2) return;

        var sb = new StringBuilder();
        for (int i = 0; i < path.Points.Count; i++)
        {
            var pt = path.Points[i];
            if (i > 0) sb.Append(' ');
            sb.Append(F(ToSvgX(pt.X)));
            sb.Append(',');
            sb.Append(F(ToSvgY(pt.Y)));
        }

        var elem = new XElement(_svgNs + "polyline",
            new XAttribute("points", sb.ToString()),
            new XAttribute("fill", "none"),
            new XAttribute("stroke", GetColor(path)),
            new XAttribute("stroke-width", F(StrokeWidth)));

        var transform = GetTransform(path);
        if (transform != null)
            elem.SetAttributeValue("transform", transform);

        container.Add(elem);
    }

    private void ExportRectangle(XElement container, LaserRectangle rect)
    {
        float svgX = ToSvgX(rect.Position.X);
        float svgY = ToSvgY(rect.Position.Y + rect.Size.Height);

        var elem = new XElement(_svgNs + "rect",
            new XAttribute("x", F(svgX)),
            new XAttribute("y", F(svgY)),
            new XAttribute("width", F(rect.Size.Width)),
            new XAttribute("height", F(rect.Size.Height)),
            new XAttribute("fill", "none"),
            new XAttribute("stroke", GetColor(rect)),
            new XAttribute("stroke-width", F(StrokeWidth)));

        var transform = GetTransform(rect);
        if (transform != null)
            elem.SetAttributeValue("transform", transform);

        container.Add(elem);
    }

    private void ExportCircle(XElement container, LaserCircle circle)
    {
        float cx = circle.Position.X + circle.Size.Width / 2f;
        float cy = circle.Position.Y + circle.Size.Height / 2f;
        float rx = circle.Size.Width / 2f;
        float ry = circle.Size.Height / 2f;

        var elem = new XElement(_svgNs + "ellipse",
            new XAttribute("cx", F(ToSvgX(cx))),
            new XAttribute("cy", F(ToSvgY(cy))),
            new XAttribute("rx", F(rx)),
            new XAttribute("ry", F(ry)),
            new XAttribute("fill", "none"),
            new XAttribute("stroke", GetColor(circle)),
            new XAttribute("stroke-width", F(StrokeWidth)));

        var transform = GetTransform(circle);
        if (transform != null)
            elem.SetAttributeValue("transform", transform);

        container.Add(elem);
    }

    private void ExportBezier(XElement container, LaserBezier bezier)
    {
        var points = bezier.Points;
        if (points.Count < 4) return;

        int validCount = points.Count - (points.Count - 1) % 3;
        if (validCount < 4) return;

        var sb = new StringBuilder();
        sb.Append($"M {F(ToSvgX(points[0].X))},{F(ToSvgY(points[0].Y))} ");

        for (int i = 1; i < validCount; i += 3)
        {
            sb.Append($"C {F(ToSvgX(points[i].X))},{F(ToSvgY(points[i].Y))} ");
            sb.Append($"{F(ToSvgX(points[i + 1].X))},{F(ToSvgY(points[i + 1].Y))} ");
            sb.Append($"{F(ToSvgX(points[i + 2].X))},{F(ToSvgY(points[i + 2].Y))} ");
        }

        var elem = new XElement(_svgNs + "path",
            new XAttribute("d", sb.ToString().Trim()),
            new XAttribute("fill", "none"),
            new XAttribute("stroke", GetColor(bezier)),
            new XAttribute("stroke-width", F(StrokeWidth)));

        var transform = GetTransform(bezier);
        if (transform != null)
            elem.SetAttributeValue("transform", transform);

        container.Add(elem);
    }

    private void ExportText(XElement container, LaserText text)
    {
        if (text.PathId != Guid.Empty)
        {
            ExportWarpedText(container, text);
            return;
        }

        float svgX = ToSvgX(text.Position.X);
        float svgY = ToSvgY(text.Position.Y);

        string anchor = text.Anchor switch
        {
            TextAnchor.Middle => "middle",
            TextAnchor.End => "end",
            _ => "start"
        };

        var elem = new XElement(_svgNs + "text",
            new XAttribute("x", F(svgX)),
            new XAttribute("y", F(svgY)),
            new XAttribute("font-family", text.FontName),
            new XAttribute("font-size", F(text.FontSize)),
            new XAttribute("text-anchor", anchor),
            new XAttribute("fill", GetColor(text)),
            new XText(text.Text));

        var transform = GetTransform(text);
        if (transform != null)
            elem.SetAttributeValue("transform", transform);

        container.Add(elem);
    }

    private void ExportWarpedText(XElement container, LaserText text)
    {
        try
        {
            using var gp = text.GetPath();
            if (gp.PointCount == 0) return;

            var pathData = ExtractPathData(gp);
            if (string.IsNullOrEmpty(pathData)) return;

            var elem = new XElement(_svgNs + "path",
                new XAttribute("d", pathData),
                new XAttribute("fill", GetColor(text)));

            container.Add(elem);
        }
        catch
        {
            // Fall back: skip silently if path generation fails
        }
    }

    private string ExtractPathData(GraphicsPath gp)
    {
        var points = gp.PathPoints;
        var types = gp.PathTypes;
        if (points.Length == 0) return "";

        var sb = new StringBuilder();
        int i = 0;

        while (i < points.Length)
        {
            var pointType = types[i] & 0x07;
            float x = ToSvgX(points[i].X);
            float y = ToSvgY(points[i].Y);

            switch (pointType)
            {
                case 0: // Start
                    sb.Append($"M {F(x)},{F(y)} ");
                    break;
                case 1: // Line
                    sb.Append($"L {F(x)},{F(y)} ");
                    break;
                case 3: // Bezier
                    if (i + 2 < points.Length)
                    {
                        float x1 = ToSvgX(points[i].X), y1 = ToSvgY(points[i].Y);
                        float x2 = ToSvgX(points[i + 1].X), y2 = ToSvgY(points[i + 1].Y);
                        float x3 = ToSvgX(points[i + 2].X), y3 = ToSvgY(points[i + 2].Y);
                        sb.Append($"C {F(x1)},{F(y1)} {F(x2)},{F(y2)} {F(x3)},{F(y3)} ");
                        i += 2;
                    }
                    break;
            }

            if ((types[i] & 0x80) != 0)
                sb.Append("Z ");

            i++;
        }

        return sb.ToString().Trim();
    }

    private void ExportImage(XElement container, XElement defs, LaserImage image)
    {
        if (image.Image == null) return;

        string base64;
        try
        {
            using var ms = new MemoryStream();
            image.Image.Save(ms, ImageFormat.Png);
            base64 = Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return;
        }

        float svgX = ToSvgX(image.Position.X);
        float svgY = ToSvgY(image.Position.Y + image.Size.Height);

        var elem = new XElement(_svgNs + "image",
            new XAttribute("x", F(svgX)),
            new XAttribute("y", F(svgY)),
            new XAttribute("width", F(image.Size.Width)),
            new XAttribute("height", F(image.Size.Height)),
            new XAttribute("href", $"data:image/png;base64,{base64}"));

        var transform = GetTransform(image);
        if (transform != null)
            elem.SetAttributeValue("transform", transform);

        if (image.MaskId != Guid.Empty)
        {
            var maskObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == image.MaskId);
            if (maskObj is LaserCircle or LaserRectangle)
            {
                var clipId = $"clip-{image.MaskId:N}";
                var clipPath = new XElement(_svgNs + "clipPath",
                    new XAttribute("id", clipId));

                if (maskObj is LaserCircle c)
                {
                    float cx = c.Position.X + c.Size.Width / 2f;
                    float cy = c.Position.Y + c.Size.Height / 2f;
                    clipPath.Add(new XElement(_svgNs + "ellipse",
                        new XAttribute("cx", F(ToSvgX(cx))),
                        new XAttribute("cy", F(ToSvgY(cy))),
                        new XAttribute("rx", F(c.Size.Width / 2f)),
                        new XAttribute("ry", F(c.Size.Height / 2f))));
                }
                else if (maskObj is LaserRectangle r)
                {
                    clipPath.Add(new XElement(_svgNs + "rect",
                        new XAttribute("x", F(ToSvgX(r.Position.X))),
                        new XAttribute("y", F(ToSvgY(r.Position.Y + r.Size.Height))),
                        new XAttribute("width", F(r.Size.Width)),
                        new XAttribute("height", F(r.Size.Height))));
                }

                defs.Add(clipPath);
                elem.SetAttributeValue("clip-path", $"url(#{clipId})");
            }
        }

        container.Add(elem);
    }

    private static string GetColor(LaserObject obj)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId)
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        var color = layer?.Color ?? Color.Black;
        return ColorTranslator.ToHtml(color);
    }

    private string? GetTransform(LaserObject obj)
    {
        if (Math.Abs(obj.Rotation) < 0.001f) return null;

        float cx = obj.Position.X + obj.Size.Width / 2f;
        float cy = obj.Position.Y + obj.Size.Height / 2f;
        return $"rotate({F(obj.Rotation)} {F(ToSvgX(cx))} {F(ToSvgY(cy))})";
    }
}
