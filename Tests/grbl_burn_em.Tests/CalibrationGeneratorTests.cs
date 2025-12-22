using Xunit;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;
using System.Linq;

namespace grbl_burn_em.Tests;

public class CalibrationGeneratorTests
{
    [Fact]
    public void TestDefaultGridGeneration()
    {
        var generator = new CalibrationGridGenerator();
        generator.Rows = 5;
        generator.Cols = 5;
        generator.MinPower = 10;
        generator.MaxPower = 50;
        generator.MinSpeed = 1000;
        generator.MaxSpeed = 5000;

        var objects = generator.Generate();

        // Count Analysis:
        // Loop Rows (0..4):
        //   Add Power Label (1 per row) -> 5
        //   Loop Cols (0..4):
        //     If Row==0: Add Speed Label (1 per col) -> 5
        //     Add Rectangle (1 per col) -> 25
        // End Loops
        // Add Title Speed (1)
        // Add Title Power (1)
        // Total Expected: 5 + 5 + 25 + 2 = 37
        Assert.Equal(37, objects.Count);

        // Verify Rectangles
        var rects = objects.OfType<LaserRectangle>().ToList();
        Assert.Equal(25, rects.Count);

        // Verify First Rect (Row 0, Col 0)
        // Expected: MinPower, MinSpeed
        var firstRect = rects[0];
        Assert.Equal(10f, firstRect.Power);
        Assert.Equal(1000f, firstRect.Speed);

        // Verify Last Rect (Row 4, Col 4)
        // Expected: MaxPower, MaxSpeed
        var lastRect = rects[24];
        Assert.Equal(50f, lastRect.Power);
        Assert.Equal(5000f, lastRect.Speed);
        
        // Verify Middle Rect (Row 2, Col 2)
        // Power: 10 + (50-10) * 2 / 4 = 10 + 40*0.5 = 30
        // Speed: 1000 + (5000-1000) * 2 / 4 = 1000 + 4000*0.5 = 3000
        var middleRect = rects[12];
        Assert.Equal(30f, middleRect.Power);
        Assert.Equal(3000f, middleRect.Speed);
    }

    [Fact]
    public void Generate_EngraveMode_ShouldSwapAxesAndSetFillMode()
    {
        var generator = new CalibrationGridGenerator();
        generator.Rows = 5;
        generator.Cols = 5;
        generator.MinPower = 10;
        generator.MaxPower = 50;
        generator.MinSpeed = 1000;
        generator.MaxSpeed = 5000;
        generator.IsEngrave = true;

        var objects = generator.Generate();

        // In Engrave Mode:
        // Rows = Speed Axis (Y)
        // Cols = Power Axis (X)
        // Check Rectangles
        var rects = objects.OfType<LaserRectangle>()
                           .Where(r => r.Name.StartsWith("Grid") && !r.Name.Contains("Outline"))
                           .ToList();
        
        // Count should still be 25
        Assert.Equal(25, rects.Count);

        // Verify First Rect (Row 0, Col 0)
        // Row 0 = Min Speed (Engrave: Y=Speed)
        // Col 0 = Min Power (Engrave: X=Power)
        var firstRect = rects[0];
        Assert.Equal(10f, firstRect.Power); // X Value
        Assert.Equal(1000f, firstRect.Speed); // Y Value
        Assert.Equal(LayerMode.Fill, firstRect.Mode);

        // Verify Outline exists (if we added it)
        // The generator adds "Grid Outline" rectangles separately
        var outlines = objects.OfType<LaserRectangle>().Where(r => r.Name == "Grid Outline").ToList();
        Assert.Equal(25, outlines.Count);
        Assert.Equal(LayerMode.Cut, outlines[0].Mode);

        // Verify Last Rect (Row 4, Col 4)
        // Row 4 = Max Speed
        // Col 4 = Max Power
        var lastRect = rects[24];
        Assert.Equal(50f, lastRect.Power); 
        Assert.Equal(5000f, lastRect.Speed); 
        
        // Verify Mixed Rect (Row 1, Col 4) -> (Row Index 1, Col Index 4)
        // Speed (Y, Row 1) = 1000 + (5000-1000)*1/4 = 2000
        // Power (X, Col 4) = 10 + (50-10)*4/4 = 50
        var mixedRect = rects[5 + 4]; // Index 9
        Assert.Equal(50f, mixedRect.Power);
        Assert.Equal(2000f, mixedRect.Speed);
    }
}
