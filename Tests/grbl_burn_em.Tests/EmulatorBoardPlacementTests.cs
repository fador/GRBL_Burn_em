/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using Emgu.CV.Aruco;
using grbl_burn_em_emulator;

namespace grbl_burn_em.Tests;

/// <summary>
/// Verifies the emulator's ChArUco board placement: the bed pixel <-> CNC coordinate
/// conversion used by the drag-to-position feature, and that drawing the board at an
/// arbitrary CNC position renders its origin corner at exactly that position.
/// </summary>
public class EmulatorBoardPlacementTests
{
    private const float BedScale = 1.5f;
    private const int BedHeight = 600; // 400mm bed at 1.5 px/mm

    private static Dictionary TestDictionary =>
        new(Dictionary.PredefinedDictionaryName.Dict4X4_50);

    [Fact]
    public void BedPixelToCnc_MatchesRendererConvention()
    {
        // Bed is 600px tall for 400mm; CNC (0,0) is at the bottom-left.
        var origin = EmulatorBoardRenderer.BedPixelToCnc(new PointF(0, BedHeight), BedScale, BedHeight);
        Assert.Equal(0f, origin.X, 3);
        Assert.Equal(0f, origin.Y, 3);

        var far = EmulatorBoardRenderer.BedPixelToCnc(new PointF(600, 0), BedScale, BedHeight);
        Assert.Equal(400f, far.X, 3);
        Assert.Equal(400f, far.Y, 3);

        var mid = EmulatorBoardRenderer.BedPixelToCnc(new PointF(150, 450), BedScale, BedHeight);
        Assert.Equal(100f, mid.X, 3);
        Assert.Equal(100f, mid.Y, 3);
    }

    [Fact]
    public void CncToBedPixel_IsInverseOfBedPixelToCnc()
    {
        foreach (var cnc in new[] { new PointF(0, 0), new PointF(123.4f, 78.9f), new PointF(400, 400) })
        {
            var px = EmulatorBoardRenderer.CncToBedPixel(cnc, BedScale, BedHeight);
            var back = EmulatorBoardRenderer.BedPixelToCnc(px, BedScale, BedHeight);
            Assert.Equal(cnc.X, back.X, 2);
            Assert.Equal(cnc.Y, back.Y, 2);
        }
    }

    [Fact]
    public void DrawBoard_AtDraggedPosition_RendersOriginAtCncPosition()
    {
        using var bed = new Bitmap(600, 600);
        using (var g = Graphics.FromImage(bed))
            g.Clear(Color.Beige);

        // The user dragged the board so its origin corner lands at CNC (100, 100),
        // i.e. bed pixel (150, 450).
        var cnc = EmulatorBoardRenderer.BedPixelToCnc(new PointF(150, 450), BedScale, BedHeight);
        EmulatorBoardRenderer.DrawBoard(bed, BedScale, cnc.X, cnc.Y, 5, 120f, TestDictionary);

        // The board's top-left square (black) must now sit with its bottom-left corner
        // at (150, 450): probe just inside the board region (160, 432) and just
        // outside the whole drawn bitmap (110, 300).
        var inside = bed.GetPixel(160, 432);
        Assert.True(inside.R < 128 && inside.G < 128 && inside.B < 128,
            $"expected dark board pixel at (160,432), got RGB({inside.R},{inside.G},{inside.B})");

        var outside = bed.GetPixel(110, 300);
        Assert.True(outside.R > 200 && outside.G > 200 && outside.B > 150,
            $"expected beige bed pixel at (110,300), got RGB({outside.R},{outside.G},{outside.B})");
    }
}
