using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using laser_gui_test.Data.GCode;
using laser_gui_test.Controls;
using laser_gui_test.Data.OpenGL;

namespace laser_gui_test.Forms;

public class PreviewForm : Form
{
    private SplitContainer _split = null!;
    private DataGridView _grid = null!;
    // Use our custom OpenGLControl instead of PictureBox
    private PreviewGLControl _renderArea = null!;
    private Panel _controlsPanel = null!;
    private Button _btnPlay = null!;
    private Button _btnPause = null!;
    private Button _btnStop = null!;
    private TrackBar _timeline = null!;
    private NumericUpDown _numSpeed = null!;

    private List<GCodeCommand> _commands;
    private int _currentIndex = 0;
    private System.Windows.Forms.Timer _playTimer = null!;

    // View Transforms (Now handled by OpenGL projection)
    private float _scale = 10f; // Pixels per mm
    private float _panX = 0;
    private float _panY = 0;
    
    // Internal class for Render Logic
    private class PreviewGLControl : OpenGLControl
    {
        public List<GCodeCommand>? Commands;
        public int CurrentIndex;
        public float ViewScale = 1f;
        public float PanX = 0f;
        public float PanY = 0f;
        public float RasterInterval = 0.1f;
        public bool ShowTravelMoves = true;

        public override void OnRender()
        {
            GL.glClearColor(1f, 1f, 1f, 1f); // White BG
            GL.glClear(GL.GL_COLOR_BUFFER_BIT);

            if (Commands == null || Commands.Count == 0) return;

            // Setup Projection
            GL.glMatrixMode(GL.GL_PROJECTION);
            GL.glLoadIdentity();
            // Origin is bottom-left in G-code usually, but screen is top-left. 
            // Let's keep specific coordinate system:
            // glOrtho(left, right, bottom, top, -1, 1)
            // We want (0,0) to be where Pan says, scaled by Scale.
            // Actually, simplest is: Map 0..Width to 0..Width/Scale
            
            float w = Width / ViewScale;
            float h = Height / ViewScale;
            // Center logic:
            // The view shows [PanX, PanX + w] x [PanY, PanY + h]
            // Standard Cartesian: Y up. GDI+ was Y down (Top-Left 0,0).
            // G-Code is usually Y up (Bottom-Left 0,0).
            // Let's use Y Up for OGL.
            
            GL.glOrtho(PanX, PanX + w, PanY, PanY + h, -1, 1);
            
            GL.glMatrixMode(GL.GL_MODELVIEW);
            GL.glLoadIdentity();

            // Set Line Width
            // GL standard line width is pixels. 
            // If we want physical width, we need to draw QUADS or ensure Scale acts on it.
            // standard glLineWidth is screen pixels.
            // User wanted "RasterLineInterval" thickness.
            // If Interval is 0.1mm, and Scale is 10px/mm, then width is 1px.
            
            float pixelWidth = Math.Max(1.0f, RasterInterval * ViewScale); 
            GL.glLineWidth(pixelWidth);

            GL.glEnable(GL.GL_BLEND);
            GL.glBlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);

            // Draw Full Path (Faint) - Future
            // OPTIMIZATION: Use Vertex Arrays if this is too slow. Initialize loop is OK for <100k points in immediate mode for modern CPUs.
            
            // 1. Travel Moves (Future)
            // 1. Travel Moves (Future)
            if (ShowTravelMoves)
            {
                GL.glColor3f(0.8f, 0.8f, 1.0f); // Light Blue
            }
            else
            {
                GL.glColor4f(0f, 0f, 0f, 0f); // Invisible
            }

            GL.glBegin(GL.GL_LINES);
            for (int i = 0; i < Commands.Count; i++)
            {
               var cmd = Commands[i];
               if (cmd.Type == CommandType.Travel && i > CurrentIndex)
               {
                   GL.glVertex2f(cmd.Start.X, cmd.Start.Y);
                   GL.glVertex2f(cmd.End.X, cmd.End.Y);
               }
            }
            GL.glEnd();

