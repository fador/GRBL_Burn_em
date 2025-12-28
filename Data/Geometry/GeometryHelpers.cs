/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;

namespace grbl_burn_em.Data.Geometry
{
    public static class GeometryHelpers
    {
        public static bool BoundsIntersect(PolygonBounds a, PolygonBounds b)
        {
            return a.MinX < b.MaxX &&
                   a.MaxX > b.MinX &&
                   a.MinY < b.MaxY &&
                   a.MaxY > b.MinY;
        }

        public static bool IsPointInPolygon(PointD point, Polygon polygon)
        {
            bool inside = false;
            var points = polygon.Points;
            int n = points.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = points[i].X, yi = points[i].Y;
                double xj = points[j].X, yj = points[j].Y;

                bool intersect = ((yi > point.Y) != (yj > point.Y)) &&
                                 (point.X < (xj - xi) * (point.Y - yi) / (yj - yi) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private static bool CCW(PointD a, PointD b, PointD c)
        {
            return (c.Y - a.Y) * (b.X - a.X) > (b.Y - a.Y) * (c.X - a.X);
        }

        public static bool DoSegmentsIntersect(PointD p1, PointD p2, PointD p3, PointD p4)
        {
            return (CCW(p1, p3, p4) != CCW(p2, p3, p4)) &&
                   (CCW(p1, p2, p3) != CCW(p1, p2, p4));
        }

        public static bool DoPolygonsIntersect(Polygon polyA, Polygon polyB)
        {
            // 1. Fast Fail: AABB
            if (!BoundsIntersect(polyA.Bounds, polyB.Bounds)) return false;

            // Composite Check A
            if (polyA.Children.Count > 0)
            {
               foreach(var child in polyA.Children)
               {
                   if (DoPolygonsIntersect(child, polyB)) return true;
               }
               // Check A (Hull) vs B?
               // If A has children, does Points represent the Hull? usually.
               // If Points is just a Hull, we don't need to check it if we checked children?
               // BUT if Points represents valid geometry too, we should check it.
               if (polyA.Points.Count == 0) return false; // Composite Container only
            }

            // Composite Check B
            if (polyB.Children.Count > 0)
            {
               foreach(var child in polyB.Children)
               {
                   if (DoPolygonsIntersect(polyA, child)) return true;
               }
               if (polyB.Points.Count == 0) return false;
            }

            // 2. Vertex inside check (covers containment)
            foreach (var p in polyA.Points)
            {
                if (IsPointInPolygon(p, polyB)) return true;
            }
            foreach (var p in polyB.Points)
            {
                if (IsPointInPolygon(p, polyA)) return true;
            }

            // 3. Edge intersection check
            int lenA = polyA.Points.Count;
            int lenB = polyB.Points.Count;

            for (int i = 0; i < lenA; i++)
            {
                var a1 = polyA.Points[i];
                var a2 = polyA.Points[(i + 1) % lenA];

                for (int j = 0; j < lenB; j++)
                {
                    var b1 = polyB.Points[j];
                    var b2 = polyB.Points[(j + 1) % lenB];

                    if (DoSegmentsIntersect(a1, a2, b1, b2)) return true;
                }
            }

            return false;
        }

        public static bool IsWithinSheet(Polygon poly, double sheetW, double sheetH)
        {
             return poly.Bounds.MinX >= 0 &&
                    poly.Bounds.MinY >= 0 &&
                    poly.Bounds.MaxX <= sheetW &&
                    poly.Bounds.MaxY <= sheetH;
        }

        public static List<PointD> GetConvexHull(List<PointD> points)
        {
            if (points.Count <= 2) return new List<PointD>(points);

            // Sort by X then Y
            var sorted = new List<PointD>(points);
            sorted.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

            // Lower Hull
            var lower = new List<PointD>();
            foreach (var p in sorted)
            {
                while (lower.Count >= 2 && !CrossProductSign(lower[lower.Count - 2], lower[lower.Count - 1], p))
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(p);
            }

            // Upper Hull
            var upper = new List<PointD>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                var p = sorted[i];
                while (upper.Count >= 2 && !CrossProductSign(upper[upper.Count - 2], upper[upper.Count - 1], p))
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(p);
            }

            // Concatenate (remove last point of each as they are duplicates of the start of the other)
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);

            lower.AddRange(upper);
            return lower;
        }

        public static bool IsPolygonInside(Polygon inner, Polygon outer)
        {
            // 1. Bounds Check
            if (!BoundsIntersect(inner.Bounds, outer.Bounds)) return false; // Must intersect to be inside
            if (inner.Bounds.MinX < outer.Bounds.MinX || inner.Bounds.MaxX > outer.Bounds.MaxX ||
                inner.Bounds.MinY < outer.Bounds.MinY || inner.Bounds.MaxY > outer.Bounds.MaxY) return false;

            // 2. Check first point
            if (inner.Points.Count > 0)
            {
                if (!IsPointInPolygon(inner.Points[0], outer)) return false;
            }
            
            // 3. For robustness, maybe check all? 
            // For optimization requests ("very very slow"), checking one valid point is usually a sufficient heuristic 
            // for "Holes inside a Part". If one point is inside and bounds are inside, it's inside 
            // (assuming no self-intersections or crossing boundaries, which would be a collision anyway).
            return true;
        }

        private static bool CrossProductSign(PointD o, PointD a, PointD b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X) > 0;
        }
    }
}
