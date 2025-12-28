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
using System.Threading;
using System.Threading.Tasks;
using grbl_burn_em.Data.Geometry;

namespace grbl_burn_em.Data.Generators
{
    public class NestingManager
    {
        private static NestingManager? _instance;
        public static NestingManager Instance => _instance ??= new NestingManager();

        public event Action<int, int>? ProgressChanged;
        public event Action<string>? LogMessage;
        public event Action<Polygon>? OnPartPlaced; // For live view

        // Configuration
        public double Alpha { get; set; } = 0.15; // Grid coefficient
        public double Beta { get; set; } = 0.1;   // Shift coefficient
        public double Theta { get; set; } = 45;   // Rotation step
        public SizeF SheetSize { get; set; } = new SizeF(800, 600);

        private void Log(string msg) => LogMessage?.Invoke(msg);

        public async Task<List<(LaserObject Obj, PointD NewPos, double Rotation)>> RunNestingAsync(List<LaserObject> objects, CancellationToken token)
        {
            var results = new List<(LaserObject, PointD, double)>();
            
            // 1. Convert LaserObjects to Polygons
            var parts = new List<Polygon>();
            foreach (var obj in objects)
            {
                var poly = ConvertToPolygon(obj);
                if (poly != null)
                {
                    parts.Add(poly);
                }
                else
                {
                    Log($"Skipping object {obj.Name} (Unsupported type or empty)");
                }
            }

            if (parts.Count == 0) return results;

            // 2. Sort by Area (Descending)
            parts.Sort((a, b) => b.Area.CompareTo(a.Area));
            Log($"Sorted {parts.Count} parts by area.");

            var placedPolygons = new List<Polygon>();
            int totalParts = parts.Count;
            int currentPart = 0;

            // 3. Nesting Loop
            foreach (var part in parts)
            {
                if (token.IsCancellationRequested) break;
                currentPart++;
                ProgressChanged?.Invoke(currentPart, totalParts);

                Polygon? bestPos = null;
                bool found = false;

                // Part Metrics
                double L = part.Radius * 2;
                double D = Math.Max(5, Alpha * L); // Grid Step
                double d = Beta * D; // Shift Step

                // Generate Grid
                // Optimized: Scan from 0,0 outwards
                var gridPoints = new List<GridPoint>();
                
                // Ensure we don't scan outside sheet
                double maxX = SheetSize.Width - part.Bounds.Width;
                double maxY = SheetSize.Height - part.Bounds.Height;

                for (double y = 0; y <= Math.Max(0, maxY) + D; y += D)
                {
                    for (double x = 0; x <= Math.Max(0, maxX) + D; x += D)
                    {
                        // Safe bound check
                         if (x > SheetSize.Width || y > SheetSize.Height) continue;
                        gridPoints.Add(new GridPoint(x, y));
                    }
                }
                gridPoints.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

                Log($"Placing part {currentPart}/{totalParts} (L={L:F1}, D={D:F1})...");

                // Rotations
                var rotations = new List<double>();
                for (double angle = 0; angle < 360; angle += Theta) rotations.Add(angle);

                // Search Grid
                foreach (var pt in gridPoints)
                {
                    if (token.IsCancellationRequested) break;
                    
                    // Yield briefly to keep UI responsive if running on UI thread (though Task.Run should handle it)
                    // if (currentPart % 5 == 0) await Task.Delay(1); 

                    foreach (var angle in rotations)
                    {
                        // Create Candidate Polygon
                        // Reset to 0,0 (remove current world pos offset logic? 
                        // Our Polygon conversion keeps World Coords.
                        // We need to Normalize it to 0,0 centroid or TopLeft for rotation?
                        // JS Logic: translate(-minX, -minY) -> 0,0 top-left aligned.
                        
                        var centeredPoly = part.Translate(-part.Bounds.MinX, -part.Bounds.MinY);
                        // Rotate around 0,0 (which is now Top-Left of the bounding box)
                        var rotated = centeredPoly.Rotate(angle, new PointD(0, 0));
                        // Move to grid point
                        var candidate = rotated.Translate(pt.X, pt.Y);

                        // Check Sheet Bounds
                        if (!GeometryHelpers.IsWithinSheet(candidate, SheetSize.Width, SheetSize.Height)) continue;

                        // Check Overlap
                        bool overlaps = false;
                        foreach (var placed in placedPolygons)
                        {
                            if (GeometryHelpers.DoPolygonsIntersect(candidate, placed))
                            {
                                overlaps = true;
                                break;
                            }
                        }

                        // Local Move Heuristic
                        if (overlaps)
                        {
                            var shifts = new[] 
                            { 
                                new { dx = 0.0, dy = d }, 
                                new { dx = d, dy = 0.0 },
                                new { dx = 0.0, dy = -d },
                                new { dx = -d, dy = 0.0 }
                            };

                            foreach (var shift in shifts)
                            {
                                var shifted = candidate.Translate(shift.dx, shift.dy);
                                if (!GeometryHelpers.IsWithinSheet(shifted, SheetSize.Width, SheetSize.Height)) continue;

                                bool shiftOverlaps = false;
                                foreach (var placed in placedPolygons)
                                {
                                    if (GeometryHelpers.DoPolygonsIntersect(shifted, placed))
                                    {
                                        shiftOverlaps = true;
                                        break;
                                    }
                                }

                                if (!shiftOverlaps)
                                {
                                    candidate = shifted;
                                    overlaps = false;
                                    break;
                                }
                            }
                        }

                        if (!overlaps)
                        {
                            bestPos = candidate;
                            found = true;
                            goto FoundLabel;
                        }
                    }
                }

                FoundLabel:
                if (found && bestPos != null)
                {
                    placedPolygons.Add(bestPos);
                    // Match back to object
                    // We need to return the NEW Position and Rotation for the object.
                    // The 'candidate' poly is the final state.
                    // Original Object: Rotation R0, Points P0.
                    // New State: We applied Translation T1 (to 0,0), Rotation R_new, Translation T2 (to Grid).
                    // Net Rotation = R_new.
                    // Net Position: 
                    // LaserObjects use Top-Left standard? Or Center?
                    // LaserCircle/Rectangle/Image use Position as Top-Left (mostly).
                    // LaserPath uses MinX/MinY of points.

                    // We need to pass back the Delta or absolute parameters.
                    // bestPos.Points is the final world coordinates of vertices.
                    
                    // For Path: Just use the points.
                    // For Primitive (Rect/Circle): We need to deduce Position/Rotation.
                    // Wait, if we rotated a Rectangle 45deg, it's now a Path essentially, or a Rotated Rectangle.
                    // LaserRectangle supports Rotation.
                    // Our 'bestPos' was created by Rotate(angle). 
                    // So the object's new Rotation should be 'angle' (relative to what? JS reset to 0 then rotated).
                    // Original part Rotation was ignored in 'ConvertToPolygon' (we baked it in).
                    // So 'angle' is the absolute rotation.
                    
                    // But wait, if we support 'Group', we baked all children transformations into points.
                    // If we rotate the Group Polygon, we rotate the whole group.
                    
                    if (part.Tag is LaserObject tagObj)
                    {
                        results.Add((tagObj, bestPos.Points[0], 0)); // Placeholder return, need better logic
                    }
                }
                else
                {
                    Log($"Failed to place part {part.Tag}");
                }
            }
            
            // Refine Results
            // We have the final Polygons in 'placedPolygons'.
            // Each Polygon has a Tag linking to LaserObject.
            // We want to update the LaserObject.
            
            // Issue: 
            // If we have a LaserRectangle at 45 deg. We convert to Polygon (4 points).
            // We nest it. New Polygon is at some other place, maybe rotated +90 deg.
            // How do we map back to LaserRectangle Properties (Pos, Rot)?
            
            // Solution: 
            // 1. For Paths: Replace Points with new Polygon Points. Set Rotation = 0.
            // 2. For Primitives: 
            //    We tracked the Applied Rotation 'angle'.
            //    We tracked the Applied Translation.
            //    But we did (Translate to 0) -> (Rotate) -> (Translate to Grid).
            //    We can calculate the delta.
            
            var finalResults = new List<(LaserObject Obj, PointD NewPos, double Rotation)>();
            for(int i=0; i<placedPolygons.Count; i++) 
            {
               var poly = placedPolygons[i];
               if (poly.Tag is LaserObject obj)
               {
                   // We can't easily reverse-engineer the primitives if we just have points.
               // But, we know what we did:
               // We took the polygon (baked), moved its bounds.Min to 0,0, Rotated 'angle', moved to Grid.
               // So new Rotation = 'angle'.
               // New Center = ...?
               
               // Alternative: Just return the placed Polygon and let integration layer handle it?
               // Or simpler: Convert everything to LaserPath after nesting?
               // User might want to keep Rectangles as Rectangles.
               
               // Let's store the 'angle' used in the loop?
               // Or better: The Polygon class doesn't store the transformations.
               
               // Let's modify the loop to store the transform data.
               // But 'placedPolygons' is just List<Polygon>.
               // I'll assume for V1 we convert everything to Paths? No that's destructive.
               
               // Let's Re-do step 4 logic properly.
            }
            } // Close for loop
            
            // Re-Implementing the return value construction inside the loop
            // to capture the exact transform.
            
            // Actually, let's just use the 'bestPos' polygon to calculate the bounds shift?
            // If I rotate a rectangle, I change its center.
            
            // Hack for V1:
            // Just return the logic result:
            // (Obj, FinalPoints)
            // It's up to the caller to apply it.
            // If caller is smart, for primitives it can maybe match? 
            // No, "Convert this QLM...". The JS outputs "packedPolygons" which are just points for drawing.
            
            // I will return the List of Placed Polygons (with Tags).
            // The Caller (UI) will likely just draw them for now.
            // "Apply" button will commit. 
            // To commit, if I have a LaserRectangle and I get back a Polygon at 30deg, 
            // I can set LaserRectangle.Rotation = 30, Position = NewTopLeft?
            // Yes, if I track what 'angle' I chose.
            // I need to store the angle in the Polygon or a wrapper.
             
            await Task.CompletedTask; // Satisfy async
            return results; // Logic moved to RunNestingWithTransformAsync below
        }

