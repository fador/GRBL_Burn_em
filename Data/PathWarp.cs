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
        // textPath is assumed to be at a certain Y position relative to its baseline/origin.
        // Usually, Text is at Y=0 (Baseline) or similar.
        // We treat X as distance along backbone, Y as normal offset.
        // Offset adds to the initial X.
        public static void Warp(GraphicsPath textPath, List<PointF> backbone, float offsetDist)
        {
            if (backbone.Count < 2) return;

            // 1. Calculate Arc Lengths of backbone
            float[] lengths = new float[backbone.Count];
            lengths[0] = 0;
            for (int i = 1; i < backbone.Count; i++)
            {
                float dx = backbone[i].X - backbone[i-1].X;
                float dy = backbone[i].Y - backbone[i-1].Y;
                lengths[i] = lengths[i-1] + (float)Math.Sqrt(dx*dx + dy*dy);
            }
            float totalLen = lengths[lengths.Length - 1];

            // 2. Transform Points
            PointF[] points = textPath.PathPoints;
            byte[] types = textPath.PathTypes;

            for (int i = 0; i < points.Length; i++)
            {
                float localX = points[i].X;
                float localY = points[i].Y; 
                // Note: localY is up/down offset from baseline.
                // In our text generation logic (GrblGenerator/LaserText.Draw), we place text.
                // Usually we generate text at 0,0.
                
                float targetDist = localX + offsetDist;
                
                // Wrap? Or Clamp? Or Hide?
                // Let's Clamp/Extend last segment?
                
                // Find segment
                int idx = Array.BinarySearch(lengths, targetDist);
                if (idx < 0) idx = ~idx; // Bitwise complement is insertion point
                
                // idx is the index of the first element LARGER than targetDist
                // So the segment is idx-1 to idx.
                
                int p0_idx = idx - 1;
                int p1_idx = idx;
                
                if (p0_idx < 0) 
                {
                    p0_idx = 0; p1_idx = 1;
                }
                if (p1_idx >= backbone.Count)
                {
                    p0_idx = backbone.Count - 2;
                    p1_idx = backbone.Count - 1;
                }

                var p0 = backbone[p0_idx];
                var p1 = backbone[p1_idx];
                
                float segLen = lengths[p1_idx] - lengths[p0_idx];
                float distOnSeg = targetDist - lengths[p0_idx];
                float t = (segLen > 0.0001f) ? distOnSeg / segLen : 0;
                
                // Interpolated Position
                float tangentX = p1.X - p0.X;
                float tangentY = p1.Y - p0.Y;
                
                // Normalize tangent
                float len = (float)Math.Sqrt(tangentX*tangentX + tangentY*tangentY);
                float nx = 0, ny = 1;
                if (len > 0.0001f)
                {
                    tangentX /= len;
                    tangentY /= len;
                    
                    // Normal (-y, x) for Left-Hand? (Y Up)
                    // If tangent is (1,0) [Right], Normal should be (0,1) [Up].
                    // So (-y, x)? (-0, 1) -> (0, 1). Correct.
                    nx = -tangentY;
                    ny = tangentX;
                }
                
                // If localY is 10 (Up), we add 10 * Normal.
                
                // Base Position
                float baseX = p0.X + tangentX * distOnSeg;
                float baseY = p0.Y + tangentY * distOnSeg;
                
                // Final Position
                points[i] = new PointF(baseX + nx * localY, baseY + ny * localY);
            }
            
            // Reconstruct path with warped points?
            // GraphicsPath property PathPoints is a clone. Setting array values doesn't affect Path.
            // We cannot set PathPoints directly.
            // We have to rebuild the path.
            // Or use GraphicsPath(points, types).
            
            // Rebuild
            // We can't modify 'textPath' easily in place without reflection or rebuild.
            // But we can create a NEW path and replace content? 
            // The method signature returns void... 
            // Actually, we can't replace 'textPath'. The caller needs the result.
            // Let's assume we modify the 'points' array and creating a new path is the caller's job?
            // No, better to accept 'ref GraphicsPath' or return new one.
            // But we passed 'GraphicsPath textPath'.
            
            // Hack: Create new path, swap internals? No.
            // Let's verify usage.
        }
        
        public static GraphicsPath CreateWarpedPath(GraphicsPath textPath, List<PointF> backbone, float offsetDist)
        {
            if (backbone.Count < 2) return (GraphicsPath)textPath.Clone();
            
            // ... Logic ... 
            // Duplicating logic above
             // 1. Calculate Arc Lengths of backbone
            float[] lengths = new float[backbone.Count];
            lengths[0] = 0;
            for (int i = 1; i < backbone.Count; i++)
            {
                float dx = backbone[i].X - backbone[i-1].X;
                float dy = backbone[i].Y - backbone[i-1].Y;
                lengths[i] = lengths[i-1] + (float)Math.Sqrt(dx*dx + dy*dy);
            }
            float totalLen = lengths[lengths.Length - 1];

            PointF[] points = textPath.PathPoints;
            byte[] types = textPath.PathTypes; // Types match 1:1

            for (int i = 0; i < points.Length; i++)
            {
                float localX = points[i].X;
                float localY = points[i].Y; 
                float targetDist = localX + offsetDist;

                int idx = Array.BinarySearch(lengths, targetDist);
                if (idx < 0) idx = ~idx;

                int p0_idx = idx - 1;
                int p1_idx = idx;
                
                if (p0_idx < 0) { p0_idx = 0; p1_idx = 1; }
                if (p1_idx >= backbone.Count) { p0_idx = backbone.Count - 2; p1_idx = backbone.Count - 1; }

                var p0 = backbone[p0_idx];
                var p1 = backbone[p1_idx];
                
                float segLen = lengths[p1_idx] - lengths[p0_idx];
                float distOnSeg = targetDist - lengths[p0_idx];
                // Extrapolate if outside
                
                float tangentX = p1.X - p0.X;
                float tangentY = p1.Y - p0.Y;
                float len = (float)Math.Sqrt(tangentX*tangentX + tangentY*tangentY);
                
                float nx = 0, ny = 1;
                if (len > 0.0001f)
                {
                    tangentX /= len;
                    tangentY /= len;
                    nx = -tangentY;
                    ny = tangentX;
                }

                float baseX = p0.X + tangentX * distOnSeg;
                float baseY = p0.Y + tangentY * distOnSeg;
                
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
    }
}
