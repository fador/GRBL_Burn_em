/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using grbl_burn_em.Data;

namespace grbl_burn_em.Data.Generators;

public struct ArrayParameters
{
    public int Rows;
    public int Cols;
    public float GapX;
    public float GapY;
    
    // Incremental Rotation
    public float RowRotStep;
    public float ColRotStep;
    
    // Randomization
    public float RandomRotMin;
    public float RandomRotMax;
    public float RandomPosX;
    public float RandomPosY;
    public float RandomScaleMin; // Percentage (e.g. 80 for 80%)
    public float RandomScaleMax; // Percentage (e.g. 120 for 120%)
    
    public int Seed;
    
    public static ArrayParameters Default => new()
    {
        Rows = 1,
        Cols = 1,
        GapX = 5,
        GapY = 5,
        RandomScaleMin = 100,
        RandomScaleMax = 100
    };
}

public class ArrayLayoutGenerator
{
    private Random _rng;

    public ArrayLayoutGenerator(int seed)
    {
        _rng = new Random(seed);
    }
    
    public List<LaserObject> Generate(IEnumerable<LaserObject> sourceObjects, ArrayParameters parameters)
    {
        var result = new List<LaserObject>();
        var sourceList = sourceObjects.ToList();
        
        if (!sourceList.Any()) return result;

        // Reset RNG for deterministic preview
        _rng = new Random(parameters.Seed);

        foreach (var obj in sourceList)
        {
            var bounds = obj.GetBounds();
            float w = bounds.Width;
            float h = bounds.Height;
            float stepX = w + parameters.GapX;
            float stepY = h + parameters.GapY; // Assuming Y+ is down/forward

            for (int r = 0; r < parameters.Rows; r++)
            {
                for (int c = 0; c < parameters.Cols; c++)
                {
                    // Clone source
                    var clone = obj.Clone();
                    
                    // 1. Calculate Base Position Shift
                    float BaseDx = c * stepX;
                    float BaseDy = r * stepY;

                    // 2. Incremental Rotation
                    float incRot = (r * parameters.RowRotStep) + (c * parameters.ColRotStep);
                    clone.Rotation += incRot;

                    // 3. Random Rotation
                    if (parameters.RandomRotMin != parameters.RandomRotMax)
                    {
                         float range = parameters.RandomRotMax - parameters.RandomRotMin;
                         float rndRot = (float)(_rng.NextDouble() * range) + parameters.RandomRotMin;
                         clone.Rotation += rndRot;
                    }
                    
                    // 4. Random Scale
                    if (parameters.RandomScaleMin != 100 || parameters.RandomScaleMax != 100)
                    {
                        float min = parameters.RandomScaleMin / 100f;
                        float max = parameters.RandomScaleMax / 100f;
                        float scaleFactor = (float)(_rng.NextDouble() * (max - min) + min);
                        
                        ApplyScale(clone, scaleFactor);
                    }

                    // 5. Random Position Offset
                    float rndX = 0;
                    float rndY = 0;
                    if (parameters.RandomPosX > 0) 
                        rndX = (float)((_rng.NextDouble() * 2 - 1) * parameters.RandomPosX);
                    if (parameters.RandomPosY > 0)
                        rndY = (float)((_rng.NextDouble() * 2 - 1) * parameters.RandomPosY);

                    // Apply layout shift + random shift
                    // Note: We need to shift relative to original position
                    ShiftObject(clone, BaseDx + rndX, BaseDy + rndY);

                    result.Add(clone);
                }
            }
        }
        
        return result;
    }

    private void ApplyScale(LaserObject obj, float scale)
    {
        // Scale around center
        var center = new PointF(obj.Position.X + obj.Size.Width / 2f, obj.Position.Y + obj.Size.Height / 2f);
        
        // Scale Size
        obj.Size = new SizeF(obj.Size.Width * scale, obj.Size.Height * scale);
        
        // Relocate Top-Left to match new size centered at same point
        obj.Position = new PointF(center.X - obj.Size.Width / 2f, center.Y - obj.Size.Height / 2f);
        
        if (obj is LaserText text)
        {
            text.FontSize *= scale;
            text.UpdateTextSize();
            // Now re-center
            obj.Position = new PointF(center.X - obj.Size.Width / 2f, center.Y - obj.Size.Height / 2f);
        }
        else if (obj is LaserPath path)
        {
            var oldCenter = center;
            for (int i = 0; i < path.Points.Count; i++)
            {
                float px = path.Points[i].X - oldCenter.X;
                float py = path.Points[i].Y - oldCenter.Y;
                px *= scale;
                py *= scale;
                path.Points[i] = new PointF(px + oldCenter.X, py + oldCenter.Y);
            }
            path.UpdateBounds(); // Recalc Size and Position
        }
        else if (obj is LaserGroup group)
        {
             // Recursively scale children around the GROUP center
             var groupCenter = center;
             
             foreach(var child in group.Children)
             {
                 // We apply scale to child relative to GROUP center
                 // 1. Position shift
                 float dx = child.Position.X - groupCenter.X;
                 float dy = child.Position.Y - groupCenter.Y;
                 
                 dx *= scale;
                 dy *= scale;
                 
                 child.Position = new PointF(groupCenter.X + dx, groupCenter.Y + dy);
                 
                 // 2. Scale the child itself (recursive)
                 ApplyScale(child, scale);
             }
             // group.UpdateBounds(); // Not implemented in snippet, but GetBounds() works dynamically.
        }
    }

    private void ShiftObject(LaserObject obj, float dx, float dy)
    {
        obj.Position = new PointF(obj.Position.X + dx, obj.Position.Y + dy);
        
        if (obj is LaserPath path)
        {
            for (int i = 0; i < path.Points.Count; i++)
            {
                path.Points[i] = new PointF(path.Points[i].X + dx, path.Points[i].Y + dy);
            }
        }
        else if (obj is LaserGroup group)
        {
             foreach(var child in group.Children)
             {
                 ShiftObject(child, dx, dy);
             }
        }
    }
}
