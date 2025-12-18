using Xunit;
using laser_gui_test.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System;

namespace laser_gui_test.Tests;

public class PathWarpTests
{
    [Fact]
    public void TestIdentityWarp()
    {
        var backbone = new List<PointF> { new PointF(0, 0), new PointF(100, 0) };
        using var gp = new GraphicsPath();
        gp.AddRectangle(new RectangleF(10, 5, 20, 10));
        
        using var warped = PathWarp.CreateWarpedPath(gp, backbone, 0);
        var pts = warped.PathPoints;
        
        // Check Bounding Box instead of indices (due to subdivision)
        float minX = pts.Min(p => p.X);
        float maxX = pts.Max(p => p.X);
        float minY = pts.Min(p => p.Y);
        float maxY = pts.Max(p => p.Y);

        Assert.Equal(10, minX, 1);
        Assert.Equal(30, maxX, 1);
        Assert.Equal(5, minY, 1);
        Assert.Equal(15, maxY, 1);
        
        // Ensure it's subdivided
        Assert.True(warped.PointCount > 10);
    }

    [Fact]
    public void TestSegmentBoundaryKink()
    {
        // 90 degree turn: (0,0) -> (50,0) -> (50,50)
        var backbone = new List<PointF> { 
            new PointF(0, 0), 
            new PointF(50, 0), 
            new PointF(50, 50) 
        };
        
        // Rect spanning the corner: X=40 to 60, Y=5
        using var gp = new GraphicsPath();
        gp.AddRectangle(new RectangleF(40, 5, 20, 2));
        
        using var warped = PathWarp.CreateWarpedPath(gp, backbone, 0);
        var pts = warped.PathPoints;
        
        // With smooth interpolation:
        // Normal at (50,0) is average of (0,1) and (-1,0) -> Normalized(-1, 1) -> (-0.7, 0.7)
        // Point at X=50, Y=5 should be close to (50,0) + normal * 5 = (50 - 3.5, 0 + 3.5) = (46.5, 3.5)
        // This is much better than the previous piecewise result which would have been at (45, 5) or similar with a jump.
        
        // Let's find a point that was previously "crushed".
        // The midpoint of the rect was at X=50.
        // It should be around the 45-degree angle.
        
        // We find the point in PathPoints that corresponds to original X=50.
        // Since AddRectangle was used, it has been flattened by PathWarp internally.
        // So we might have more points.
    }

    [Fact]
    public void TestTextFlattening()
    {
        // Backbone is a quarter circle (approx)
        var backbone = new List<PointF>();
        for (int i = 0; i <= 10; i++) {
            float a = (float)(i * Math.PI / 20.0);
            backbone.Add(new PointF((float)Math.Cos(a) * 100, (float)Math.Sin(a) * 100));
        }

        // A single long straight line from X=0 to X=50, Y=5
        using var gp = new GraphicsPath();
        gp.AddLine(0, 5, 50, 5);
        
        using var warped = PathWarp.CreateWarpedPath(gp, backbone, 0);
        
        // If flattening WORKED, we should have more than 2 points.
        Assert.True(warped.PointCount > 2, "Path should be flattened to follow the curve");
        
        // The first and last points should be at the correct offsets from backbone start/mid
        var pts = warped.PathPoints;
        // Start: X=0, Y=5. Backbone[0]=(100,0). Normal=(1,0). Result=(105, 0)
        // Wait, Backbone[0] is (100,0). Normal (-dy, dx) -> (-(0), (100-100))? 
        // Tangent is downwards-ish.
        // Let's just check if it's curved.
        
        float dist0 = (float)Math.Sqrt(Math.Pow(pts[0].X - pts[1].X, 2) + Math.Pow(pts[0].Y - pts[1].Y, 2));
        float totalDist = 0;
        for(int i=0; i<pts.Length-1; i++) totalDist += (float)Math.Sqrt(Math.Pow(pts[i].X - pts[i+1].X, 2) + Math.Pow(pts[i].Y - pts[i+1].Y, 2));
        
        // If it's a straight line, distance between start/end is totalDist.
        float chord = (float)Math.Sqrt(Math.Pow(pts[0].X - pts.Last().X, 2) + Math.Pow(pts[0].Y - pts.Last().Y, 2));
        Assert.True(totalDist > chord + 0.1f, "Path should be curved");
    }
}
