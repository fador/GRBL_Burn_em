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
        
        // Scanning State
        private int _currentX = 0;
        private int _currentY = 0;
        private int _scanSpeed = 150; // Pixels per tick (Horizontal speed)
        private int _scanLineHeight = 20; // Number of rows processed per pass (Thicker beam for speed)
        
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
            _timer.Interval = 15; // High refresh rate for smooth scanning
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

            // 1. Update Sparks
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.X += s.VX;
                s.Y += s.VY;
                s.VY += 0.8f; 
                s.Life -= 30; 
                _sparks[i] = s;
                if (s.Life <= 0) _sparks.RemoveAt(i);
            }

            // 2. Process Scanning
            Rectangle rect = new Rectangle(0, 0, _canvas.Width, _canvas.Height);
            BitmapData srcData = _sourceImage.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData destData = _canvas.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            float currentTickMaxDarkness = 0f;

            try
            {
                int bytes = Math.Abs(srcData.Stride) * _canvas.Height;
                byte[] srcBuffer = new byte[bytes];
                byte[] destBuffer = new byte[bytes];

                Marshal.Copy(srcData.Scan0, srcBuffer, 0, bytes);
                Marshal.Copy(destData.Scan0, destBuffer, 0, bytes);

                int width = _canvas.Width;
                int height = _canvas.Height;
                int stride = srcData.Stride;
                int bpp = 4;

                // A. Laser Cutting (Scanning Left to Right)
                int steps = 0;
                while (steps < _scanSpeed && _currentY < height)
                {
                    // Process a vertical strip at _currentX (Height = _scanLineHeight)
                    int yStart = _currentY;
                    int yEnd = Math.Min(_currentY + _scanLineHeight, height);

                    // Mark these rows as HOT (Reset fade delay)
                    for (int h = yStart; h < yEnd; h++)
                        _rowHeat[h] = 2.5f; 

                    for (int y = yStart; y < yEnd; y++)
                    {
                        int idx = (y * stride) + (_currentX * bpp);
                        
                        byte b = srcBuffer[idx];
                        byte g = srcBuffer[idx + 1];
                        byte r = srcBuffer[idx + 2];
                        byte a = srcBuffer[idx + 3];

                        float brightness = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
                        float visualIntensity = 1.0f - brightness;

                        if (visualIntensity > currentTickMaxDarkness) 
                            currentTickMaxDarkness = visualIntensity;

                        // Initial Burn (Grayscale)
                        byte gray = (byte)(brightness * 255);
                        
                        destBuffer[idx] = gray;     
                        destBuffer[idx + 1] = gray; 
                        destBuffer[idx + 2] = gray; 
                        destBuffer[idx + 3] = a;    

                        // Sparks
                        if (visualIntensity > 0.2f && _rnd.NextDouble() < (visualIntensity * 0.05)) 
                        {
                            _sparks.Add(new Spark 
                            { 
                                X = _currentX, Y = y, 
                                VX = (float)(_rnd.NextDouble() * 4 - 2), 
                                VY = (float)(_rnd.NextDouble() * -5 - 2), 
                                Life = 255,
                                BaseColor = (gray < 80) ? Color.Gold : Color.OrangeRed 
                            });
                        }
                    }

                    // Move Horizontal
                    _currentX++;
                    
                    // Wrap around (Carriage Return)
                    if (_currentX >= width)
                    {
                        _currentX = 0;
                        _currentY += _scanLineHeight;
                        if (_currentY >= height) break;
                    }
                    steps++;
                }

                // B. Update Cooling Rows (Fade In)
                int scanLimitY = Math.Min(_currentY, height);
                
                for (int y = 0; y < scanLimitY; y++)
                {
                    // Don't cool the lines currently being cut
                    if (y >= _currentY && y < _currentY + _scanLineHeight) continue;

                    if (_rowHeat[y] <= 0) continue; 

                    _rowHeat[y] -= 0.05f; // Slower fade for better effect
                    if (_rowHeat[y] < 0) _rowHeat[y] = 0;

                    float heat = _rowHeat[y];
                    if (heat > 1.0f) heat = 1.0f; 

                    if (heat < 1.0f) 
                    {
                        int rowOffset = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowOffset + (x * bpp);
                            byte b_src = srcBuffer[idx];
                            byte g_src = srcBuffer[idx + 1];
                            byte r_src = srcBuffer[idx + 2];
                            
                            float lum = (0.299f * r_src + 0.587f * g_src + 0.114f * b_src);
                            byte gray = (byte)lum;

                            destBuffer[idx] = (byte)(b_src + (gray - b_src) * heat);
                            destBuffer[idx + 1] = (byte)(g_src + (gray - g_src) * heat);
                            destBuffer[idx + 2] = (byte)(r_src + (gray - r_src) * heat);
                        }
                    }
                }

                Marshal.Copy(destBuffer, 0, destData.Scan0, bytes);
            }
            finally
            {
                _sourceImage.UnlockBits(srcData);
                _canvas.UnlockBits(destData);
            }
            
            // 3. Completion Check
            if (_currentY >= _sourceImage.Height)
            {
                bool allCooled = true;
                for(int k=0; k<_sourceImage.Height; k+=10)
                    if (_rowHeat[k] > 0) { allCooled = false; break; }

                if (_sparks.Count == 0 && allCooled)
                {
                    EndSplash();
                    return;
                }
            }

            _laserIntensity += (currentTickMaxDarkness - _laserIntensity) * 0.3f;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_canvas != null)
                e.Graphics.DrawImage(_canvas, 0, 0);

            // Draw Sparks
            foreach (var s in _sparks)
            {
                int a = s.Life; 
                if (a > 255) a = 255; else if (a < 0) a = 0;

                using (var brush = new SolidBrush(Color.FromArgb(a, s.BaseColor)))
                {
                    e.Graphics.FillRectangle(brush, s.X, s.Y, 2, 2);
                }
            }

            // Draw Focused Laser Beam
            if (_currentY < Height)
            {
                int alpha = (int)(_laserIntensity * 255);
                if (alpha > 255) alpha = 255;
                if (alpha < 50) alpha = 50; // Always show faint beam

                // Source: Top Right Corner
                Point pSource = new Point(Width, 0);
                
                // Target: Current Cutting Head (Center of scanline block)
                Point pTarget = new Point(_currentX, _currentY + (_scanLineHeight/2));
                
                // 1. Draw The Beam
                using (var beamPen = new Pen(Color.FromArgb(alpha, 255, 50, 50), 2))
                {
                    e.Graphics.DrawLine(beamPen, pSource, pTarget);
                }

                // 2. Beam Core (Brighter, thinner)
                using (var corePen = new Pen(Color.FromArgb(alpha, 255, 200, 200), 1))
                {
                    e.Graphics.DrawLine(corePen, pSource, pTarget);
                }

                // 3. Contact Point Glow
                int glowSize = 6 + (int)(_laserIntensity * 10);
                using (var brush = new SolidBrush(Color.FromArgb((int)(alpha * 0.8), 255, 150, 50)))
                {
                    e.Graphics.FillEllipse(brush, pTarget.X - glowSize/2, pTarget.Y - glowSize/2, glowSize, glowSize);
                }

                // 4. White Hot Center
                using (var brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    e.Graphics.FillEllipse(brush, pTarget.X - 2, pTarget.Y - 2, 4, 4);
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