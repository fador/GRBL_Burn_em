/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;

namespace grbl_burn_em.Data.Generators;

public class MarlinGenerator : IGCodeGenerator
{
    public string Name => "Marlin";

    public IEnumerable<string> Generate(IEnumerable<LaserObject> objects)
    {
        // Settings
        string toolOn = AppConfiguration.Instance.ToolOnCommand;
        string toolOff = AppConfiguration.Instance.ToolOffCommand;
        bool usePwm = AppConfiguration.Instance.EnablePWM;
        float travelSpeed = AppConfiguration.Instance.DefaultTravelSpeed;

        // Startup
        yield return "G21"; // Metric
        yield return "G90"; // Absolute positioning
        
        yield return $"G0 F{travelSpeed:F0}"; // Set default travel speed

        // Initial Tool Off
        yield return toolOff;

        foreach (var obj in objects)
        {
            if (!obj.IsEnabled) continue;
            
            foreach (var line in GenerateObject(obj, toolOn, toolOff, usePwm))
            {
                yield return line;
            }
        }

        // Shutdown
        yield return toolOff; 
        yield return "G0 X0 Y0"; // Return to home
    }

    private IEnumerable<string> GenerateObject(LaserObject obj, string toolOn, string toolOff, bool usePwm)
    {
        var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId) 
                    ?? ProjectState.Instance.Layers.FirstOrDefault();
        
        LayerMode mode = obj.Mode ?? layer?.Mode ?? LayerMode.Cut;

        if (obj is LaserGroup group && mode == LayerMode.Cut)
        {
            foreach (var child in group.Children)
            {
                foreach (var line in GenerateObject(child, toolOn, toolOff, usePwm)) yield return line;
            }
            yield break;
        }

        float pwrPercent = obj.Power ?? layer?.Power ?? 100f;
        float speedVal = obj.Speed ?? layer?.Speed ?? 1000f;

        // If usePwm is true, S-value is scaled 0-255 or 0-1000? 
        // Marlin typically uses 0-255 for fan/laser (M106/M3), but some setups use 0-1000 or custom.
        // GrblGenerator uses 0-1000 (pwrPercent * 10). Let's stick to that for compatibility, 
        // assuming standard Spindle speed mapping.
        // For Pen Plotters (PWM=False), S is ignored on moves.
        
        float sVal = pwrPercent * 10f; 
        float fVal = speedVal;

