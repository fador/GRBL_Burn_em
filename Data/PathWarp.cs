using System.Drawing.Drawing2D;
using System.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace laser_gui_test.Data
{
    public static class PathWarp
    {
        public static List<PointF> FlattenPath(LaserObject pathObject)
        {
            if (pathObject is LaserPath p)
            {
                // Just use points? LaserPath usually has segments.
                // If it has few points, we might want to interpolate?
                // For now, treat segments as lines.
                return p.Points;
            }
            else if (pathObject is LaserBezier b)
            {
                using var gp = new GraphicsPath();
                if (b.Points.Count >= 4)
                {
                    int n = b.Points.Count;
                    int valid = n - (n - 1) % 3;
                    gp.AddBeziers(b.Points.Take(valid).ToArray());
                    gp.Flatten(null, 0.05f); // 0.05mm precision for warping backbone
                    return gp.PathPoints.ToList();
                }
            }
            else if (pathObject is LaserCircle c)
            {
                 using var gp = new GraphicsPath();
                 gp.AddEllipse(c.Position.X, c.Position.Y, c.Size.Width, c.Size.Height);
                 gp.Flatten(null, 0.05f);
                 var pts = gp.PathPoints.ToList();
                 if (pts.Count > 0) pts.Add(pts[0]); // Close loop
                 return pts;
            }
            return new List<PointF>();
        }

        // Warps 'textPath' along 'backbone' points.
        // Returns a NEW GraphicsPath.
        public static GraphicsPath CreateWarpedPath(GraphicsPath textPath, List<PointF> backbone, float offsetDist)
        {
            if (backbone.Count < 2) return (GraphicsPath)textPath.Clone();

            // 1. Flatten the text path and subdivide long segments
            // This ensures letters can bend along the curve.
            var flatText = (GraphicsPath)textPath.Clone();
            flatText.Flatten(null, 0.05f); // 0.05 unit tolerance (mm) for curves
            
            // Subdivide long lines
            var subdivided = SubdividePath(flatText, 1.0f); // 1mm max segment length
            flatText.Dispose();
            flatText = subdivided;

            // 2. Pre-calculate backbone properties
            float[] lengths = new float[backbone.Count];
            PointF[] normals = new PointF[backbone.Count];
            PointF[] segmentNormals = new PointF[backbone.Count - 1];
            
            lengths[0] = 0;
            for (int i = 0; i < backbone.Count - 1; i++)
            {
                float dx = backbone[i+1].X - backbone[i].X;
                float dy = backbone[i+1].Y - backbone[i].Y;
                float segLen = (float)Math.Sqrt(dx*dx + dy*dy);
                lengths[i+1] = lengths[i] + segLen;

                if (segLen > 0.0001f)
                {
                    segmentNormals[i] = new PointF(-dy / segLen, dx / segLen);
                }
                else
                {
                    segmentNormals[i] = i > 0 ? segmentNormals[i-1] : new PointF(0, 1);
                }
            }

            // Vertex Normals (average of adjacent segments)
            for (int i = 0; i < backbone.Count; i++)
            {
                if (i == 0) normals[i] = segmentNormals[0];
                else if (i == backbone.Count - 1) normals[i] = segmentNormals[i-1];
                else
                {
                    float nx = segmentNormals[i-1].X + segmentNormals[i].X;
                    float ny = segmentNormals[i-1].Y + segmentNormals[i].Y;
                    float nLen = (float)Math.Sqrt(nx*nx + ny*ny);
                    if (nLen > 0.0001f)
                    {
                        normals[i] = new PointF(nx / nLen, ny / nLen);
                    }
                    else
                    {
                        normals[i] = segmentNormals[i];
                    }
                }
            }

            // 3. Transform Points
            PointF[] points = flatText.PathPoints;
            byte[] types = flatText.PathTypes;
            float totalLen = lengths[lengths.Length - 1];

            for (int i = 0; i < points.Length; i++)
            {
                float localX = points[i].X;
                float localY = points[i].Y; 
                float targetDist = localX + offsetDist;

                // Path Looping
                if (totalLen > 0.001f)
                {
                    // Handle negative modulo correctly for reversed paths or offsets
                    targetDist = ((targetDist % totalLen) + totalLen) % totalLen;
                }

                // Find segment
                int idx = Array.BinarySearch(lengths, targetDist);
                if (idx < 0) idx = ~idx;

                int i0 = idx - 1;
                int i1 = idx;
                
                if (i0 < 0) { i0 = 0; i1 = 1; }
                if (i1 >= backbone.Count) { i0 = backbone.Count - 2; i1 = backbone.Count - 1; }

                float segLen = lengths[i1] - lengths[i0];
                float t = (segLen > 0.0001f) ? (targetDist - lengths[i0]) / segLen : 0;
                
                // Position interpolation (linear)
                float baseX = backbone[i0].X + (backbone[i1].X - backbone[i0].X) * t;
                float baseY = backbone[i0].Y + (backbone[i1].Y - backbone[i0].Y) * t;

                // Normal interpolation (linear between vertex normals)
                float nx = normals[i0].X + (normals[i1].X - normals[i0].X) * t;
                float ny = normals[i0].Y + (normals[i1].Y - normals[i0].Y) * t;
                
                // Re-normalize interpolated normal
                float nLen = (float)Math.Sqrt(nx*nx + ny*ny);
                if (nLen > 0.0001f) { nx /= nLen; ny /= nLen; }

                // Map Point
                points[i] = new PointF(baseX + nx * localY, baseY + ny * localY);
            }
            
            return new GraphicsPath(points, types);
        }

        public static float GetClosestOffset(LaserObject pathObj, PointF target)
        {
            var backbone = FlattenPath(pathObj);
            if (backbone.Count < 2) return 0;

            float bestDist = 0;
            float minSqDist = float.MaxValue;
            float currentPathLen = 0;

            for (int i = 0; i < backbone.Count - 1; i++)
            {
                var p0 = backbone[i];
                var p1 = backbone[i+1];
                float segLen = (float)Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
                
                // Project point to line segment
                float t = 0;
                float l2 = segLen * segLen;
                if (l2 > 0.0001f)
                {
                    t = ((target.X - p0.X) * (p1.X - p0.X) + (target.Y - p0.Y) * (p1.Y - p0.Y)) / l2;
                }
                
                t = Math.Max(0, Math.Min(1, t));
                
                float px = p0.X + t * (p1.X - p0.X);
                float py = p0.Y + t * (p1.Y - p0.Y);
                
                float distSq = (float)(Math.Pow(px - target.X, 2) + Math.Pow(py - target.Y, 2));
                
                if (distSq < minSqDist)
                {
                    minSqDist = distSq;
                    bestDist = currentPathLen + t * segLen;
                }
                
                currentPathLen += segLen;
            }
            
            return bestDist;
        }

        public static GraphicsPath SubdividePath(GraphicsPath path, float maxLen)
        {
            var points = path.PathPoints;
            var types = path.PathTypes;
            var newPoints = new List<PointF>();
            var newTypes = new List<byte>();

            for (int i = 0; i < points.Length; i++)
            {
                newPoints.Add(points[i]);
                newTypes.Add(types[i]);

                // If this is the start of a segment and not the last point
                // PathTypes: 0=Start, 1=Line, 3=Bezier (but we flattened to lines)
                if (i < points.Length - 1 && (types[i+1] & 0x07) != 0) // Next is not a NewFigure/MoveTo
                {
                    var p0 = points[i];
                    var p1 = points[i + 1];
                    float dx = p1.X - p0.X;
                    float dy = p1.Y - p0.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (dist > maxLen)
                    {
                        int divisions = (int)Math.Ceiling(dist / maxLen);
                        for (int j = 1; j < divisions; j++)
                        {
                            float t = (float)j / divisions;
                            newPoints.Add(new PointF(p0.X + dx * t, p0.Y + dy * t));
                            newTypes.Add(1); // LineTo
                        }
                    }
                }
            }

            return new GraphicsPath(newPoints.ToArray(), newTypes.ToArray());
        }
    }
}
