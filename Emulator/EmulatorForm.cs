using System;
using System.Drawing;
using System.Windows.Forms;

namespace grbl_burn_em_emulator;

public partial class EmulatorForm : Form
{
    private PictureBox _workArea;
    private TextBox _logBox;
    private Bitmap _bedBitmap;
    private Graphics _bedGraphics;
    
    // Scale: 1px = 1mm? Or adjustable.
    // Let's say WorkArea is 400x400mm.
    private float _scale = 1.5f; 

    private int _bedWidth;
    private int _bedHeight;

    public EmulatorForm()
    {
        InitializeComponent();
        SetupUI();
        
        // Initialize Bed
        int w = (int)(400 * _scale);
        int h = (int)(400 * _scale);
        _bedWidth = w;
        _bedHeight = h;
        _bedBitmap = new Bitmap(w, h);
        _bedGraphics = Graphics.FromImage(_bedBitmap);
        _bedGraphics.Clear(Color.Beige); // Wood color
        
        // Wire Logic
        EmulatorLogic.Instance.LogMessage += OnLog;
        EmulatorLogic.Instance.StateChanged += OnStateChanged;
        EmulatorLogic.Instance.BurnMark += OnBurnMark;
        
        // Start Server
        TcpServer.Instance.Start(2345);
        
        // Start Camera Server
        CameraServer.Instance.CaptureProvider = () => 
        {
            lock(_bedBitmap)
            {
               return VirtualCamera.Instance.Capture(_bedBitmap, _scale);
            }
        };
        CameraServer.Instance.Start(2346);
        
        // Start Repaint Timer
        var timer = new System.Windows.Forms.Timer();
        timer.Interval = 33; // ~30fps
        timer.Tick += (s, e) => _workArea.Invalidate();
        timer.Start();
    }

    private void OnLog(string msg)
    {
        if (InvokeRequired) { Invoke(new Action<string>(OnLog), msg); return; }
        _logBox.AppendText(msg + "\r\n");
    }
    
    private void OnStateChanged()
    {
        // Redraw usually handled by Timer, but maybe update Title?
    }
    
    private void OnBurnMark(float x, float y, float power)
    {
        lock (_bedBitmap)
        {
             // Transform (Invert Y for CNC Coordinates)
             float sx = x * _scale;
             float sy = _bedHeight - (y * _scale);
             
             // Check bounds
             if (sx >=0 && sx < _bedWidth && sy >=0 && sy < _bedHeight)
             {
                 // Calculate Alpha based on Power (Assumed Max S=1000)
                 // Map 0-1000 to Alpha 0-255
                 float intensity = Math.Clamp(power / 1000.0f, 0f, 1f);
                 int alpha = (int)(intensity * 255);
                 
                 if (alpha > 5) // Don't draw invisible
                 {
                     using var brush = new SolidBrush(Color.FromArgb(alpha, 30, 30, 30)); // Black/Grey
                     _bedGraphics.FillRectangle(brush, sx, sy, 2, 2); 
                 }
             }
        }
    }

    private void SetupUI()
    {
        this.Text = "GRBL Burn Em Emulator - Port 2345";
        this.Size = new Size(800, 800);

        // Split Container
        var split = new SplitContainer();
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Horizontal;
        split.SplitterDistance = 600;
        this.Controls.Add(split);

        // Work Area (Top)
        _workArea = new PictureBox();
        _workArea.Dock = DockStyle.Fill;
        _workArea.BackColor = Color.Gray;
        _workArea.Paint += _workArea_Paint;
        split.Panel1.Controls.Add(_workArea);

        // Log (Bottom)
        _logBox = new TextBox();
        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        split.Panel2.Controls.Add(_logBox);
        
        // Clear Button (Overlay on Log or separate panel? Let's add a small panel at bottom of Split1 or top of Split2)
        // Simplest: Add a helper panel in Panel2
        var buttonPanel = new Panel();
        buttonPanel.Dock = DockStyle.Bottom;
        buttonPanel.Height = 40;
        split.Panel2.Controls.Add(buttonPanel);
        
        var btnClear = new Button();
        btnClear.Text = "Clear Bed";
        btnClear.Dock = DockStyle.Left;
        btnClear.Click += (s, e) => 
        {
             lock(_bedBitmap)
             {
                 _bedGraphics.Clear(Color.Beige);
             }
             _workArea.Invalidate();
        };
        buttonPanel.Controls.Add(btnClear);
    }

    private void _workArea_Paint(object? sender, PaintEventArgs e)
    {
        // Draw Bed
        lock(_bedBitmap)
        {
            e.Graphics.DrawImage(_bedBitmap, 0, 0);
        }
        
        // Draw Laser Head
        float lx = EmulatorLogic.Instance.X * _scale;
        float ly = _bedHeight - (EmulatorLogic.Instance.Y * _scale);
        
        // Crosshair
        e.Graphics.DrawLine(Pens.Red, lx - 10, ly, lx + 10, ly);
        e.Graphics.DrawLine(Pens.Red, lx, ly - 10, lx, ly + 10);
        
        // State
        e.Graphics.DrawString($"Pos: {EmulatorLogic.Instance.X:F1}, {EmulatorLogic.Instance.Y:F1}\nState: {EmulatorLogic.Instance.State}\nLaser: {(EmulatorLogic.Instance.IsLaserOn ? "ON" : "OFF")}", 
            SystemFonts.DefaultFont, Brushes.Black, 10, 10);
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.Name = "EmulatorForm";
        this.Text = "Emulator";
        this.ResumeLayout(false);
    }
}

