/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;
using grbl_burn_em.Data;
using grbl_burn_em.Tools;
using grbl_burn_em.Data.Commands;
using grbl_burn_em.Forms;
using System.ComponentModel;

namespace grbl_burn_em.Controls
{
    public partial class WorkbenchControl
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Apply transformations
            g.TranslateTransform(Width / 2f + _panOffset.X, Height / 2f + _panOffset.Y);
            g.ScaleTransform(_zoom, -_zoom); // Y-Up coordinate system

            DrawCameraBackground(g);
            DrawGrid(g);
            DrawWorkArea(g); // Draw Work Area before Origin
            DrawOrigin(g);
            DrawObjects(g);
            DrawLaserPosition(g);
            DrawCameraOverlay(g);
            DrawRulerOverlay(g);
        }

        private void DrawWorkArea(Graphics g)
        {
            float w = AppConfiguration.Instance.WorkAreaWidth;
            float h = AppConfiguration.Instance.WorkAreaHeight;
            string origin = AppConfiguration.Instance.WorkOrigin;
            
            float x = 0;
            float y = 0;
            
            if (origin == "BottomLeft")
            {
                 // 0,0 is Bottom Left. Box extends Right (+X) and Up (+Y).
                 x = 0;
                 y = 0; 
            }
            else if (origin == "TopLeft")
            {
                 // 0,0 is Top Left. Box extends Right (+X) and Down (-Y).
                 x = 0;
                 y = -h;
            }
            else if (origin == "Center")
            {
                 x = -w / 2;
                 y = -h / 2;
            }
            
            using var pen = new Pen(Color.Black, 3.0f / _zoom);
            g.DrawRectangle(pen, x, y, w, h);
        }

        private void DrawCameraBackground(Graphics g)
        {
            var config = AppConfiguration.Instance;
            
            // 1. Draw Composite/Scanned Images (Head Mounted Backlog)
            if (config.CameraIsMounted)
            {
                 var frames = CameraManager.Instance.CapturedFrames;
                 // Lock and Iterate
                 // Using a Copy or Lock to avoid collection was modified?
                 // CameraManager should manage the list thread-safe.
                 var list = frames.ToList(); // Shallow copy of list
                 
                 if (list.Count > 0)
                 {
                     float opacity = config.CameraOverlayOpacity;
                     if (opacity <= 0) return;
                     
                     System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix();
                     cm.Matrix33 = opacity;
                     using var ia = new System.Drawing.Imaging.ImageAttributes();
                     ia.SetColorMatrix(cm);

                     foreach(var frame in list)
                     {
                         try 
                         {
                             // Frame.WorldX/Y is Center of Camera
                             // Frame.Width/Height is FOV size
                             // Top-Left:
                             float x = frame.WorldX - frame.Width / 2;
                             float y = frame.WorldY + frame.Height / 2; // Y-Up: Top is Y+H/2
                             
                             PointF[] destPoints = {
                                 new PointF(x, y),                 // UL (World Top-Left)
                                 new PointF(x + frame.Width, y),   // UR
                                 new PointF(x, y - frame.Height)   // DL (World Bottom-Left)
                             };
                             
                             g.DrawImage(frame.Image, destPoints, 
                                 new RectangleF(0, 0, frame.Image.Width, frame.Image.Height),
                                 GraphicsUnit.Pixel, ia);
                         }
                         catch {}
                     }
                 }
            }
        }

        private void DrawCameraOverlay(Graphics g)
        {
            var config = AppConfiguration.Instance;

            // 2. Draw Live Overlay (Stationary or Single Frame Mounted)
            if (OverlayImage != null && (!config.CameraIsMounted || config.ShowCameraOverlay)) // Show Live even if mounted? Yes, usually.
            {
                 // Create ColorMatrix
                 float opacity = OverlayImageOpacity;
                 if (opacity < 0) opacity = 0;
                 if (opacity > 1) opacity = 1;
                 
                 System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix();
                 cm.Matrix33 = opacity;
                 
                 using var ia = new System.Drawing.Imaging.ImageAttributes();
                 ia.SetColorMatrix(cm);
                 
                 // Dest Rect
                 float x = config.CameraOverlayX;
                 float y = config.CameraOverlayY;
                 float w = config.CameraOverlayWidth;
                 float h = config.CameraOverlayHeight;
                 
                 PointF[] destPoints = {
                     new PointF(x, y),         // UL (Upper Left of source maps here) -> World Top-Left
                     new PointF(x + w, y),     // UR -> World Top-Right
                     new PointF(x, y - h)      // DL -> World Bottom-Left
                 };
                 
                 try
                 {
                     if (OverlayImage == null) return;
                     
                     g.DrawImage(OverlayImage, destPoints, 
                         new RectangleF(0, 0, OverlayImage.Width, OverlayImage.Height),
                         GraphicsUnit.Pixel, ia);
                 }
                 catch
                 {
                     // Image might be disposed during paint
                 }
            }
        }

        private void DrawGrid(Graphics g)
        {
            var pen = new Pen(Color.LightGray, 1.0f / _zoom);
            
            int lines = 100;
            float step = GridStep; // 10.0 world units

            for (int i = -lines; i <= lines; i++)
            {
                g.DrawLine(pen, i * step, -lines * step, i * step, lines * step);
                g.DrawLine(pen, -lines * step, i * step, lines * step, i * step);
            }
        }

        private void DrawOrigin(Graphics g)
        {
            var pen = new Pen(Color.Red, 2.0f / _zoom);
            float len = 10.0f;
            g.DrawLine(pen, -len, 0, len, 0); // X axis
            g.DrawLine(pen, 0, -len, 0, len); // Y axis
        }

        private void DrawObjects(Graphics g)
        {
            foreach (var obj in ProjectState.Instance.Objects)
            {
                if (!obj.IsEnabled) continue;
                
                var state = g.Save();
                obj.Draw(g, _zoom);
                
                // Draw selection highlight
                if (ProjectState.Instance.SelectedObjects.Contains(obj))
                {
                    using var selPen = new Pen(Color.Cyan, 2.0f / _zoom);
                    selPen.DashStyle = DashStyle.Dash;
                    if (obj is LaserRectangle rect)
                    {
                        g.DrawRectangle(selPen, rect.Position.X, rect.Position.Y, rect.Size.Width, rect.Size.Height);
                    }
                    else if (obj is LaserPath path && path.Points.Count > 1)
                    {
                        g.DrawLines(selPen, path.Points.ToArray());
                    }
                }
                
                g.Restore(state);
            }

            // Draw selection box interaction
            if (_isSelecting && ToolManager.Instance.CurrentTool == ToolType.Select)
            {
                 // Normalizing rect for drawing
                 float x = Math.Min(_dragStartPos.X, _currentMouseWorld.X);
                 float y = Math.Min(_dragStartPos.Y, _currentMouseWorld.Y);
                 float w = Math.Abs(_currentMouseWorld.X - _dragStartPos.X);
                 float h = Math.Abs(_currentMouseWorld.Y - _dragStartPos.Y);
                 
                 using var boxBrush = new SolidBrush(Color.FromArgb(50, Color.Cyan));
                 using var boxPen = new Pen(Color.Cyan, 1.0f / _zoom);
                 g.FillRectangle(boxBrush, x, y, w, h);
                 g.DrawRectangle(boxPen, x, y, w, h);
            }
            
            // Draw Resize Handles
            if (ToolManager.Instance.CurrentTool == ToolType.Select && ProjectState.Instance.SelectedObjects.Count == 1)
            {
                 var obj = ProjectState.Instance.SelectedObjects[0];
                 if (obj is LaserBezier bezier)
                 {
                     DrawNodeHandles(g, bezier);
                 }

                 var bounds = GetSelectionBounds();
                 if (bounds != null)
                 {
                     using var boundaryPen = new Pen(Color.Cyan, 1.0f / _zoom);
                     boundaryPen.DashStyle = DashStyle.Solid;
                     g.DrawRectangle(boundaryPen, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height);
                     DrawResizeHandles(g, bounds.Value);
                 }
            }
            else if (ProjectState.Instance.SelectedObjects.Count > 0)
            {
                 var bounds = GetSelectionBounds();
                 if (bounds != null)
                 {
                     using var boundaryPen = new Pen(Color.Cyan, 1.0f / _zoom);
                     boundaryPen.DashStyle = DashStyle.Solid;
                     g.DrawRectangle(boundaryPen, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height);
                     DrawResizeHandles(g, bounds.Value);
                 }
            }
        }

        private void DrawLaserPosition(Graphics g)
        {
            float x = _laserPosition.X;
            float y = _laserPosition.Y;
            
            // Size in pixels
            float size = 10.0f / _zoom;
            
            using var pen = new Pen(Color.Red, 2.0f / _zoom);
            // X
            g.DrawLine(pen, x - size, y - size, x + size, y + size);
            g.DrawLine(pen, x - size, y + size, x + size, y - size);
            
            // Circle
            g.DrawEllipse(pen, x - size, y - size, size * 2, size * 2);
        }

        private void DrawRulerOverlay(Graphics g)
        {
            if (ToolManager.Instance.CurrentTool == ToolType.Ruler && _isMeasuring)
            {
                 using var pen = new Pen(Color.Red, 2.0f / _zoom);
                 pen.DashStyle = DashStyle.Solid;
                 pen.EndCap = LineCap.ArrowAnchor;
                 pen.StartCap = LineCap.DiamondAnchor;

                 g.DrawLine(pen, _measureStart, _measureEnd);
                 
                 // Draw Text
                 float dist = (float)Math.Sqrt(Math.Pow(_measureEnd.X - _measureStart.X, 2) + Math.Pow(_measureEnd.Y - _measureStart.Y, 2));
                 PointF mid = new PointF((_measureStart.X + _measureEnd.X)/2, (_measureStart.Y + _measureEnd.Y)/2);
                 
                 string text = $"{dist:F1} mm";
                 
                 float fontSize = 12.0f / _zoom;
                 if (fontSize < 0.1f) fontSize = 0.1f; // Safety
                 
                 using var font = new Font("Arial", fontSize);
                 using var bgBrush = new SolidBrush(Color.FromArgb(180, Color.White));
                 using var textBrush = new SolidBrush(Color.DarkBlue);
                 
                 // Calculate size and position
                 var size = g.MeasureString(text, font);
                 float tx = mid.X - size.Width / 2;
                 float ty = mid.Y - size.Height / 2;

                 // Save state to flip back for text
                 var state = g.Save();
                 g.TranslateTransform(tx, ty);
                 g.ScaleTransform(1, -1); // Flip Y back for text
                 
                 // Draw at 0,0 (relative to Translate)
                 g.FillRectangle(bgBrush, 0, 0, size.Width, size.Height);
                 g.DrawString(text, font, textBrush, 0, 0);
                 
                 g.Restore(state);
            }
        }

        private void DrawResizeHandles(Graphics g, RectangleF bounds)
        {
            float size = 8.0f / _zoom; // Constant screen size handles
            using var brush = new SolidBrush(Color.White);
            using var pen = new Pen(Color.Black, 1.0f / _zoom);

            // 8 handles
            PointF[] handles = GetHandlePositions(bounds);
            
            foreach (var h in handles)
            {
                g.FillRectangle(brush, h.X - size/2, h.Y - size/2, size, size);
                g.DrawRectangle(pen, h.X - size/2, h.Y - size/2, size, size);
            }
        }
        
        private PointF[] GetHandlePositions(RectangleF b)
        {
            return new PointF[] {
                new(b.Left, b.Top), // TL
                new(b.Left + b.Width/2, b.Top), // T
                new(b.Right, b.Top), // TR
                new(b.Right, b.Top + b.Height/2), // R
                new(b.Right, b.Bottom), // BR
                new(b.Left + b.Width/2, b.Bottom), // B
                new(b.Left, b.Bottom), // BL
                new(b.Left, b.Top + b.Height/2) // L
            };
        }

        private void DrawNodeHandles(Graphics g, LaserBezier b)
        {
            float size = 6.0f / _zoom;
            using var brushAnchor = new SolidBrush(Color.Blue);
            using var brushControl = new SolidBrush(Color.LightBlue);
            using var penLine = new Pen(Color.Gray, 1.0f / _zoom);
            
            for (int i = 0; i < b.Points.Count; i++)
            {
                var p = b.Points[i];
                bool isAnchor = (i % 3 == 0);
                
                // Draw connection lines
                if (!isAnchor)
                {
                    // Connect to anchor
                    // if i%3 == 1, anchor is i-1
                    // if i%3 == 2, anchor is i+1
                    int anchorIdx = (i % 3 == 1) ? i - 1 : i + 1;
                    if (anchorIdx >= 0 && anchorIdx < b.Points.Count)
                    {
                        g.DrawLine(penLine, p, b.Points[anchorIdx]);
                    }
                }

                var brush = isAnchor ? brushAnchor : brushControl;
                g.FillEllipse(brush, p.X - size/2, p.Y - size/2, size, size);
            }
        }
    }
}
