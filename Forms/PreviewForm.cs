using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using laser_gui_test.Data.GCode;

namespace laser_gui_test.Forms;

public class PreviewForm : Form
{
    private SplitContainer _split;
    private DataGridView _grid;
    private PictureBox _renderArea;
    private Panel _controlsPanel;
    private Button _btnPlay;
    private Button _btnPause;
    private Button _btnStop;
    private TrackBar _timeline;
    private NumericUpDown _numSpeed;
    private Label _lblStatus;

    private List<GCodeCommand> _commands;
    private int _currentIndex = 0;
    private System.Windows.Forms.Timer _playTimer;
    private Bitmap _cacheBitmap;

    // View Transforms
    private float _scale = 10f; // Pixels per mm
    private PointF _pan = PointF.Empty;
    private Point _lastMouse;
    private bool _isPanning;

    public PreviewForm(string gcode)
    {
        InitializeComponent();
        
        _commands = GCodeParser.Parse(gcode);
        _grid.DataSource = _commands;
        
        if (_commands.Count > 0)
        {
             _timeline.Maximum = _commands.Count - 1;
             FitToScreen();
        }

        _playTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _playTimer.Tick += PlayTimer_Tick;
    }

    private void InitializeComponent()
    {
        this.Text = "Laser Preview";
        this.Size = new Size(1000, 700);
        this.StartPosition = FormStartPosition.CenterParent;

        _split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 100, FixedPanel = FixedPanel.Panel1 };
        
