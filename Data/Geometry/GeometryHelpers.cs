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
    }
}