        public class NestingResult
        {
            public LaserObject OriginalObject { get; set; } = default!;
            public Polygon PlacedPolygon { get; set; } = default!;
            public double Rotation { get; set; } // Absolute rotation applied
            public PointD Translation { get; set; } // Shift applied
        }

        public async Task<List<NestingResult>> RunNesting(List<LaserObject> objects, CancellationToken token)
        {
             var results = new List<NestingResult>();
             var parts = new List<PolyWrapper>();

             foreach (var obj in objects)
             {
                 var poly = ConvertToPolygon(obj);
                 if (poly != null)
                 {
                     parts.Add(new PolyWrapper { Polygon = poly, Original = obj });
                 }
             }
             
             parts.Sort((a, b) => b.Polygon.Area.CompareTo(a.Polygon.Area));
             var placedWrappers = new List<PolyWrapper>();
             var unplacedWrappers = new List<PolyWrapper>();

             int total = parts.Count;
             for(int i=0; i<total; i++)
             {
                 if (token.IsCancellationRequested) break;
                 ProgressChanged?.Invoke(i+1, total);
                 
                 var wrapper = parts[i];
                 var part = wrapper.Polygon;
                 
                 // Step sizes
                 double L = part.Radius * 2;
                 double D = Math.Max(5, Alpha * L);
                 double d = Beta * D;

                 var gridPoints = GenerateGrid(SheetSize, part.Bounds, D);
                 
                 // Rotations
                 var rotations = new List<double>();
                 for (double angle = 0; angle < 360; angle += Theta) rotations.Add(angle);
                 
                 Polygon? bestPoly = null;
                 double bestAngle = 0;
                 
                 bool found = false;
                 
                 foreach(var pt in gridPoints)
                 {
                     if (token.IsCancellationRequested) break;
                     foreach(var angle in rotations)
                     {
                         // Transform:
                         // 1. Center at Origin (Top-Left based on Bounds)
                         var centered = part.Translate(-part.Bounds.MinX, -part.Bounds.MinY);
                         // 2. Rotate
                         var rotated = centered.Rotate(angle, new PointD(0,0));
                         // 3. Move to Grid
                         var candidate = rotated.Translate(pt.X, pt.Y);
                         
                         if (!GeometryHelpers.IsWithinSheet(candidate, SheetSize.Width, SheetSize.Height)) continue;
                         
                         if (!CheckOverlap(candidate, placedWrappers, d))
                         {
                             // Try Nudging if overlap? 
                             // Wait, JS logic: "if overlaps... try shifts". 
                             // My CheckOverlap helper should NOT recurse.
                             
                             // Re-read JS:
                             // "if (overlaps) { shifts... if (!shiftOverlaps) { candidate=shifted; overlaps=false; } }"
                             
                             // Let's implement the nudge here
                             bool overlaps = IsOverlapping(candidate, placedWrappers);
                             if (overlaps)
                             {
                                 var shifts = new[] 
                                 { 
                                     new { dx = 0.0, dy = d }, 
                                     new { dx = d, dy = 0.0 }, 
                                     new { dx = 0.0, dy = -d }, 
                                     new { dx = -d, dy = 0.0 } 
                                 };
                                 
                                 foreach(var s in shifts)
                                 {
                                     var shifted = candidate.Translate(s.dx, s.dy);
                                     if (GeometryHelpers.IsWithinSheet(shifted, SheetSize.Width, SheetSize.Height) &&
                                         !IsOverlapping(shifted, placedWrappers))
                                     {
                                         candidate = shifted;
                                         overlaps = false;
                                         break;
                                     }
                                 }
                             }
                             
                             if (!overlaps)
                             {
                                 bestPoly = candidate;
                                 bestAngle = angle;
                                 found = true;
                                 goto FoundMatch;
                             }
                         }
                     }
                 }
                 
                 FoundMatch:
                 if (found)
                 {
                     placedWrappers.Add(new PolyWrapper { Polygon = bestPoly!, Original = wrapper.Original });
                     if (bestPoly != null) OnPartPlaced?.Invoke(bestPoly); // Fire live update
                     results.Add(new NestingResult 
                     { 
                         OriginalObject = wrapper.Original, 
                         PlacedPolygon = bestPoly!,
                         Rotation = bestAngle 
                     });
                 }
                 else
                 {
                     Log($"Could not place {wrapper.Original.Name}");
                     // Add to unplaced list
                     unplacedWrappers.Add(wrapper);
                 }
                 
                 // Yield occasionally
                 if (i % 5 == 0) await Task.Delay(1);
             }
             
             // Arrange unplaced objects outside the area
             if (unplacedWrappers.Count > 0)
             {
                 double cursorX = SheetSize.Width + 10; // Start 10 units to the right
                 double cursorY = 0;
                 double rowHeight = 0;
                 double maxH = SheetSize.Height;
                 
                 foreach(var wrapper in unplacedWrappers)
                 {
                     if (token.IsCancellationRequested) break;
                     
                     var poly = wrapper.Polygon; // Original polygon (0 rotation)
                     
                     // Move to current cursor
                     // Align Top-Left of poly to cursor
                     double dx = cursorX - poly.Bounds.MinX;
                     double dy = cursorY - poly.Bounds.MinY;
                     
                     var placed = poly.Translate(dx, dy);
                     
                     // Update row height
                     if (placed.Bounds.Height > rowHeight) rowHeight = placed.Bounds.Height;
                     
                     // Add to results
                     results.Add(new NestingResult
                     {
                         OriginalObject = wrapper.Original,
                         PlacedPolygon = placed,
                         Rotation = 0
                     });
                     
                     // Fire update so user sees them
                     OnPartPlaced?.Invoke(placed);
                     
                     // Advance cursor
                     cursorX += placed.Bounds.Width + 5; // 5mm gap
                     
                     // Wrap if too wide? Nah, just strip to right.
                     // Or maybe Wrap if we want a grid?
                     // Let's do simple row / column.
                     // Actually, vertical column is safer if width is unknown.
                     // But user said "outside". Right side is standard.
                     
                     // Let's check max Width. If we have many items, we might go very far right.
                     // Let's wrap to next row if X > SheetW * 2 (arbitrary)
                     if (cursorX > SheetSize.Width * 2.5)
                     {
                         cursorX = SheetSize.Width + 10;
                         cursorY += rowHeight + 5;
                         rowHeight = 0;
                     }
                 }
             }
             
             return results;
        }

