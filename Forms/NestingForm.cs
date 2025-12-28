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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;
using grbl_burn_em.Data.Geometry;
using grbl_burn_em.Data.Commands;

namespace grbl_burn_em.Forms
{
    public class NestingForm : Form
    {
        private List<LaserObject> _inputObjects;
        private List<Polygon> _visualPolygons = new List<Polygon>();
        private List<NestingManager.NestingResult>? _finalResults = null;
        
        private CancellationTokenSource? _cts;
        private bool _isRunning = false;

        // UI Controls
        private PictureBox _canvas = null!;
        private NumericUpDown _nudSheetW = null!, _nudSheetH = null!;
        private NumericUpDown _nudAlpha = null!, _nudBeta = null!, _nudTheta = null!;
        private Button _btnStart = null!, _btnStop = null!, _btnApply = null!, _btnCancel = null!;
        private ProgressBar _progressBar = null!;
        private Label _lblStatus = null!;

        public NestingForm(List<LaserObject> objects)
        {
            _inputObjects = objects;
            InitializeComponent();
            
            // Default Sheet Size (try to guess or use default)
            _nudSheetW.Value = (decimal)NestingManager.Instance.SheetSize.Width;
            _nudSheetH.Value = (decimal)NestingManager.Instance.SheetSize.Height;
            _nudAlpha.Value = (decimal)NestingManager.Instance.Alpha;
            _nudBeta.Value = (decimal)NestingManager.Instance.Beta;
            _nudTheta.Value = (decimal)NestingManager.Instance.Theta;

            this.FormClosing += NestingForm_FormClosing!;
        }

        private void InitializeComponent()
        {
            this.Text = "Nesting / Packing (Experimental QLM)";
            this.Size = new Size(1000, 700);
            this.MinimumSize = new Size(600, 400);

            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250)); // Settings Panel
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // Canvas

            // Settings Panel
            var pnlSettings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            
            pnlSettings.Controls.Add(new Label { Text = "Sheet Width (mm)", AutoSize = true });
            _nudSheetW = new NumericUpDown { Maximum = 10000, DecimalPlaces = 1, Width = 200 };
            pnlSettings.Controls.Add(_nudSheetW);

            pnlSettings.Controls.Add(new Label { Text = "Sheet Height (mm)", AutoSize = true });
            _nudSheetH = new NumericUpDown { Maximum = 10000, DecimalPlaces = 1, Width = 200 };
            pnlSettings.Controls.Add(_nudSheetH);
            
            pnlSettings.Controls.Add(new Label { Text = "Grid Step Coeff (Alpha)", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
            _nudAlpha = new NumericUpDown { Maximum = 1, Minimum = 0.01m, DecimalPlaces = 2, Increment = 0.01m, Width = 200 };
            pnlSettings.Controls.Add(_nudAlpha);
            
            pnlSettings.Controls.Add(new Label { Text = "Shift Step Coeff (Beta)", AutoSize = true });
            _nudBeta = new NumericUpDown { Maximum = 1, Minimum = 0.01m, DecimalPlaces = 2, Increment = 0.01m, Width = 200 };
            pnlSettings.Controls.Add(_nudBeta);

            pnlSettings.Controls.Add(new Label { Text = "Rotation Step (Theta)", AutoSize = true });
            _nudTheta = new NumericUpDown { Maximum = 180, Minimum = 1, DecimalPlaces = 0, Width = 200 };
            pnlSettings.Controls.Add(_nudTheta);

            _btnStart = new Button { Text = "Start Packing", Height = 40, Width = 200, Margin = new Padding(0, 20, 0, 0), BackColor = Color.LightGreen };
            _btnStart.Click += (s, e) => StartNesting();
            pnlSettings.Controls.Add(_btnStart);

            _btnStop = new Button { Text = "Stop", Height = 40, Width = 200, Enabled = false, BackColor = Color.LightSalmon };
            _btnStop.Click += (s, e) => StopNesting();
            pnlSettings.Controls.Add(_btnStop);

            _btnApply = new Button { Text = "Apply Results", Height = 40, Width = 200, Margin = new Padding(0, 20, 0, 0), Enabled = false };
            _btnApply.Click += (s, e) => ApplyResults();
            pnlSettings.Controls.Add(_btnApply);

            _btnCancel = new Button { Text = "Close", Height = 30, Width = 200 };
            _btnCancel.Click += (s, e) => this.Close();
            pnlSettings.Controls.Add(_btnCancel);
            
            _lblStatus = new Label { Text = "Ready", AutoSize = true, Font = new Font(this.Font, FontStyle.Italic) };
            pnlSettings.Controls.Add(_lblStatus);
            
            _progressBar = new ProgressBar { Width = 200, Height = 20, Style = ProgressBarStyle.Continuous };
            pnlSettings.Controls.Add(_progressBar);

            mainLayout.Controls.Add(pnlSettings, 0, 0);

            // Canvas
            _canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke, BorderStyle = BorderStyle.Fixed3D };
            _canvas.Paint += Canvas_Paint!;
            _canvas.Resize += (s, e) => _canvas.Invalidate();
            mainLayout.Controls.Add(_canvas, 1, 0);

            this.Controls.Add(mainLayout);
        }