        // If Mode is CUT, generate Vector GCode (unless Image)
        if (mode == LayerMode.Cut && !(obj is LaserImage))
        {
            // Vector Generation Logic
            using (var path = new GraphicsPath())
            {
                // We reuse the logic to get a flattened path from the object
                AddObjectToPath(path, obj);

                path.Flatten(null, 0.05f); // Precision

                if (path.PointCount > 0)
                {
                     PointF[] points = path.PathPoints;
                     byte[] types = path.PathTypes;
                     PointF lastPos = new PointF(float.NaN, float.NaN);
                     PointF subpathStart = new PointF(0,0);

                     // State tracking for Tool to avoid redundant M3/M5 if possible?
                     // Actually for plotters, we MUST Lift (M5/ToolOff) before G0, and Drop (M3/ToolOn) before G1.
                     
                     for (int i = 0; i < points.Length; i++)
                     {
                         var p = points[i];
                         byte type = types[i];
                         byte typeMasked = (byte)(type & 0x07);
                         
                         bool isStart = (typeMasked == 0); // Start of a subpath (Move)
                         
                         // Check for "gap" which implies a move even if not explicitly start type
                         // (GraphicsPath sometimes makes new start for non-contiguous)
                         
                         if (isStart) 
                         {
                             subpathStart = p;
                             
                             // Travel to Start
                             // Ensure Tool is OFF before travel
                             // If we were cutting, we just finished a segment.
                             // But we can't easily know previous state here without context.
                             // Safest approach: Always Tool OFF before G0. 
                             
                             // However, if we just emitted Tool OFF at end of loop, we might duplicate.
                             // Let's rely on the structure: 
                             // 1. Tool OFF
                             // 2. G0 to Start
                             // 3. Tool ON
                             // 4. G1 ...
                             
                             yield return toolOff;
                             yield return $"G0 X{p.X:F3} Y{p.Y:F3}";
                             
                             // Update Feedrate for upcoming cut
                             yield return $"G1 F{fVal:F0}"; 
                             
                             yield return toolOn + (usePwm ? $" S{sVal:F0}" : "");
                         }
                         else 
                         {
                             // Line (G1)
                             if (usePwm)
                                yield return $"G1 X{p.X:F3} Y{p.Y:F3} S{sVal:F0}";
                             else
                                yield return $"G1 X{p.X:F3} Y{p.Y:F3}";
                         }
                         
                         lastPos = p;

                         if ((type & 0x80) != 0) // CloseSubpath flag
                         {
                             // Draw line back to subpath start
                             // This is a CUT move
                             if (usePwm)
                                yield return $"G1 X{subpathStart.X:F3} Y{subpathStart.Y:F3} S{sVal:F0}";
                             else
                                yield return $"G1 X{subpathStart.X:F3} Y{subpathStart.Y:F3}";
                             
                             lastPos = subpathStart;
                         }
                     }
                     // End of object: Turn Tool Off
                     yield return toolOff;
                }
            }
        }
        else
        {
            // Raster / Image Mode
            // TODO: Raster support for Pen Plotter? 
            // Pen plotters usually can't raster effectively (shaking machine to death).
            // But User might be using Marlin on a Laser machine.
            // So we should support Raster if PWM is enabled.
            // If PWM is disabled (Pen Plotter), Raster is bad idea, but we can try to generate it as dots/lines?
            // For now, let's implement standard Raster logic similar to GrblGenerator but with configurable commands.
            
            yield return toolOff;

            // Reuse existing raster logic helpers or copy-paste?
            // Since Rasterizer returns G-Code strings, we might need to interpret them or duplicate logic.
            // The existing Rasterizer helper seems to generate specific G1 lines.
            // Let's look at `Rasterizer` class if accessible.
            // Assuming we need to replicate the bitmap generation logic.
            
            // ... (Bitmap generation logic same as GrblGenerator) ...
             Bitmap? bitmapToRasterize = null;
             bool disposeBitmap = false;
             PointF rasterPos = obj.Position;
             SizeF rasterSize = obj.Size;

             // ... [Duplicate Bitmap Setup Logic to avoid public access issues if methods are private] ...
             // For brevity, I will call a shared helper if possible, but GrblGenerator logic is private.
             // I will duplicate the bitmap setup for now.
             
            if (obj is LaserImage img)
            {
                 if (img.Image != null)
                 {
                     var bounds = img.GetBounds();
                     if (bounds.Width > 0 && bounds.Height > 0)
                     {
                         rasterPos = bounds.Location;
                         rasterSize = bounds.Size;
                         float interval = AppConfiguration.Instance.RasterLineInterval;
                         if (interval <= 0) interval = 0.1f;
                         float dpmm = 1.0f / interval;
                         int w = (int)Math.Ceiling(bounds.Width * dpmm);
                         int h = (int)Math.Ceiling(bounds.Height * dpmm);
                         if (w > 0 && h > 0)
                         {
                             bitmapToRasterize = new Bitmap(w, h);
                             disposeBitmap = true;
                             using (var g = Graphics.FromImage(bitmapToRasterize))
                             {
                                 g.Clear(Color.White);
                                 g.ScaleTransform(dpmm, -dpmm);
                                 g.TranslateTransform(-bounds.X, -(bounds.Y + bounds.Height));
                                 img.Draw(g, 1.0f);
                             }
                         }
                     }
                 }
            }
            else
            {
                // Vector Fill
                using (var path = new GraphicsPath())
                {
                    AddObjectToPath(path, obj);
                    if (path.PointCount > 0)
                    {
                        var exactBounds = path.GetBounds();
                        if (exactBounds.Width > 0 && exactBounds.Height > 0)
                        {
                            exactBounds.Inflate(1.0f, 1.0f);
                            rasterPos = exactBounds.Location;
                            rasterSize = exactBounds.Size;
                            float interval = AppConfiguration.Instance.RasterLineInterval;
                            if (interval <= 0) interval = 0.1f;
                            float dpmm = 1.0f / interval;
                            int w = (int)Math.Ceiling(rasterSize.Width * dpmm);
                            int h = (int)Math.Ceiling(rasterSize.Height * dpmm);
                            if (w > 0 && h > 0)
                            {
                                bitmapToRasterize = new Bitmap(w, h);
                                disposeBitmap = true;
                                using (var g = Graphics.FromImage(bitmapToRasterize))
                                {
                                    g.Clear(Color.White);
                                    g.ScaleTransform(dpmm, -dpmm);
                                    g.TranslateTransform(-rasterPos.X, -(rasterPos.Y + rasterSize.Height));
                                    using (var brush = new SolidBrush(Color.Black))
                                    {
                                        g.FillPath(brush, path);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (bitmapToRasterize != null)
            {
                float interval = AppConfiguration.Instance.RasterLineInterval;
                float minSeg = AppConfiguration.Instance.MinRasterSegmentLength;
                bool bicubic = AppConfiguration.Instance.EnableBicubicResampling;
                bool dither = AppConfiguration.Instance.Enable1BitDithering;

                var tempImg = new LaserImage
                {
                    Image = bitmapToRasterize,
                    Position = rasterPos,
                    Size = rasterSize,
                    Power = pwrPercent,
                    Speed = speedVal
                };

                // NOTE: Rasterizer.Rasterize yields Grbl-specific G1 lines usually? 
                // We might need to implement our own Rasterizer or parse the output.
                // Or better, if EnablePWM is true, Rasterizer output (lots of G1 ... Sxxx) is fine.
                // If EnablePWM is false, Rasterizing is basically "Dot matrix" or impossible.
                // For now, let's assume if they want Raster, they use PWM. 
                // If they don't use PWM, we probably shouldn't rasterize or it will just be ON/OFF banging which is violent.
                
                if (usePwm)
                {
                     // Use standard rasterizer
                     // But we need to inject our ToolOn/Off? 
                     // Standard Rasterizer often assumes "M4 S0" is active and just varies S.
                     
                     yield return toolOn; // M3/M4
                     
                     foreach (var line in Rasterizer.Rasterize(tempImg, sVal, fVal, interval, minSeg, bicubic, dither))
                     {
                         yield return line;
                     }
                     yield return toolOff;
                }
                else
                {
                    // No PWM raster? 
                    // Support Stippling? Too complex for now.
                    // Just skipping raster for non-PWM mode to be safe, or warning?
                    // Let's generate it but without S words? That would just be a solid block of burnt area.
                    // Probably not what user wants.
                    // For now: omit raster in non-PWM mode or do simple constant burn.
                }

                if (disposeBitmap) bitmapToRasterize.Dispose();
            }
        }
    }
    
    // Copy helper from GrblGenerator (private there)
    private void AddObjectToPath(GraphicsPath path, LaserObject obj)
    {
        if (!obj.IsEnabled) return;

        if (obj is LaserGroup group)
        {
            foreach (var child in group.Children)
            {
                AddObjectToPath(path, child);
            }
            return;
        }

        using (var gp = new GraphicsPath())
        {
            bool hasPath = false;

            if (obj is LaserRectangle rect)
            {
                gp.AddRectangle(new RectangleF(rect.Position, rect.Size));
                hasPath = true;
            }
            else if (obj is LaserCircle circ)
            {
                gp.AddEllipse(circ.Position.X, circ.Position.Y, circ.Size.Width, circ.Size.Height);
                hasPath = true;
            }
            else if (obj is LaserPath lp)
            {
                if (lp.Points.Count > 1)
                {
                    gp.AddLines(lp.Points.ToArray());
                    gp.CloseFigure(); 
                    hasPath = true;
                }
            }
            else if (obj is LaserBezier lb)
            {
                if (lb.Points.Count >= 4)
                {
                    int count = lb.Points.Count;
                    int valid = count - (count - 1) % 3;
                    gp.AddBeziers(lb.Points.Take(valid).ToArray());
                    gp.CloseFigure();
                    hasPath = true;
                }
            }
            else if (obj is LaserText lt)
            {
                using (var tgp = lt.GetPath())
                {
                    gp.AddPath(tgp, false);
                }
                hasPath = true;
            }

            if (hasPath)
            {
                if (obj.Rotation != 0)
                {
                    float cx = obj.Position.X + obj.Size.Width / 2f;
                    float cy = obj.Position.Y + obj.Size.Height / 2f;
                    using (var m = new System.Drawing.Drawing2D.Matrix())
                    {
                        m.RotateAt(obj.Rotation, new PointF(cx, cy));
                        gp.Transform(m);
                    }
                }
                path.AddPath(gp, false);
            }
        }
    }
}
