/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using Xunit;
using grbl_burn_em.Data;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace grbl_burn_em.Tests;

public class LaserObjectTests
{
    [Fact]
    public void TestLaserRectangleBounds_NoRotation()
    {
        var rect = new LaserRectangle
        {
            Position = new PointF(10, 20),
            Size = new SizeF(30, 40),
            Rotation = 0
        };

        var bounds = rect.GetBounds();

        Assert.Equal(10, bounds.X, 2);
        Assert.Equal(20, bounds.Y, 2);
        Assert.Equal(30, bounds.Width, 2);
        Assert.Equal(40, bounds.Height, 2);
    }

    [Fact]
    public void TestLaserRectangleBounds_45DegreeRotation()
    {
        // 10x10 rect at 0,0 rotated 45 degrees around center (5,5)
        // Corners: (0,0), (10,0), (10,10), (0,10)
        // Rotated corners will be further out.
        // Diagonal is sqrt(200) approx 14.14
        var rect = new LaserRectangle
        {
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10),
            Rotation = 45
        };

        var bounds = rect.GetBounds();

        // Center is (5,5). 
        // Peak points should be at 5 +/- (10/2 * sqrt(2)) = 5 +/- 7.07
        // Min X/Y approx -2.07, Max X/Y approx 12.07
        Assert.Equal(-2.07, bounds.X, 1);
        Assert.Equal(-2.07, bounds.Y, 1);
        Assert.Equal(14.14, bounds.Width, 1);
        Assert.Equal(14.14, bounds.Height, 1);
    }

    [Fact]
    public void TestLaserCircleBounds()
    {
        var circle = new LaserCircle
        {
            Position = new PointF(50, 60),
            Size = new SizeF(20, 20)
        };

        var bounds = circle.GetBounds();

        Assert.Equal(50, bounds.X, 2);
        Assert.Equal(60, bounds.Y, 2);
        Assert.Equal(20, bounds.Width, 2);
        Assert.Equal(20, bounds.Height, 2);
    }

    [Fact]
    public void TestLaserPathBounds()
    {
        var path = new LaserPath();
        path.Points.Add(new PointF(0, 0));
        path.Points.Add(new PointF(100, 50));
        path.Points.Add(new PointF(50, 100));
        
        path.UpdateBounds();
        var bounds = path.GetBounds();

        Assert.Equal(0, bounds.X, 2);
        Assert.Equal(0, bounds.Y, 2);
        Assert.Equal(100, bounds.Width, 2);
        Assert.Equal(100, bounds.Height, 2);
    }
    
    [Fact]
    public void TestLaserPathBounds_WithRotation()
    {
        var path = new LaserPath();
        path.Points.Add(new PointF(0, 0));
        path.Points.Add(new PointF(10, 0));
        path.UpdateBounds(); // Update Position/Size before rotation
        path.Rotation = 90; // Rotate 90 degrees around center (5,0)
        
        // Point (0,0) -> (5,-5)
        // Point (10,0) -> (5,5)
        // New bounds: X=5, Y=-5, W=0, H=10
        
        var bounds = path.GetBounds();
        
        Assert.Equal(5, bounds.X, 2);
        Assert.Equal(-5, bounds.Y, 2);
        Assert.Equal(0, bounds.Width, 1);
        Assert.Equal(10, bounds.Height, 1);
    }
}
