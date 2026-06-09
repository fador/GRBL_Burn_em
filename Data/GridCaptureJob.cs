/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using grbl_burn_em.Controls;

namespace grbl_burn_em.Data
{
    public class GridCaptureJob
    {
        private float _overlap = 0.2f; // 20% overlap
        private AppConfiguration _config;
        private bool _cancel = false;
        
        // public event Action<int, int> ProgressChanged;
        public event Action? Finished;

        public GridCaptureJob()
        {
            _config = AppConfiguration.Instance;
        }

        public async Task Start(float w, float h)
        {
            // 1. Calculate Field of View from Calibration (or Estimate)
            // We need "Pixels per mm" or "World Width of Camera View".
            // Frame.Width (mm) = Frame.Width(px) * Scale.
            // If we don't have Scale, we can't scan efficiently.
            // Assumed Scale or User Input?
            // "Head Mounted" usually implies we know the Scale or Height.
            
            // Fallback: Use Manual Overlay W/H as the "Field of View" size in mm.
            float fovW = _config.CameraOverlayWidth;
            float fovH = _config.CameraOverlayHeight;
            
            if (fovW <= 10 || fovH <= 10) 
            {
                MessageBox.Show("Camera Field of View not defined. Please set Overlay Width/Height to approximate real world MM size.", "Error");
                return;
            }

            // Calculate Step Sizes
            float stepX = fovW * (1.0f - _overlap);
            float stepY = fovH * (1.0f - _overlap);
            
            // Calculate Grid
            // Scan from (0,0) to (w, h)
            // Center of Camera is what moves.
            // Limits are 0..WorkAreaW, 0..WorkAreaH.
            
            // If Camera is physically offset from laser, we must account for it?
            // The Machine moves the LASER.
            // We want the CAMERA to cover the area.
            // Camera = Laser - Offset.
            // To put Camera at (X,Y), Laser must be at (X + OffsetX, Y + OffsetY).
            
            float offX = _config.CameraOverlayX; // Or Calibration.OffsetX
            float offY = _config.CameraOverlayY;
            
            // Grid Generation
            // We want Camera Centers at:
            // X: fovW/2, fovW/2 + stepX, ... until W - fovW/2
            // Y: fovH/2, fovH/2 + stepY, ...
            
            var points = new List<System.Drawing.PointF>();
            for (float y = fovH / 2; y < h + fovH/2; y += stepY)
            {
                if (y > h) y = h - fovH/2; // Clamp last row?

                for (float x = fovW / 2; x < w + fovW/2; x += stepX)
                {
                    if (x > w) x = w - fovW/2;
                    points.Add(new System.Drawing.PointF(x + offX, y + offY)); // Laser Target
                }
            }
            
            // Execute
            CameraManager.Instance.CapturedFrames.Clear();
            
            foreach (var pt in points)
            {
                if (_cancel) break;

                // Move
                // Use SerialInterface to Jog? Or G0?
                // G0 is better for exact positioning.
                string cmd = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"$J=G90 X{pt.X:F2} Y{pt.Y:F2} F{_config.FramingSpeed}");
                // Using G0 requires getting out of other states.
                // $J is good.
                
                SerialInterface.Instance.Write(cmd + "\n");
                
                // Wait for Idle
                // How do we know when we are there?
                // Monitor MachinePosition vs Target?
                // Or Wait for 'Idle' state.
                
                await WaitForPosition(pt.X, pt.Y);
                await Task.Delay(500); // Settle time
                
                // Capture
                CameraManager.Instance.CaptureCurrentFrame(pt.X - offX, pt.Y - offY, fovW, fovH);
            }
            
            Finished?.Invoke();
        }
        
        private async Task WaitForPosition(float targetX, float targetY)
        {
            // Simple timeout based wait loop
            int timeout = 10000; // 10 sec
            while (timeout > 0)
            {
                var pos = SerialInterface.Instance.MachinePosition;
                float dist = (float)Math.Sqrt(Math.Pow(pos.X - targetX, 2) + Math.Pow(pos.Y - targetY, 2));
                if (dist < 1.0f) return; // Arrived
                
                await Task.Delay(100);
                timeout -= 100;
            }
        }
        
        public void Cancel()
        {
            _cancel = true;
        }
    }
}