        // Left: Grid
        _grid = new DataGridView 
        { 
            Dock = DockStyle.Fill, 
            AutoGenerateColumns = false, 
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            VirtualMode = true,
            ColumnHeadersVisible = false,
            AllowUserToResizeRows = false
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", DataPropertyName = "LineIndex", Width = 20 });
        // Removed Command Column to save space as requested (50px wide list)
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "cmd", DataPropertyName = "OriginalCommand", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        
        _grid.CellValueNeeded += (s, e) => 
        {
            if (e.RowIndex < _commands.Count)
            {
                if (e.ColumnIndex == 0) e.Value = _commands[e.RowIndex].LineIndex + 1;
                if (e.ColumnIndex == 1) e.Value = _commands[e.RowIndex].OriginalCommand;
            }
        };

        _grid.SelectionChanged += (s, e) => 
        {
            if (_grid.SelectedRows.Count > 0)
            {
                int idx = _grid.SelectedRows[0].Index;
                if (Math.Abs(idx - _currentIndex) > 0)
                {
                    _currentIndex = idx;
                    _timeline.Value = Math.Min(idx, _timeline.Maximum);
                    _renderArea.Invalidate();
                }
            }
        };

        _split.Panel1.Controls.Add(_grid);

        // Right: Render + Controls
        var rightPanel = new Panel { Dock = DockStyle.Fill };
        
        _controlsPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.LightGray };
        _renderArea = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White };
        
        // Render Area Events
        _renderArea.Paint += RenderArea_Paint;
        _renderArea.MouseDown += (s, e) => 
        { 
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right) 
            {
                _isPanning = true; 
                _lastMouse = e.Location; 
            }
        };
        _renderArea.MouseMove += (s, e) => 
        {
            if (_isPanning)
            {
                _pan.X += e.X - _lastMouse.X;
                _pan.Y += e.Y - _lastMouse.Y;
                _lastMouse = e.Location;
                _renderArea.Invalidate();
            }
        };
        _renderArea.MouseUp += (s, e) => { _isPanning = false; };
        _renderArea.MouseWheel += (s, e) => 
        {
            float factor = e.Delta > 0 ? 1.1f : 0.9f;
            _scale *= factor;
            _renderArea.Invalidate();
        };

        // Controls
        _btnPlay = new Button { Text = "Play", Location = new Point(10, 10) };
        _btnPause = new Button { Text = "Pause", Location = new Point(90, 10) };
        _btnStop = new Button { Text = "Stop", Location = new Point(170, 10) };
        
        _numSpeed = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 10, Location = new Point(260, 12), Width = 60 };
        var lblSpeed = new Label { Text = "Speed:", Location = new Point(260, 35), AutoSize = true };

        _timeline = new TrackBar { Location = new Point(330, 5), Width = 400, TickStyle = TickStyle.None };
        _timeline.Scroll += (s, e) => 
        {
            _currentIndex = _timeline.Value;
            UpdateSelection();
            _renderArea.Invalidate();
        };

        _btnPlay.Click += (s, e) => { _playTimer.Start(); };
        _btnPause.Click += (s, e) => { _playTimer.Stop(); };
        _btnStop.Click += (s, e) => { _playTimer.Stop(); _currentIndex = 0; UpdateSelection(); _renderArea.Invalidate(); };

        _controlsPanel.Controls.Add(_btnPlay);
        _controlsPanel.Controls.Add(_btnPause);
        _controlsPanel.Controls.Add(_btnStop);
        _controlsPanel.Controls.Add(_numSpeed);
        _controlsPanel.Controls.Add(lblSpeed);
        _controlsPanel.Controls.Add(_timeline);

        rightPanel.Controls.Add(_renderArea);
        rightPanel.Controls.Add(_controlsPanel);
        _split.Panel2.Controls.Add(rightPanel);

        this.Controls.Add(_split);
    }

    private void PlayTimer_Tick(object? sender, EventArgs e)
    {
        int speed = (int)_numSpeed.Value;
        _currentIndex += speed;
        if (_currentIndex >= _commands.Count)
        {
            _currentIndex = _commands.Count - 1;
            _playTimer.Stop();
        }
        
        UpdateSelection();
        _renderArea.Invalidate();
        _timeline.Value = Math.Min(_currentIndex, _timeline.Maximum);
    }

    private void UpdateSelection()
    {
        if (_currentIndex >= 0 && _currentIndex < _commands.Count && _grid.RowCount > _currentIndex)
        {
            // Sync Grid Selection without triggering event loop (if possible) or just set it
            // _grid.Rows[_currentIndex].Selected = true; // This might be slow for rapid updates
            // Optimize: Only update every N ticks?
            try
            {
               _grid.FirstDisplayedScrollingRowIndex = _currentIndex; // Auto-scroll
            }
            catch{}
        }
    }

    private void RenderArea_Paint(object? sender, PaintEventArgs e)
    {
        if (_commands == null || _commands.Count == 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None; // Crisp pixels for raster lines
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half; // Aligns pixels better
        g.TranslateTransform(_pan.X, _pan.Y);
        g.ScaleTransform(_scale, _scale);

        // Draw Background (All commands as faint lines?)
        // Caching: We should draw ALL commands to a bitmap once, then draw that bitmap.
        // But for "Progress" we want to highlight executed ones.
        // Strategy:
        // 1. Draw "Future" lines in Light Gray.
        // 2. Draw "Past" lines in Dark Color/Red.
        // 3. Draw Laser Head.
        
        // Simple implementation first: Iterate all loop. Optimization later.
        
        using var penTravel = new Pen(Color.LightBlue, 0) { DashStyle = DashStyle.Dot }; // 0 width = 1 pixel always
        
        // Raster Line Interval for Cut width
        float interval = Data.AppConfiguration.Instance.RasterLineInterval;
        if (interval <= 0) interval = 0.1f;
        
        using var penCut = new Pen(Color.FromArgb(50, 0, 0, 0), interval); // Future Cut (Faint)
        using var penExecuted = new Pen(Color.Red, interval); // Past Cut
        using var penTravelExecuted = new Pen(Color.Blue, 0) { DashStyle = DashStyle.Dot }; // Past Travel

        // Draw entire path faint first (Optimize: Cache this)
        // For 80k lines, this loop is too slow to do 60fps.
        // We really need a "Background Cache" of the whole job, then draw over it?
        // Or "Past" and "Future" caches.
        
        // Let's implement background caching on first load.
        if (_cacheBitmap == null)
        {
             UpdateCache(); // Draws FULL preview
        }
        
        if (_cacheBitmap != null)
        {
            // Draw FULL preview faded
             // Coordinate system of Bitmap? 
             // We can't easily use a Bitmap if we want indefinite zoom/pan without massive texture.
             // But for GDI+, drawing 1000 lines is fine. 50k is not.
             // We will skip optimization for "User Request 1" unless it lags.
        }

        // naive render loop
        // Optimization: dynamic step?
        // Let's try drawing only a window of commands? No, we need context.
        
        for (int i = 0; i < _commands.Count; i++)
        {
            var cmd = _commands[i];
            if (cmd.Type == CommandType.Other) continue;

            bool isPast = i <= _currentIndex;
            var pen = cmd.Type == CommandType.Travel 
                ? (isPast ? penTravelExecuted : penTravel)
                : (isPast ? penExecuted : penCut);
            
            // Adjust Opacity for Power?
            if (cmd.Type == CommandType.Cut && isPast)
            {
                 int alpha = (int)(255 * (cmd.Power / 1000f));
                 if (alpha < 20) alpha = 20; // Min visibility
                 penExecuted.Color = Color.FromArgb(alpha, 255, 0, 0);
                 pen = penExecuted;
            }

            g.DrawLine(pen, cmd.Start, cmd.End);
        }

        // Draw Head
        if (_currentIndex < _commands.Count)
        {
            var cmd = _commands[_currentIndex];
            var headPos = cmd.End; // Current position is end of current command
            
            float headSize = 5f / _scale;
            using var rPen = new Pen(Color.Red, 2f / _scale);
            g.DrawLine(rPen, headPos.X - headSize, headPos.Y - headSize, headPos.X + headSize, headPos.Y + headSize);
            g.DrawLine(rPen, headPos.X + headSize, headPos.Y - headSize, headPos.X - headSize, headPos.Y + headSize);
        }
    }

    private void UpdateCache()
    {
        // Placeholder for caching logic
    }
    
    private void FitToScreen()
    {
        if (_commands.Count == 0) return;
        
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach(var cmd in _commands)
        {
            if (cmd.Start.X < minX) minX = cmd.Start.X;
            if (cmd.Start.Y < minY) minY = cmd.Start.Y;
            if (cmd.End.X > maxX) maxX = cmd.End.X;
            if (cmd.End.Y > maxY) maxY = cmd.End.Y;
        }

        if (minX == float.MaxValue) return;
        
        float w = maxX - minX;
        float h = maxY - minY;
        if (w == 0) w = 100;
        if (h == 0) h = 100;
        
        float ratioX = (_renderArea.Width - 40) / w;
        float ratioY = (_renderArea.Height - 40) / h;
        _scale = Math.Min(ratioX, ratioY);
        
        // Center
        _pan = new PointF(20 - minX * _scale, 20 - minY * _scale);
        
        _renderArea.Invalidate();
    }
}
