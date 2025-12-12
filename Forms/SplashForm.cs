using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;

namespace laser_gui_test.Forms
{
    public class SplashForm : Form
    {
        private Bitmap? _sourceImage;
        private Bitmap? _canvas;
        private System.Windows.Forms.Timer _timer;
        private int _currentY = 0;
        private int _rowsPerTick = 5;
        private float _laserIntensity = 0f;
        
        // Heat Map for "Cooling" effect (0.0 = Original Color, 1.0 = Grayscale/Hot)
        private float[]? _rowHeat; 

        // Sparks
        private struct Spark
        {
            public float X, Y;
            public float VX, VY;
            public int Life;
            public Color BaseColor;
        }
        private List<Spark> _sparks = new List<Spark>();
        private Random _rnd = new Random();

        public SplashForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.DoubleBuffered = true;
            this.TopMost = true;

            // Load logo
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "logo.png");
            if (!File.Exists(logoPath))
                logoPath = Path.Combine(Directory.GetCurrentDirectory(), "logo.png");

            if (File.Exists(logoPath))
            {
                try
                {
                    using (var temp = new Bitmap(logoPath))
                        _sourceImage = new Bitmap(temp);
                    
                    this.Size = _sourceImage.Size;
                    _canvas = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppPArgb);
                    _rowHeat = new float[this.Height];
                    
                    using (var g = Graphics.FromImage(_canvas))
                        g.Clear(Color.White);
                }
                catch
                {
                    SetupFallback();
                }
            }
            else
            {
                 SetupFallback();
            }

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 30; // Faster tick for smoother animation
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void SetupFallback()
        {
            this.Size = new Size(500, 300);
            _sourceImage = new Bitmap(500, 300);
            using(var g = Graphics.FromImage(_sourceImage))
            {
                g.Clear(Color.White);
                g.DrawString("LASER CTRL", new Font("Arial", 40, FontStyle.Bold), Brushes.Black, 50, 100);
            }
            _canvas = new Bitmap(500, 300, PixelFormat.Format32bppPArgb);
            _rowHeat = new float[300];
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_sourceImage == null || _canvas == null || _rowHeat == null)
            {
                EndSplash();
                return;
            }

            // 1. Update Sparks (Cleanup dead ones first)
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.X += s.VX;
                s.Y += s.VY;
                s.VY += 0.8f; // Stronger gravity
                s.Life -= 30; // Die much faster (Optimize: Short lifespan)
                
                // Update in place
                _sparks[i] = s;

                if (s.Life <= 0) 
                    _sparks.RemoveAt(i);
            }

            // 2. Process Pixels (Using Marshal.Copy for Managed Performance)
            Rectangle rect = new Rectangle(0, 0, _canvas.Width, _canvas.Height);
            BitmapData srcData = _sourceImage.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData destData = _canvas.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            float currentTickMaxDarkness = 0f;

            try
            {
                int bytes = Math.Abs(srcData.Stride) * _canvas.Height;
                byte[] srcBuffer = new byte[bytes];
                byte[] destBuffer = new byte[bytes];

                // Copy data from unmanaged memory to managed arrays
                Marshal.Copy(srcData.Scan0, srcBuffer, 0, bytes);
                Marshal.Copy(destData.Scan0, destBuffer, 0, bytes);

                int width = _canvas.Width;
                int height = _canvas.Height;
                int stride = srcData.Stride; // Assume strides are equal for same size/format bitmaps
                int bpp = 4; // ARGB

                // A. Process New Rows (The Laser Cut)
                int endY = Math.Min(_currentY + _rowsPerTick, height);
                
                // Only process if we haven't finished scanning
                if (_currentY < height)
                {
                    for (int y = _currentY; y < endY; y++)
                    {
                        int rowOffset = y * stride;
                        _rowHeat[y] = 2.0f; // Start extra hot (delay fade slightly)

                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowOffset + (x * bpp);
                            byte b = srcBuffer[idx];
                            byte g = srcBuffer[idx + 1];
                            byte r = srcBuffer[idx + 2];
                            byte a = srcBuffer[idx + 3];

                            // Quick brightness calc
                            float brightness = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
                            float visualIntensity = 1.0f - brightness;

                            if (visualIntensity > currentTickMaxDarkness) 
                                currentTickMaxDarkness = visualIntensity;

                            // Initial "Burn" (Grayscale)
                            byte gray = (byte)(brightness * 255);
                            
                            destBuffer[idx] = gray;     // B
                            destBuffer[idx + 1] = gray; // G
                            destBuffer[idx + 2] = gray; // R
                            destBuffer[idx + 3] = a;    // A

                            // Spawn Spark (Optimized Chance)
                            if (visualIntensity > 0.2f && _rnd.NextDouble() < (visualIntensity * 0.05)) 
                            {
                                _sparks.Add(new Spark 
                                { 
                                    X = x, Y = y, 
                                    VX = (float)(_rnd.NextDouble() * 6 - 3), 
                                    VY = (float)(_rnd.NextDouble() * -6 - 3), 
                                    Life = 255,
                                    BaseColor = (gray < 80) ? Color.Gold : Color.OrangeRed 
                                });
                            }
                        }
                    }
                    _currentY = endY;
                }

                // B. Update Cooling Rows (The Fade In)
                int scanLimitY = Math.Min(_currentY, height);
                
                for (int y = 0; y < scanLimitY; y++)
                {
                    if (_rowHeat[y] <= 0) continue; // Already cooled

                    _rowHeat[y] -= 0.1f; // Cooling speed
                    if (_rowHeat[y] < 0) _rowHeat[y] = 0;

                    float heat = _rowHeat[y];
                    if (heat > 1.0f) heat = 1.0f; // Clamp visual blend factor

                    // Only blend if heat is changing
                    if (heat < 1.0f) 
                    {
                        int rowOffset = y * stride;

                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowOffset + (x * bpp);
                            byte b_src = srcBuffer[idx];
                            byte g_src = srcBuffer[idx + 1];
                            byte r_src = srcBuffer[idx + 2];
                            
                            // Get current "burned" gray value
                            float lum = (0.299f * r_src + 0.587f * g_src + 0.114f * b_src);
                            byte gray = (byte)lum;

                            // Lerp
                            destBuffer[idx] = (byte)(b_src + (gray - b_src) * heat);
                            destBuffer[idx + 1] = (byte)(g_src + (gray - g_src) * heat);
                            destBuffer[idx + 2] = (byte)(r_src + (gray - r_src) * heat);
                        }
                    }
                }

                // Copy modified data back to unmanaged memory
                Marshal.Copy(destBuffer, 0, destData.Scan0, bytes);
            }
            finally
            {
                _sourceImage.UnlockBits(srcData);
                _canvas.UnlockBits(destData);
            }
            
            // 3. Laser Physics & Completion Check
            if (_currentY >= _sourceImage.Height)
            {
                // Ensure all heat is gone before quitting? Or just check sparks
                bool allCooled = true;
                for(int k=0; k<_sourceImage.Height; k+=10) // check sparsely
                    if (_rowHeat[k] > 0) { allCooled = false; break; }

                if (_sparks.Count == 0 && allCooled)
                {
                    EndSplash();
                    return;
                }
            }

            // Smooth laser intensity based on actual pixel darkness
            _laserIntensity += (currentTickMaxDarkness - _laserIntensity) * 0.3f;

            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Do not call base.OnPaint(e) for full control
            
            if (_canvas != null)
                e.Graphics.DrawImage(_canvas, 0, 0);

            // Draw Sparks (Optimized)
            // Grouping isn't strictly necessary for < 500 sparks, but nice for logic
            foreach (var s in _sparks)
            {
                // Simple pixel pushing is faster than creating brushes if we had unsafe access here,
                // but for GDI+, FillRectangle is okay for low counts.
                // Alpha fade
                int a = s.Life; 
                if (a > 255) a = 255;
                if (a < 0) a = 0;

                using (var brush = new SolidBrush(Color.FromArgb(a, s.BaseColor)))
                {
                    e.Graphics.FillRectangle(brush, s.X, s.Y, 2, 2);
                }
            }

            // Draw Laser "Galvo" Beam
            if (_currentY < Height && _currentY > 0)
            {
                int y = _currentY;
                int alpha = (int)(_laserIntensity * 255);
                if (alpha > 255) alpha = 255;
                
                // Beam Origin (Top Right Corner)
                Point origin = new Point(Width, 0); 
                
                // Laser Fan (Triangle to the current line)
                if (alpha > 10)
                {
                    Point p1 = new Point(0, y);
                    Point p2 = new Point(Width, y);
                    Point pCenter = new Point(Width / 2, y);

                    // 1. Fill the "Volume" of the beam (Solid "Sheet" of light instead of fade)
                    using (var brush = new SolidBrush(Color.FromArgb(alpha / 4, 255, 0, 0)))
                    {
                         e.Graphics.FillPolygon(brush, new Point[] { origin, p1, p2 });
                    }

                    // 2. Draw defined edges to make it look like a contained beam
                    using (var edgePen = new Pen(Color.FromArgb(alpha / 2, 255, 0, 0), 2))
                    {
                        e.Graphics.DrawLine(edgePen, origin, p1);
                        e.Graphics.DrawLine(edgePen, origin, p2);
                    }

                    // 3. Draw a "Core" ray to simulate intensity/focus
                    using (var corePen = new Pen(Color.FromArgb(alpha, 255, 200, 200), 1))
                    {
                        e.Graphics.DrawLine(corePen, origin, pCenter);
                    }

                    // 4. Bright hot line at the cut position
                    using (var burnPen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 2))
                    {
                        e.Graphics.DrawLine(burnPen, 0, y, Width, y);
                    }
                    
                    // 5. Impact Glow
                    using (var brush = new SolidBrush(Color.FromArgb((int)(alpha * 0.4), 255, 100, 50)))
                    {
                        e.Graphics.FillRectangle(brush, 0, y - 2, Width, 5);
                    }
                }
            }
        }

        private void EndSplash()
        {
            _timer.Stop();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}