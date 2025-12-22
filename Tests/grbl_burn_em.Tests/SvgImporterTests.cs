/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using Xunit;
using grbl_burn_em.Data;
using System.IO;
using System.Linq;
using System.Drawing;
using System;

namespace grbl_burn_em.Tests;

public class SvgImporterTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string CreateSvgFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".svg");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void TestViewBoxScaling_Mm()
    {
        // Width 100mm, Height 100mm, ViewBox 0 0 50 50.
        // Scale Factor = 2.0 (50 units -> 100mm)
        // Rect x=10 y=10 w=20 h=20 -> x=20 y=20 w=40 h=40 (mm)
        // Position (Laser Y-Up): Top-Left SVG (20,20) -> Bottom-Left Laser. 
        // Doc Height 100. Y=20 (from top) -> Y=80 (from bottom).
        // Rect Height 40. Bottom of Rect = 80 - 40 = 40.
        // Expect Pos(20, 40), Size(40, 40).

        string svg = @"<svg width=""100mm"" height=""100mm"" viewBox=""0 0 50 50"" xmlns=""http://www.w3.org/2000/svg""><rect x=""10"" y=""10"" width=""20"" height=""20"" /></svg>";
        string file = CreateSvgFile(svg);

        var objects = SvgImporter.Import(file);

        Assert.Single(objects);
        var obj = objects[0];
        
        Assert.Equal(40, obj.Size.Width, 2);
        Assert.Equal(40, obj.Size.Height, 2);
        Assert.Equal(20, obj.Position.X, 2);
        Assert.Equal(40, obj.Position.Y, 2);
    }
    
    [Fact]
    public void TestUnitParsing() 
    {
        // Width="100mm". No ViewBox -> Default scale = pxToMm.
        // Elements with units (mm, in, px) are parsed to Pixels by ParseDimension.
        // Then Scaled by pxToMm.
        // Result: 10mm -> 37.8px * pxToMm = 10mm.
        
        string svg = @"<svg width=""100mm"" height=""100mm"" xmlns=""http://www.w3.org/2000/svg"">
            <rect id=""mm"" width=""10mm"" height=""10"" x=""0"" y=""0"" />
            <rect id=""in"" width=""1in"" height=""10"" x=""0"" y=""20"" />
            <rect id=""px"" width=""96px"" height=""10"" x=""0"" y=""40"" />
        </svg>";

        var objs = SvgImporter.Import(CreateSvgFile(svg));
        
        Assert.Equal(3, objs.Count);
        
        // Rect 1: 10mm. Expect ~10.0
        // Currently SvgImporter might be returning pixels as user-units, so let's see.
        // If it parses "10mm" to 37.8 (px), and scale is 1.0 (from 100mm/100u).
        // It presumably results in 37.8mm. Which is wrong, but let's Assert what SHOULD be true.
        // We will likely fail this test, which identifies the bug.
        
        var r_mm = objs[0];
        var r_in = objs[1];
        var r_px = objs[2];

        // Tolerances due to float precision
        Assert.Equal(10.0, r_mm.Size.Width, 1); 
        Assert.Equal(25.4, r_in.Size.Width, 1);
        Assert.Equal(25.4, r_px.Size.Width, 1); 
    }
    
    [Fact]
    public void TestObjectTypes_Rect()
    {
        string svg = @"<svg width=""100"" height=""100"" xmlns=""http://www.w3.org/2000/svg""><rect x=""10"" y=""10"" width=""20"" height=""30"" /></svg>";
        // No units -> 100px width/height? SvgImporter scales to internal MM?
        // If width="100", ParseDimension="100". pxToMm applies -> 26.458 mm.
        // VW=100 (fallback). Scale = 1. 
        // Rect 20x30 -> 20x30 scaled by pxToMm?
        // Wait, my fix ONLY affected Main Document Width/Height.
        // It did NOT affect ParseDimension calls inside ParseRect/ParsePath.
        // Inside ParseRect: w = ParseDimension(...). Returns 20.
        // GlobalTransform: Scale(width/vw, ...). 
        // If width="100" (unitless) -> treated as 100 * pxToMm = 26.45 mm.
        // VW = 100.
        // Scale = 0.2645.
        // Rect w=20. Transformed w = 20 * 0.2645 = 5.29 mm.
        // This effectively treats unitless numbers as Pixels (at 96 DPI) and converts entire doc to MM.
        // So 20px -> 5.29mm. Correct.
        
        string file = CreateSvgFile(svg);
        var objs = SvgImporter.Import(file);
        Assert.Single(objs);
        var r = objs[0];
        // 20 px * (25.4/96) = 5.29166
        Assert.Equal(5.29, r.Size.Width, 2); 
    }

    [Fact]
    public void TestCircle()
    {
        string svg = @"<svg width=""100"" height=""100"" xmlns=""http://www.w3.org/2000/svg""><circle cx=""50"" cy=""50"" r=""20"" /></svg>";
        var objs = SvgImporter.Import(CreateSvgFile(svg));
        Assert.Single(objs);
        var c = objs[0]; // LaserPath (Circle converted to path/lines)
        
        // Size = 40x40 px -> mm
        Assert.Equal(40 * (25.4/96), c.Size.Width, 1);
    }
    
    [Fact]
    public void TestPath()
    {
        string svg = @"<svg width=""100"" height=""100"" xmlns=""http://www.w3.org/2000/svg""><path d=""M10 10 L 30 10 L 30 30 Z"" /></svg>";
        // Triangle 20x20
        var objs = SvgImporter.Import(CreateSvgFile(svg));
        Assert.Single(objs);
        Assert.Equal(20 * (25.4/96), objs[0].Size.Width, 1);
    }

    [Fact]
    public void TestGroupingInternalTransforms()
    {
        // Group with translate
        string svg = @"<svg width=""100"" height=""100"" xmlns=""http://www.w3.org/2000/svg"">
          <g transform=""translate(10, 10)"">
            <rect x=""0"" y=""0"" width=""10"" height=""10"" />
          </g>
        </svg>";
        
        var objs = SvgImporter.Import(CreateSvgFile(svg));
        var obj = objs[0];
        
        // Rect at 0,0 inside group. Group at 10,10.
        // Absolute Pos = 10,10 (px).
        // Y-Flip: Doc H=100. Y=10. Bottom Y = 90.
        // Rect H=10. Bottom = 80?
        // Wait, Top Y = 10 + 10 = 20 (Bottom of rect in svg coords).
        // In Laser (Y-Up): H=100.
        // Svg Y=10 -> Laser Y = 90.
        // Rect Top-Left is at 10,10.
        // Size 10x10.
        // Svg Bounds: X=10, Y=10, W=10, H=10.
        // Laser Bounds: X=10, Y=100-(10+10) = 80.
        // Scaled to MM.
        
        float scale = 25.4f / 96.0f;
        Assert.Equal(10 * scale, obj.Position.X, 2);
        Assert.Equal(80 * scale, obj.Position.Y, 2); 
    }
}
