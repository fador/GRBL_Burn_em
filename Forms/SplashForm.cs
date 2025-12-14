using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using laser_gui_test.Controls;
using laser_gui_test.Data.OpenGL;

namespace laser_gui_test.Forms
{
    public class SplashForm : Form
    {
        private SplashGLControl _glControl = null!;
        private System.Windows.Forms.Timer _timer = null!;

        // Simulation State
        private Bitmap? _sourceImage;
        private int _currentX = 0;
        private int _currentY = 0;
        private int _scanSpeed = 150; 
        private int _scanLineHeight = 20; 
        private float _laserIntensity = 0f;
        
        // Heat Map (0.0 = Original Color, > 0.0 = Blend to Gray)
        private float[] _rowHeat = Array.Empty<float>();

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
            this.BackColor = Color.Black;
            
            LoadLogo();

            _glControl = new SplashGLControl(this)
            {
                Dock = DockStyle.Fill
            };
            this.Controls.Add(_glControl);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 15;
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void LoadLogo()
        {
             var assembly = Assembly.GetExecutingAssembly();
             var resourceName = assembly.GetManifestResourceNames()
                                        .FirstOrDefault(r => r.EndsWith("logo.png"));

             if (resourceName != null)
             {
                 try
                 {
                     using (var stream = assembly.GetManifestResourceStream(resourceName))
                     {
                         if (stream != null)
                             _sourceImage = new Bitmap(stream);
                     }
                 }
                 catch { }
             }

             if (_sourceImage == null)
             {
                 _sourceImage = new Bitmap(500, 300);
                 using (var g = Graphics.FromImage(_sourceImage))
                 {
                     g.Clear(Color.Black);
                     g.DrawString("LASER CTRL", new Font("Arial", 40, FontStyle.Bold), Brushes.White, 50, 100);
                 }
             }

             this.Size = _sourceImage.Size;
             _rowHeat = new float[_sourceImage.Height];
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_sourceImage == null) { EndSplash(); return; }

            int width = _sourceImage.Width;
            int height = _sourceImage.Height;

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

            // 2. Scan Logic
            int steps = 0;
            float currentTickMaxDarkness = 0f;

            // We need to sample darkness for sparks. 
            // Optim: We can cache a "Darkness Map" or sample Bitmap directly (Bitmap.GetPixel is slow).
            // Using LockBits ONCE per frame for sampling is better than GetPixel.
            // But we only need it at _currentX, _currentY.
            // For now, let's assume random sparks if we don't want to lock bits.
            // Or just lock bits for the scan region.
            
            // Simpler: Just random sparks based on scan speed? 
            // Original code: visualIntensity from pixel.
            // We can skip pixel-perfect spark spawning to save CPU or LockBits just the small strip?
            // Let's Skip pixel reading for performance. Sparks are visual candy anyway.

            while (steps < _scanSpeed && _currentY < height)
            {
                int yStart = _currentY;
                int yEnd = Math.Min(_currentY + _scanLineHeight, height);

                // Keep Active Rows Hot
                for (int h = yStart; h < yEnd; h++)
                    _rowHeat[h] = 2.5f;

                // Move X
                _currentX++;
                
                // Spawn Sparks (Simplified: purely random, no pixel intensity check)
                if (_rnd.NextDouble() < 0.05) 
                {
                    int sy = _rnd.Next(yStart, yEnd);
                    _sparks.Add(new Spark 
                    { 
                        X = _currentX, Y = sy, 
                        VX = (float)(_rnd.NextDouble() * 4 - 2), 
                        VY = (float)(_rnd.NextDouble() * -5 - 2), 
                        Life = 255,
                        BaseColor = Color.OrangeRed 
                    });
                     currentTickMaxDarkness = 1.0f; // Fake intensity
                }

                if (_currentX >= width)
                {
                    _currentX = 0;
                    _currentY += _scanLineHeight;
                    if (_currentY >= height) break;
                }
                steps++;
            }

            // 3. Cool Down
             int scanLimitY = Math.Min(_currentY, height);
             for (int y = 0; y < scanLimitY; y++)
             {
                 if (y >= _currentY && y < _currentY + _scanLineHeight) continue;
                 if (_rowHeat[y] > 0)
                 {
                     _rowHeat[y] -= 0.05f;
                     if (_rowHeat[y] < 0) _rowHeat[y] = 0;
                 }
             }

            // 4. Completion
            if (_currentY >= height)
            {
                bool allCooled = true;
                for (int k = 0; k < height; k += 10)
                    if (_rowHeat[k] > 0) { allCooled = false; break; }

                if (_sparks.Count == 0 && allCooled)
                {
                    EndSplash();
                    return;
                }
            }