            // 2. Cut Moves (Future)
            GL.glBegin(GL.GL_LINES);
            for (int i = 0; i < Commands.Count; i++)
            {
               var cmd = Commands[i];
               if (cmd.Type == CommandType.Cut && i > CurrentIndex)
               {
                   float alpha = cmd.Power / 1000f;
                   if (alpha < 0.05f) alpha = 0.05f; // Min visibility
                   GL.glColor4f(0f, 0f, 0f, alpha * 0.5f); // Faint Black with power opacity

                   GL.glVertex2f(cmd.Start.X, cmd.Start.Y);
                   GL.glVertex2f(cmd.End.X, cmd.End.Y);
               }
            }
            GL.glEnd();

            // 3. Executed Moves
            GL.glBegin(GL.GL_LINES);
            for (int i = 0; i <= Math.Min(CurrentIndex, Commands.Count - 1); i++)
            {
                var cmd = Commands[i];
                if (cmd.Type == CommandType.Travel)
                {
                    if (ShowTravelMoves) GL.glColor3f(0f, 0f, 1f); // Blue
                    else GL.glColor4f(0f,0f,0f,0f);
                    
                    GL.glVertex2f(cmd.Start.X, cmd.Start.Y);
                    GL.glVertex2f(cmd.End.X, cmd.End.Y);
                }
                else if (cmd.Type == CommandType.Cut)
                {
                    // Power variable opacity
                    float alpha = cmd.Power / 1000f;
                    if (alpha < 0.1f) alpha = 0.1f;
                    GL.glColor4f(1f, 0f, 0f, alpha); // Red
                    GL.glVertex2f(cmd.Start.X, cmd.Start.Y);
                    GL.glVertex2f(cmd.End.X, cmd.End.Y);
                }
            }
            GL.glEnd();

