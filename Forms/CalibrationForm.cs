/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using System.Windows.Forms;
using grbl_burn_em.Data;
using System.Collections.Generic;

namespace grbl_burn_em.Forms
{
    public class CalibrationForm : Form
    {
        private PictureBox _pbCamera = null!;
        private Label _lblInstruction = null!;
        private List<PointF> _imagePoints = new List<PointF>();
        private Bitmap _currentFrame = null!;
        
        public PointF[] SelectedPoints => _imagePoints.ToArray();

        public CalibrationForm()
        {
            InitializeComponent();
            CameraManager.Instance.FrameReceived += OnFrameReceived;
        }

        private void InitializeComponent()
        {
            this.Size = new Size(800, 600);
            this.Text = "Camera Calibration - Click 4 Points (Corners of Bed)";
            
            _lblInstruction = new Label { Dock = DockStyle.Top, Height = 40, Text = "Step 1: Click 4 points on the camera image corresponding to known world locations (e.g. bed corners).", Font = new Font(FontFamily.GenericSansSerif, 12) };
            this.Controls.Add(_lblInstruction);
            
            _pbCamera = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            _pbCamera.MouseClick += OnCameraClick;
            this.Controls.Add(_pbCamera);
            
            this.FormClosing += (s, e) => CameraManager.Instance.FrameReceived -= OnFrameReceived;
        }

        private void OnFrameReceived(Bitmap bmp)
        {
            try
            {
                var copy = new Bitmap(bmp);
                this.Invoke(new Action(() => 
                {
                    var old = _pbCamera.Image;
                    _pbCamera.Image = copy;
                    old?.Dispose();
                    _currentFrame = copy; // Keep ref for coordinates
                }));
            }
            catch {}
        }

        private void OnCameraClick(object? sender, MouseEventArgs e)
        {
            if (_imagePoints.Count >= 4) return;
            
            // Map Mouse Coords to Image Coords
            if (_pbCamera.Image == null) return;
            
            // PictureBox Zoom Mode coordinate mapping is tricky.
            // Simplified: Assume Image fills view or use center? 
            // Better: Use Normal SizeMode inside a AutoScroll Panel? 
            // Or Calculate Zoom ratio.
            
            float imageAspect = (float)_pbCamera.Image.Width / _pbCamera.Image.Height;
            float clientAspect = (float)_pbCamera.Width / _pbCamera.Height;
            
            float scale;
            float offsetX = 0;
            float offsetY = 0;
            
            if (imageAspect > clientAspect)
            {
                // Widther than tall - Fit Width
                scale = (float)_pbCamera.Width / _pbCamera.Image.Width;
                offsetY = (_pbCamera.Height - _pbCamera.Image.Height * scale) / 2;
            }
            else
            {
                // Taller than wide - Fit Height
                scale = (float)_pbCamera.Height / _pbCamera.Image.Height;
                offsetX = (_pbCamera.Width - _pbCamera.Image.Width * scale) / 2;
            }
            
            float imgX = (e.X - offsetX) / scale;
            float imgY = (e.Y - offsetY) / scale;
            
            if (imgX >= 0 && imgX < _pbCamera.Image.Width && imgY >= 0 && imgY < _pbCamera.Image.Height)
            {
                _imagePoints.Add(new PointF(imgX, imgY));
                
                using (var g = Graphics.FromImage(_pbCamera.Image))
                {
                    g.FillEllipse(Brushes.Red, imgX-5, imgY-5, 10, 10);
                    g.DrawString(_imagePoints.Count.ToString(), SystemFonts.DefaultFont, Brushes.Yellow, imgX+5, imgY+5);
                }
                _pbCamera.Invalidate();
                
                if (_imagePoints.Count == 4)
                {
                    _lblInstruction.Text = "Points Selected. Close this window to proceed to World Point Selection.";
                    MessageBox.Show("4 Points Selected. Click OK to continue to Workbench Selection.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
        }
    }
}