        private bool IsOverlapping(Polygon candidate, List<PolyWrapper> existing)
        {
            foreach(var w in existing)
            {
                if (GeometryHelpers.DoPolygonsIntersect(candidate, w.Polygon)) return true;
            }
            return false;
        }

        // Helper for nudge check compatibility (removed unused d param)
        private bool CheckOverlap(Polygon candidate, List<PolyWrapper> existing, double d)
        {
             // This function signature was a mistake in thought process, ignore.
             return IsOverlapping(candidate, existing);
        }

        private List<GridPoint> GenerateGrid(SizeF sheet, PolygonBounds partBounds, double D)
        {
            var points = new List<GridPoint>();
            double maxX = sheet.Width - partBounds.Width;
            double maxY = sheet.Height - partBounds.Height;
            
            for (double y = 0; y <= Math.Max(0, maxY) + D; y += D)
            {
                for (double x = 0; x <= Math.Max(0, maxX) + D; x += D)
                {
                     if (x > sheet.Width || y > sheet.Height) continue;
                     points.Add(new GridPoint(x, y));
                }
            }
            points.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
            return points;
        }

        private Polygon? ConvertToPolygon(LaserObject obj)
        {
            var points = new List<PointD>();

            // Bake current rotation/position into points
            if (obj is LaserPath path)
            {
                // LaserPath stores Points relative to Position? 
                // LaserPath.Draw: Translates to Position+Center, Rotates, Translates back.
                // Wait, LaserPath.Points are usually raw coordinates if imported from Svg?
                // Let's check LaserPath.Draw again.
                // Draw: Translate(cx, cy), Rotate(Rot), Translate(-cx, -cy), DrawLines(Points).
                // So Points are in "World Space" but rotated around Center.
                // Actually if Rotation=0, Points are drawn as is. 
                // If Rotation!=0, they are rotated around Center.
                
                // We need the Absolute World Coordinates of the vertices.
                
                var mat = new System.Drawing.Drawing2D.Matrix();
                float cx = obj.Position.X + obj.Size.Width / 2f;
                float cy = obj.Position.Y + obj.Size.Height / 2f;
                mat.RotateAt(obj.Rotation, new PointF(cx, cy));
                
                var pts = path.Points.ToArray();
                if (pts.Length > 0)
                {
                    mat.TransformPoints(pts);
                    foreach(var p in pts) points.Add(new PointD(p.X, p.Y));
                }
            }
            else if (obj is LaserRectangle rect)
            {
                // 4 Corners
                float x = rect.Position.X;
                float y = rect.Position.Y;
                float w = rect.Size.Width;
                float h = rect.Size.Height;
                
                var corners = new PointF[] {
                    new PointF(x, y),
                    new PointF(x + w, y),
                    new PointF(x + w, y + h),
                    new PointF(x, y + h)
                };
                
                var mat = new System.Drawing.Drawing2D.Matrix();
                float cx = x + w / 2f;
                float cy = y + h / 2f;
                mat.RotateAt(rect.Rotation, new PointF(cx, cy));
                mat.TransformPoints(corners);
                
                foreach(var p in corners) points.Add(new PointD(p.X, p.Y));
            }
            else if (obj is LaserCircle circ)
            {
                // Approximate with 32 segments
                int segs = 32;
                float rx = circ.Size.Width / 2f;
                float ry = circ.Size.Height / 2f;
                float cx = circ.Position.X + rx;
                float cy = circ.Position.Y + ry;
                
                // Rotation affects the ellipse axis
                var mat = new System.Drawing.Drawing2D.Matrix();
                mat.RotateAt(circ.Rotation, new PointF(cx, cy));
                
                var polyPts = new PointF[segs];
                for(int i=0; i<segs; i++)
                {
                    double ang = i * 2 * Math.PI / segs;
                    polyPts[i] = new PointF(
                        cx + rx * (float)Math.Cos(ang),
                        cy + ry * (float)Math.Sin(ang)
                    );
                }
                mat.TransformPoints(polyPts);
                foreach(var p in polyPts) points.Add(new PointD(p.X, p.Y));
            }
            else if (obj is LaserGroup group)
            {
               // Container-First Approach
               var allChildren = new List<Polygon>();
               CollectPolygons(group, new System.Drawing.Drawing2D.Matrix(), allChildren);
               
               if (allChildren.Count == 0) return null;
               
               // 1. Sort by Area Descending to find the "Container" (Base Object)
               allChildren.Sort((a, b) => b.Area.CompareTo(a.Area));
               
               var primary = allChildren[0];
               var keptChildren = new List<Polygon>();
               
               // 2. Filter remaining objects
               for (int i = 1; i < allChildren.Count; i++)
               {
                   var candidate = allChildren[i];
                   // If candidate is inside the Primary, strictly ignore it (it's a hole/detail)
                   // If it's outside, it's a disjoint part of the group, so keep it.
                   if (!GeometryHelpers.IsPolygonInside(candidate, primary))
                   {
                        // Check redundancy against already kept children? 
                        // For now, simple primary check is sufficient for "Base Object" request.
                        keptChildren.Add(candidate);
                   }
               }
               
               // 3. Construct Result
               // We reuse 'primary' as the main polygon representing the group
               primary.Children.AddRange(keptChildren);
               primary.Tag = obj; // IMPORTANT: Link back to the GROUP, not the child part
               
               if (keptChildren.Count > 0) primary.RecomputeBounds();
               
               return primary;
            }
            // Add return null for unsupported types or check logic
            else if (obj == null) return null;
            else
            {
               // Image or Text -> treat as Box
                float x = obj.Position.X;
                float y = obj.Position.Y;
                float w = obj.Size.Width;
                float h = obj.Size.Height;
                 var corners = new PointF[] {
                    new PointF(x, y),
                    new PointF(x + w, y),
                    new PointF(x + w, y + h),
                    new PointF(x, y + h)
                };
                
                var mat = new System.Drawing.Drawing2D.Matrix();
                float cx = x + w / 2f;
                float cy = y + h / 2f;
                mat.RotateAt(obj.Rotation, new PointF(cx, cy));
                mat.TransformPoints(corners);
                 foreach(var p in corners) points.Add(new PointD(p.X, p.Y));
            }

            return points.Count > 2 ? new Polygon(points) { Tag = obj } : null;
        }

        private void CollectPolygons(LaserObject obj, System.Drawing.Drawing2D.Matrix parentMat, List<Polygon> collector)
        {
            if (obj is LaserGroup group)
            {
                 foreach(var child in group.Children)
                 {
                     CollectPolygons(child, parentMat, collector);
                 }
            }
            else
            {
                // Convert simple object
                var poly = ConvertToPolygon(obj);
                if (poly != null && poly.Points.Count > 2) 
                {
                    collector.Add(poly);
                }
            }
        }

        private struct GridPoint
        {
            public double X, Y, DistSq;
            public GridPoint(double x, double y)
            {
                X = x; Y = y;
                DistSq = x * x + y * y; // From 0,0
            }
        }
        
        private class PolyWrapper
        {
            public Polygon Polygon { get; set; } = default!;
            public LaserObject Original { get; set; } = default!;
        }
    }
}