            _laserIntensity += (currentTickMaxDarkness - _laserIntensity) * 0.3f;
            _glControl.Invalidate();
        }

        private void EndSplash()
        {
            _timer.Stop();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                EndSplash();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // --- Inner OpenGL Control ---
        private class SplashGLControl : OpenGLControl
        {
            private SplashForm _owner;
            private uint _texColor = 0;
            private uint _texGray = 0;
            private bool _texturesLoaded = false;

            public SplashGLControl(SplashForm owner)
            {
                _owner = owner;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                if (!_texturesLoaded && _owner._sourceImage != null)
                {
                    EnsureContext();
                    LoadTextures(_owner._sourceImage);
                }
            }
            
            private void EnsureContext()
            {
                // OpenGLControl manages context, but we might need to be sure we are current
            }

            private void LoadTextures(Bitmap bmp)
            {
                // 1. Color Texture
                uint[] textures = new uint[2];
                GL.glGenTextures(2, textures);
                _texColor = textures[0];
                _texGray = textures[1];

                BitmapData data = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height), 
                    ImageLockMode.ReadOnly, 
                    PixelFormat.Format32bppArgb);

                // Use BGRA format for 32bppArgb
                try
                {
                    GL.glBindTexture(GL.GL_TEXTURE_2D, _texColor);
                    GL.glTexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, 
                        bmp.Width, bmp.Height, 0, 
                        GL.GL_BGRA, GL.GL_UNSIGNED_BYTE, data.Scan0);
                    
                    GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_LINEAR);
                    GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_LINEAR);

                    // 2. Grayscale Texture
                    // We must manually create grayscale bytes
                    int bytes = Math.Abs(data.Stride) * bmp.Height;
                    byte[] grayBuffer = new byte[bytes];
                    Marshal.Copy(data.Scan0, grayBuffer, 0, bytes);

                    for (int i = 0; i < bytes; i += 4)
                    {
                        byte b = grayBuffer[i];
                        byte g = grayBuffer[i+1];
                        byte r = grayBuffer[i+2];
                        // a = i+3
                        
                        // Grayscale
                        byte lum = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                        grayBuffer[i] = lum;
                        grayBuffer[i+1] = lum;
                        grayBuffer[i+2] = lum;
                    }
                    
                    GL.glBindTexture(GL.GL_TEXTURE_2D, _texGray);

                    // Upload array
                    GCHandle pinned = GCHandle.Alloc(grayBuffer, GCHandleType.Pinned);
                    try
                    {
                        GL.glTexImage2D(GL.GL_TEXTURE_2D, 0, (int)GL.GL_RGBA, 
                            bmp.Width, bmp.Height, 0, 
                            GL.GL_BGRA, GL.GL_UNSIGNED_BYTE, pinned.AddrOfPinnedObject());
                    }
                    finally
                    {
                        pinned.Free();
                    }

                    GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, (int)GL.GL_LINEAR);
                    GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, (int)GL.GL_LINEAR);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                
                _texturesLoaded = true;
            }

            public override void OnRender()
            {
                if (!_texturesLoaded) return;
                
                int w = this.Width;
                int h = this.Height;
                int imgW = _owner._sourceImage!.Width;
                int imgH = _owner._sourceImage.Height;

                GL.glViewport(0, 0, w, h);
                GL.glMatrixMode(GL.GL_PROJECTION);
                GL.glLoadIdentity();
                // Screen: Top=0, Bottom=Height.
                GL.glOrtho(0, w, h, 0, -1, 1); 
                GL.glMatrixMode(GL.GL_MODELVIEW);
                GL.glLoadIdentity();

                GL.glClearColor(0,0,0,1);
                GL.glClear(GL.GL_COLOR_BUFFER_BIT);
                
                GL.glEnable(GL.GL_TEXTURE_2D);
                GL.glEnable(GL.GL_BLEND);
                GL.glBlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);

                // Render Completed Rows (Color + Heat)
                int currentY = _owner._currentY;
                int scanH = _owner._scanLineHeight;
                
                // 1. Color Layer (All revealed rows)
                GL.glBindTexture(GL.GL_TEXTURE_2D, _texColor);
                GL.glColor4f(1, 1, 1, 1);
                
                // Completed Area
                DrawQuad(0, 0, w, currentY, 0, 0, 1, (float)currentY/imgH);
                
                // 2. Heat/Gray Overlay
                GL.glBindTexture(GL.GL_TEXTURE_2D, _texGray);
                
                GL.glBegin(GL.GL_QUADS);
                int limitY = Math.Min(currentY, _owner._rowHeat.Length);
                for(int y=0; y < limitY; y++)
                {
                    float heat = _owner._rowHeat[y];
                    if (heat <= 0.01f) continue;
                    
                    float alpha = heat;
                    if (alpha > 1f) alpha = 1f;
                    
                    GL.glColor4f(1, 1, 1, alpha);
                    
                    float v0 = (float)y / imgH;
                    float v1 = (float)(y + 1) / imgH;
                    
                    GL.glTexCoord2f(0, v0); GL.glVertex2f(0, y);
                    GL.glTexCoord2f(1, v0); GL.glVertex2f(w, y);
                    GL.glTexCoord2f(1, v1); GL.glVertex2f(w, y + 1);
                    GL.glTexCoord2f(0, v1); GL.glVertex2f(0, y + 1);
                }
                GL.glEnd();

                // 3. Active Strip (Behind the laser head)
                // From (0, currentY) to (currentX, currentY + scanH)
                // This area is FRESH CUT -> Full Heat -> Full Gray
                // NOTE: We only draw up to currentX!
                if (currentY < h)
                {
                    int stripH = Math.Min(scanH, h - currentY);
                    
                    // Since active area is heat > 1, we just draw Gray Texture fully opaque
                    GL.glBindTexture(GL.GL_TEXTURE_2D, _texGray);
                    GL.glColor4f(1, 1, 1, 1);
                    
                    if (_owner._currentX > 0)
                    {
                        float uEnd = (float)_owner._currentX / imgW;
                        float v0 = (float)currentY / imgH;
                        float v1 = (float)(currentY + stripH) / imgH;
                        
                        DrawQuad(0, currentY, _owner._currentX, stripH, 0, v0, uEnd, v1);
                    }
                }
                
                GL.glDisable(GL.GL_TEXTURE_2D);

                // 4. Laser Beam & Sparks
                DrawOverlayEffects();
            }

            private void DrawQuad(float x, float y, float w, float h, float u0, float v0, float u1, float v1)
            {
                GL.glBegin(GL.GL_QUADS);
                GL.glTexCoord2f(u0, v0); GL.glVertex2f(x, y);
                GL.glTexCoord2f(u1, v0); GL.glVertex2f(x + w, y);
                GL.glTexCoord2f(u1, v1); GL.glVertex2f(x + w, y + h);
                GL.glTexCoord2f(u0, v1); GL.glVertex2f(x, y + h);
                GL.glEnd();
            }

            private void DrawOverlayEffects()
            {
                // Sparks
                GL.glBegin(GL.GL_QUADS);
                foreach(var s in _owner._sparks)
                {
                    float alpha = s.Life / 255f;
                    Color c = s.BaseColor;
                    GL.glColor4f(c.R/255f, c.G/255f, c.B/255f, alpha);
                    
                    float size = 2f;
                    GL.glVertex2f(s.X, s.Y);
                    GL.glVertex2f(s.X + size, s.Y);
                    GL.glVertex2f(s.X + size, s.Y + size);
                    GL.glVertex2f(s.X, s.Y + size);
                }
                GL.glEnd();
                
                // Laser Beam
                if (_owner._currentY < _owner.Height)
                {
                    float intensity = _owner._laserIntensity;
                    if (intensity < 0.2f) intensity = 0.2f;
                    float alpha = intensity;
                    
                    float tx = _owner._currentX;
                    float ty = _owner._currentY + (_owner._scanLineHeight / 2);
                    float sx = _owner.Width;
                    float sy = 0;
                    
                    GL.glLineWidth(2f);
                    GL.glBegin(GL.GL_LINES);
                    
                    // Outer Beam
                    GL.glColor4f(1f, 0.2f, 0.2f, alpha);
                    GL.glVertex2f(sx, sy);
                    GL.glVertex2f(tx, ty);
                    
                    GL.glEnd();
                    
                    // Core
                    GL.glLineWidth(1f);
                    GL.glBegin(GL.GL_LINES);
                    GL.glColor4f(1f, 0.8f, 0.8f, alpha);
                    GL.glVertex2f(sx, sy);
                    GL.glVertex2f(tx, ty);
                    GL.glEnd();
                    
                    // Dot
                    // Draw simple diamond point
                    GL.glBegin(GL.GL_QUADS);
                    GL.glColor4f(1f, 0.6f, 0.2f, alpha);
                    float d = 3f + (intensity * 4);
                    GL.glVertex2f(tx, ty - d);
                    GL.glVertex2f(tx + d, ty);
                    GL.glVertex2f(tx, ty + d);
                    GL.glVertex2f(tx - d, ty);
                    GL.glEnd();
                }
            }
        }
    }
}