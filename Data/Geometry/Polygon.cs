/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace grbl_burn_em.Data.Geometry
{
    public class PolygonBounds
    {
        public double MinX, MinY, MaxX, MaxY;
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public PolygonBounds(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }
    }

    public class Polygon
    {
        public List<PointD> Points { get; private set; }
        public PolygonBounds Bounds { get; private set; } = default!;
        public double Area { get; private set; }
        public PointD Centroid { get; private set; }
        public double Radius { get; private set; }
        
        // Optional ID/Tag to link back to original object
        public object? Tag { get; set; }

        public Polygon(List<PointD> points)
        {
            Points = points;
            CalculateProperties();
        }

        private void CalculateProperties()
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var p in Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            Bounds = new PolygonBounds(minX, minY, maxX, maxY);

            CalculateAreaAndCentroid();
            CalculateBoundingRadius();
        }

        private void CalculateAreaAndCentroid()
        {
            double area = 0;
            double cx = 0;
            double cy = 0;
            int n = Points.Count;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double cross = Points[i].X * Points[j].Y - Points[j].X * Points[i].Y;
                area += cross;
                cx += (Points[i].X + Points[j].X) * cross;
                cy += (Points[i].Y + Points[j].Y) * cross;
            }

            Area = Math.Abs(area / 2.0);
            
            if (area == 0)
            {
                Centroid = new PointD(Bounds.MinX, Bounds.MinY); // Fallback
            }
            else
            {
                cx /= (6 * (area / 2.0)); // Use signed area for centroid calc
                cy /= (6 * (area / 2.0));
                Centroid = new PointD(cx, cy);
            }
        }

        private void CalculateBoundingRadius()
        {
            double maxDistSq = 0;
            foreach (var p in Points)
            {
                double dx = p.X - Centroid.X;
                double dy = p.Y - Centroid.Y;
                double distSq = dx * dx + dy * dy;
                if (distSq > maxDistSq) maxDistSq = distSq;
            }
            Radius = Math.Sqrt(maxDistSq);
        }

        public Polygon Translate(double dx, double dy)
        {
            var newPoints = new List<PointD>(Points.Count);
            foreach (var p in Points)
            {
                newPoints.Add(new PointD(p.X + dx, p.Y + dy));
            }
            var poly = new Polygon(newPoints);
            poly.Tag = this.Tag;
            return poly;
        }

        public Polygon Rotate(double angleDeg, PointD? origin = null)
        {
            PointD org = origin ?? Centroid;
            double rad = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            var newPoints = new List<PointD>(Points.Count);
            foreach (var p in Points)
            {
                double dx = p.X - org.X;
                double dy = p.Y - org.Y;
                newPoints.Add(new PointD(
                    org.X + (dx * cos - dy * sin),
                    org.Y + (dx * sin + dy * cos)
                ));
            }
            var poly = new Polygon(newPoints);
            poly.Tag = this.Tag;
            return poly;
        }
    }
}
