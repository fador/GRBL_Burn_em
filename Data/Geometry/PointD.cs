/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;

namespace grbl_burn_em.Data.Geometry
{
    public struct PointD
    {
        public double X;
        public double Y;

        public PointD(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static PointD operator +(PointD a, PointD b) => new PointD(a.X + b.X, a.Y + b.Y);
        public static PointD operator -(PointD a, PointD b) => new PointD(a.X - b.X, a.Y - b.Y);
        public static PointD operator *(PointD a, double d) => new PointD(a.X * d, a.Y * d);
        public static PointD operator /(PointD a, double d) => new PointD(a.X / d, a.Y / d);
        
        public double DistanceTo(PointD other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString() => $"{{X={X}, Y={Y}}}";
    }
}