            // 4. Head Position
            if (CurrentIndex < Commands.Count)
            {
                var cmd = Commands[CurrentIndex];
                var head = cmd.End;
                float size = 5f / ViewScale; // 5 screen pixels in world coords
                
                GL.glColor3f(1f, 0f, 0f);
                GL.glLineWidth(2f);
                GL.glBegin(GL.GL_LINES);
                GL.glVertex2f(head.X - size, head.Y - size);
                GL.glVertex2f(head.X + size, head.Y + size);
                GL.glVertex2f(head.X + size, head.Y - size);
                GL.glVertex2f(head.X - size, head.Y + size);
                GL.glEnd();
            }
        }
    }

    public PreviewForm(string gcode)
    {
        InitializeComponent();
        
        _commands = GCodeParser.Parse(gcode);
        _grid.DataSource = _commands;
        _renderArea.RasterInterval = Data.AppConfiguration.Instance.RasterLineInterval;
        
        if (_commands.Count > 0)
        {
             _timeline.Maximum = _commands.Count - 1;
             _renderArea.Commands = _commands;
             FitToScreen();
        }

        _playTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _playTimer.Tick += PlayTimer_Tick;
    }

    private void InitializeComponent()
    {
        this.Text = "Laser Preview (OpenGL)";
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
                    _renderArea.CurrentIndex = _currentIndex;
                    _renderArea.Invalidate();
                }
            }
        };

        _split.Panel1.Controls.Add(_grid);

        // Right: Render + Controls
        var rightPanel = new Panel { Dock = DockStyle.Fill };
        
        _controlsPanel = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.LightGray };
        
        // Initialize OpenGL Control
        _renderArea = new PreviewGLControl { Dock = DockStyle.Fill, BackColor = Color.White };
        
        // Render Area Events
        Point _lastMouse = Point.Empty;
        bool _isPanning = false;

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
                // In OpenGL orthographic, moving mouse right (positive X) should move View Left (PanX decreases)
                // But typically Pan represents the "Camera Position" or "Window Left".
                // If I drag mouse Left (-X), I want to see more Right. So Window Left increases.
                float dx = (e.X - _lastMouse.X) / _scale;
                float dy = (e.Y - _lastMouse.Y) / _scale; 
                
                // GDI+ Translate was: g.Translate(PanX, PanY).
                // If I moved mouse right (+50px), PanX increased +50. GDI+ shifted content Right.
                // Here, PanX is "Left Edge of View".
                // To shift content Right, we need to show content that is further Left. So PanX should Decrease.
                
                _panX -= dx;
                _panY += dy; // Y is inverted in screen vs world usually?
                // Screen Y is down. Mouse move down = +Y.
                // World Y is up.
                // If I drag mouse Down (+Y), I expect content to move Down.
                // If content moves down, I am seeing more "Top" content.
                // So Window Bottom (PanY) should Increase? 
                // Let's test standard behavior.
                
                _renderArea.PanX = _panX;
                _renderArea.PanY = _panY;
                
                _lastMouse = e.Location;
                _renderArea.Invalidate();
            }
        };
        _renderArea.MouseUp += (s, e) => { _isPanning = false; };
        _renderArea.MouseWheel += (s, e) => 
        {
            float factor = e.Delta > 0 ? 1.1f : 0.9f;
            // Zoom at Mouse Pointer?
            // Current World Pos at Mouse:
            // Wx = PanX + MouseX / Scale
            // New Scale = Scale * Factor
            // New PanX needs such that Wx = NewPanX + MouseX / NewScale
            // NewPanX = Wx - MouseX / NewScale
            
            float mouseWorldX = _panX + e.X / _scale;
            float mouseWorldY = _panY + (_renderArea.Height - e.Y) / _scale; // OGL Height flip

             _scale *= factor;
            
            _panX = mouseWorldX - e.X / _scale;
            _panY = mouseWorldY - (_renderArea.Height - e.Y) / _scale;

            _renderArea.ViewScale = _scale;
            _renderArea.PanX = _panX;
            _renderArea.PanY = _panY;
            _renderArea.Invalidate();
        };

        // Controls (Same as before)
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
            _renderArea.CurrentIndex = _currentIndex;
            _renderArea.Invalidate();
        };

        _btnPlay.Click += (s, e) => { _playTimer.Start(); };
        _btnPause.Click += (s, e) => { _playTimer.Stop(); };
        _btnStop.Click += (s, e) => { _playTimer.Stop(); _currentIndex = 0; UpdateSelection(); _renderArea.CurrentIndex = 0; _renderArea.Invalidate(); };

        _controlsPanel.Controls.Add(_btnPlay);
        _controlsPanel.Controls.Add(_btnPause);
        _controlsPanel.Controls.Add(_btnStop);
        _controlsPanel.Controls.Add(_numSpeed);
        _controlsPanel.Controls.Add(lblSpeed);
        _controlsPanel.Controls.Add(lblSpeed);
        _controlsPanel.Controls.Add(_timeline);
        
        var cbShowTravel = new CheckBox { Text = "Show Travel", Checked = true, Location = new Point(740, 10), AutoSize = true };
        cbShowTravel.CheckedChanged += (s, e) => 
        {
            _renderArea.ShowTravelMoves = cbShowTravel.Checked;
            _renderArea.Invalidate();
        };
        _controlsPanel.Controls.Add(cbShowTravel);

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
        _renderArea.CurrentIndex = _currentIndex;
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
        
        // Center: Pan X/Y are "Left/Bottom" of view in World coords
        // We want Center of Model to be Center of Screen.
        // Screen Center = [W/2, H/2]
        // Model Center = [MinX + w/2, MinY + h/2]
        
        // ViewWidth in World = ScreenWidth / Scale;
        
        float viewW = _renderArea.Width / _scale;
        float viewH = _renderArea.Height / _scale;
        
        _panX = (minX + w / 2) - viewW / 2;
        _panY = (minY + h / 2) - viewH / 2;
        
        _renderArea.ViewScale = _scale;
        _renderArea.PanX = _panX;
        _renderArea.PanY = _panY;
        _renderArea.Invalidate();
    }
}