        private async void StartNesting()
        {
            if (_isRunning) return;
            _isRunning = true;
            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _btnApply.Enabled = false;
            _visualPolygons.Clear();
            
            // settings
            var config = NestingManager.Instance;
            config.SheetSize = new SizeF((float)_nudSheetW.Value, (float)_nudSheetH.Value);
            config.Alpha = (double)_nudAlpha.Value;
            config.Beta = (double)_nudBeta.Value;
            config.Theta = (double)_nudTheta.Value;

            _cts = new CancellationTokenSource();

            // Hook Event
            NestingManager.Instance.OnPartPlaced += OnPartUpdate;
            NestingManager.Instance.ProgressChanged += OnProgress;

            try
            {
                _lblStatus.Text = "Packing...";
                if (_cts != null)
                {
                    _finalResults = await NestingManager.Instance.RunNesting(_inputObjects, _cts.Token);
                }
                
                if (_finalResults != null && (_cts == null || !_cts.IsCancellationRequested))
                {
                    _lblStatus.Text = $"Completed! Placed {_finalResults.Count}/{_inputObjects.Count} objects.";
                    _btnApply.Enabled = true;
                }
                else
                {
                    _lblStatus.Text = "Stopped / Cancelled.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                _lblStatus.Text = "Error";
            }
            finally
            {
                NestingManager.Instance.OnPartPlaced -= OnPartUpdate;
                NestingManager.Instance.ProgressChanged -= OnProgress;
                _isRunning = false;
                _btnStart.Enabled = true;
                _btnStop.Enabled = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void StopNesting()
        {
            _cts?.Cancel();
        }

        private void ApplyResults()
        {
            if (_finalResults == null) return;
            
            // Create a Command to apply changes (Undo support would be nice, but for now direct)
            // Ideally we wrap this in a Command.
            // Let's modify the objects directly.
            var cmd = new NestingApplyCommand();
            
            foreach (var res in _finalResults)
            {
                var obj = res.OriginalObject;
                if (obj is LaserPath path)
                {
                    var newPoints = new List<PointF>();
                    foreach(var p in res.PlacedPolygon.Points) newPoints.Add(new PointF((float)p.X, (float)p.Y));
                    
                    cmd.AddChange(path, path.Position, 0, newPoints);
                }
                else if (obj is LaserGroup group)
                {
                    // Group: Apply transform to all children recursively
                    var oldBounds = group.GetBounds();
                    float cx = oldBounds.X + oldBounds.Width / 2f;
                    float cy = oldBounds.Y + oldBounds.Height / 2f;
                    var pivot = new PointF(cx, cy);
                    
                    var newCenter = res.PlacedPolygon.Centroid;
                    var shift = new PointF((float)newCenter.X - cx, (float)newCenter.Y - cy);
                    float angle = (float)res.Rotation;
                    
                    ApplyTransformRecursive(group, pivot, angle, shift, cmd);
                }
                else
                {
                    // Primitives
                    float newRotation = (float)res.Rotation;
                    
                    // Center alignment logic (as before)
                    var oldPos = obj.Position;
                    float oldRot = obj.Rotation;
                                        
                    var currentBounds = obj.GetBounds(); 
                    float currentCX = currentBounds.X + currentBounds.Width / 2f;
                    float currentCY = currentBounds.Y + currentBounds.Height / 2f;
                    
                    var targetBounds = res.PlacedPolygon.Bounds;
                    float targetCX = (float)(targetBounds.MinX + targetBounds.Width / 2.0);
                    float targetCY = (float)(targetBounds.MinY + targetBounds.Height / 2.0);
                    
                    float dx = targetCX - currentCX;
                    float dy = targetCY - currentCY;
                    
                    var newPos = new PointF(obj.Position.X + dx, obj.Position.Y + dy);
                    
                    cmd.AddChange(obj, newPos, newRotation);
                }
            }
            
            // Execute via CommandManager
            CommandManager.Instance.Execute(cmd);
            
            // Notify Main
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ApplyTransformRecursive(LaserObject obj, PointF pivot, float angle, PointF translation, NestingApplyCommand cmd)
        {
            if (obj is LaserGroup group)
            {
                foreach(var child in group.Children)
                {
                    ApplyTransformRecursive(child, pivot, angle, translation, cmd);
                }
            }
            else
            {
                // Rotate around pivot
                // 1. Translate to origin relative to pivot
                float dx = obj.Position.X - pivot.X;
                float dy = obj.Position.Y - pivot.Y;
                
                // 2. Rotate
                double rad = angle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                
                float rx = (float)(dx * cos - dy * sin);
                float ry = (float)(dx * sin + dy * cos);

                // 3. Translate back + Shift
                var newPos = new PointF(pivot.X + rx + translation.X, pivot.Y + ry + translation.Y);
                var newRot = obj.Rotation + angle;
                
                List<PointF>? newPoints = null;

                if (obj is LaserPath path)
                {
                     // Special handling for LaserPath to bake transforms
                     // LaserPath uses Absolute Points + Rotation. To move it orbitally, we must move the points.
                     // And since we rotate the points, we should ideally bake the original rotation too or handle it carefully.
                     // Simplest robust method: Bake EVERYTHING into new Points and set Rotation to 0.
                     
                     var currentPoints = new List<PointF>(path.Points);
                     
                     // A. Unwrap internal rotation if any to get "true world" points
                     if (path.Rotation != 0)
                     {
                         var cx = path.Position.X + path.Size.Width / 2f;
                         var cy = path.Position.Y + path.Size.Height / 2f;
                         using (var mat = new System.Drawing.Drawing2D.Matrix())
                         {
                             mat.RotateAt(path.Rotation, new PointF(cx, cy));
                             var ptsArr = currentPoints.ToArray();
                             mat.TransformPoints(ptsArr);
                             currentPoints = new List<PointF>(ptsArr);
                         }
                     }
                     
                     // B. Apply Group Transform (Rotate around Group Pivot + Translate)
                     newPoints = new List<PointF>();
                     
                     double grpRad = angle * Math.PI / 180.0;
                     double gCos = Math.Cos(grpRad);
                     double gSin = Math.Sin(grpRad);

                     foreach(var p in currentPoints)
                     {
                        // 1. Relative to Group Pivot
                        double px = p.X - pivot.X;
                        double py = p.Y - pivot.Y;

                        // 2. Rotate by Group Angle
                        double rotX = px * gCos - py * gSin;
                        double rotY = px * gSin + py * gCos;

                        // 3. Translate back + Shift
                        float finalX = (float)(pivot.X + rotX + translation.X);
                        float finalY = (float)(pivot.Y + rotY + translation.Y);
                        
                        newPoints.Add(new PointF(finalX, finalY));
                     }
                     
                     // Points are now fully transformed (baked). Rotation should be 0.
                     newRot = 0;
                     // New Position will be calculated by UpdateBounds() later, so passed newPos is essentially ignored/overwritten but needed for Command struct.
                }

                cmd.AddChange(obj, newPos, newRot, newPoints);
            }
        }

        private void OnPartUpdate(Polygon p)
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((Action)(() => 
            {
                _visualPolygons.Add(p);
                _canvas.Invalidate();
            }));
        }

        private void OnProgress(int a, int b)
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((Action)(() => 
            {
                if(b > 0) _progressBar.Value = Math.Min(100, (int)((a / (float)b) * 100));
            }));
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Sheet
            float sw = (float)_nudSheetW.Value;
            float sh = (float)_nudSheetH.Value;

            // Fit to view
            float scaleW = _canvas.Width / sw;
            float scaleH = _canvas.Height / sh;
            float scale = Math.Min(scaleW, scaleH) * 0.9f;
            
            float marginX = (_canvas.Width - sw * scale) / 2f;
            float marginY = (_canvas.Height - sh * scale) / 2f;

            g.TranslateTransform(marginX, marginY + sh * scale);
            g.ScaleTransform(scale, -scale);

            // Draw Sheet
            using (var brush = new SolidBrush(Color.White))
                g.FillRectangle(brush, 0, 0, sw, sh);
            using (var pen = new Pen(Color.Black, 2 / scale))
                g.DrawRectangle(pen, 0, 0, sw, sh);

            // Draw Polygons
            // To prevent tearing, we might clone the list, but it's add-only.
            Polygon[] toDraw;
            lock (_visualPolygons) { toDraw = _visualPolygons.ToArray(); }

            foreach (var poly in toDraw)
            {
                DrawPolygonRecursive(g, poly, scale);
            }
        }
        
        private void DrawPolygonRecursive(Graphics g, Polygon poly, float scale)
        {
            if (poly.Points.Count >= 2)
            {
                var pts = new PointF[poly.Points.Count];
                for(int i=0; i<poly.Points.Count; i++) pts[i] = new PointF((float)poly.Points[i].X, (float)poly.Points[i].Y);

                Color c = Color.CornflowerBlue;
                using (var brush = new SolidBrush(Color.FromArgb(100, c)))
                    g.FillPolygon(brush, pts);
                using (var pen = new Pen(Color.DarkBlue, 1 / scale))
                    g.DrawPolygon(pen, pts);
            }
            
            if (poly.Children.Count > 0)
            {
                foreach(var child in poly.Children)
                {
                    DrawPolygonRecursive(g, child, scale);
                }
            }
        }

        private void NestingForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopNesting();
            _inputObjects.Clear(); // Detach
        }
    }
}
