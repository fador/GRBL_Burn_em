using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.Structure;

namespace grbl_burn_em_emulator;

public partial class EmulatorForm : Form
{
    private PictureBox _workArea = null!;
    private TextBox _logBox = null!;
    private Bitmap _bedBitmap = null!;
    private Graphics _bedGraphics = null!;

    private NumericUpDown _nudFovW = null!;
    private NumericUpDown _nudFovH = null!;
    private NumericUpDown _nudCamX = null!;
    private NumericUpDown _nudCamY = null!;
    private NumericUpDown _nudCamZ = null!;
    private NumericUpDown _nudResX = null!;
    private NumericUpDown _nudResY = null!;
    private CheckBox _chkCrosshair = null!;
    private CheckBox _chkDistort = null!;
    private NumericUpDown _nudK1 = null!;
    private NumericUpDown _nudK2 = null!;
    private NumericUpDown _nudNoise = null!;

    private CheckBox _chkCharuco = null!;
    private NumericUpDown _nudBoardX = null!;
    private NumericUpDown _nudBoardY = null!;
    private NumericUpDown _nudBoardSquares = null!;
    private NumericUpDown _nudBoardSize = null!;
    private Button _btnDrawBoard = null!;

    private float _scale = 1.5f;
    private int _bedWidth;
    private int _bedHeight;

    private float _workW = 400f;
    private float _workH = 400f;

    private bool _drawCharuco;
    private float _boardX, _boardY;

    public EmulatorForm()
    {
        InitializeComponent();
        SetupUI();
        InitBed();
        WireEvents();
        StartServers();
    }

    private void InitBed()
    {
        _bedWidth = (int)(_workW * _scale);
        _bedHeight = (int)(_workH * _scale);
        _bedBitmap = new Bitmap(_bedWidth, _bedHeight);
        _bedGraphics = Graphics.FromImage(_bedBitmap);
        _bedGraphics.Clear(Color.Beige);
    }

    private void WireEvents()
    {
        EmulatorLogic.Instance.LogMessage += OnLog;
        EmulatorLogic.Instance.StateChanged += () => _workArea?.Invalidate();
        EmulatorLogic.Instance.BurnMark += OnBurnMark;
        EmulatorLogic.Instance.WorkAreaWidth = _workW;
        EmulatorLogic.Instance.WorkAreaHeight = _workH;
    }

    private void StartServers()
    {
        TcpServer.Instance.Log += OnLog;
        TcpServer.Instance.Start(2345);

        CameraServer.Instance.CaptureProvider = () =>
        {
            lock (_bedBitmap)
            {
                return VirtualCamera.Instance.Capture(_bedBitmap, _scale);
            }
        };
        CameraServer.Instance.Start(2346);

        var timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (s, e) => _workArea?.Invalidate();
        timer.Start();

        LogMessage("Emulator started. Serial:2345 Camera:2346");
    }

    private void LogMessage(string msg)
    {
        if (InvokeRequired) { Invoke(new Action<string>(LogMessage), msg); return; }
        _logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\r\n");
    }

    private void OnLog(string msg)
    {
        if (InvokeRequired) { Invoke(new Action<string>(OnLog), msg); return; }
        _logBox.AppendText(msg + "\r\n");
    }

    private void OnBurnMark(float x, float y, float power)
    {
        lock (_bedBitmap)
        {
            float sx = x * _scale;
            float sy = _bedHeight - (y * _scale);
            if (sx >= 0 && sx < _bedWidth && sy >= 0 && sy < _bedHeight)
            {
                float intensity = Math.Clamp(power / 1000f, 0f, 1f);
                int alpha = (int)(intensity * 255);
                if (alpha > 5)
                {
                    using var brush = new SolidBrush(Color.FromArgb(alpha, 30, 30, 30));
                    _bedGraphics.FillRectangle(brush, sx, sy, 2, 2);
                }
            }
        }
    }

    private void SetupUI()
    {
        Text = "GRBL Burn Em Emulator";
        Size = new Size(1100, 800);
        MinimumSize = new Size(800, 600);

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 750
        };
        Controls.Add(mainSplit);

        var leftSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 550
        };
        mainSplit.Panel1.Controls.Add(leftSplit);

        _workArea = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Gray,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _workArea.Paint += WorkAreaPaint;
        leftSplit.Panel1.Controls.Add(_workArea);

        var logPanel = new Panel { Dock = DockStyle.Fill };
        _logBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9)
        };
        logPanel.Controls.Add(_logBox);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.LeftToRight };
        var btnClear = new Button { Text = "Clear Bed" };
        btnClear.Click += (s, e) => { lock (_bedBitmap) _bedGraphics.Clear(Color.Beige); _workArea.Invalidate(); };
        var btnClearLog = new Button { Text = "Clear Log" };
        btnClearLog.Click += (s, e) => _logBox.Clear();
        var btnHome = new Button { Text = "Home (0,0)" };
        btnHome.Click += (s, e) =>
        {
            EmulatorLogic.Instance.X = 0; EmulatorLogic.Instance.Y = 0; EmulatorLogic.Instance.Z = 0;
            LogMessage("Manual home to 0,0");
        };
        btnPanel.Controls.Add(btnClear);
        btnPanel.Controls.Add(btnClearLog);
        btnPanel.Controls.Add(btnHome);
        logPanel.Controls.Add(btnPanel);
        leftSplit.Panel2.Controls.Add(logPanel);

        var rightPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        BuildSettingsPanel(rightPanel);
        mainSplit.Panel2.Controls.Add(rightPanel);
    }

    private void BuildSettingsPanel(Panel parent)
    {
        int y = 10;
        var font = new Font("Arial", 9, FontStyle.Bold);

        AddLabel(parent, "Camera Settings", font, ref y);
        (_nudFovW, y) = AddNumeric(parent, "FOV Width (mm):", VirtualCamera.Instance.FovWidth, 1, 500, ref y);
        _nudFovW.ValueChanged += (s, e) => VirtualCamera.Instance.FovWidth = (float)_nudFovW.Value;

        (_nudFovH, y) = AddNumeric(parent, "FOV Height (mm):", VirtualCamera.Instance.FovHeight, 1, 500, ref y);
        _nudFovH.ValueChanged += (s, e) => VirtualCamera.Instance.FovHeight = (float)_nudFovH.Value;

        (_nudCamX, y) = AddNumeric(parent, "Cam Offset X (mm):", VirtualCamera.Instance.OffsetX, -500, 500, ref y);
        _nudCamX.ValueChanged += (s, e) => VirtualCamera.Instance.OffsetX = (float)_nudCamX.Value;

        (_nudCamY, y) = AddNumeric(parent, "Cam Offset Y (mm):", VirtualCamera.Instance.OffsetY, -500, 500, ref y);
        _nudCamY.ValueChanged += (s, e) => VirtualCamera.Instance.OffsetY = (float)_nudCamY.Value;

        (_nudCamZ, y) = AddNumeric(parent, "Cam Height Z (mm):", VirtualCamera.Instance.OffsetZ, 10, 500, ref y);
        _nudCamZ.ValueChanged += (s, e) => VirtualCamera.Instance.OffsetZ = (float)_nudCamZ.Value;

        (_nudResX, y) = AddNumeric(parent, "Resolution X:", VirtualCamera.Instance.ResX, 160, 3840, ref y);
        _nudResX.ValueChanged += (s, e) => VirtualCamera.Instance.ResX = (int)_nudResX.Value;

        (_nudResY, y) = AddNumeric(parent, "Resolution Y:", VirtualCamera.Instance.ResY, 120, 2160, ref y);
        _nudResY.ValueChanged += (s, e) => VirtualCamera.Instance.ResY = (int)_nudResY.Value;

        y += 5;
        _chkCrosshair = new CheckBox { Text = "Draw crosshair", Checked = true, Left = 10, Top = y, Width = 200 };
        _chkCrosshair.CheckedChanged += (s, e) => VirtualCamera.Instance.DrawCrosshair = _chkCrosshair.Checked;
        parent.Controls.Add(_chkCrosshair);
        y += 25;

        AddLabel(parent, "Distortion", font, ref y);
        _chkDistort = new CheckBox { Text = "Simulate lens distortion", Left = 10, Top = y, Width = 200 };
        _chkDistort.CheckedChanged += (s, e) => VirtualCamera.Instance.SimulateDistortion = _chkDistort.Checked;
        parent.Controls.Add(_chkDistort);
        y += 25;

        (_nudK1, y) = AddNumeric(parent, "k1:", 0, -0.5m, 0.5m, 4, ref y);
        _nudK1.ValueChanged += (s, e) => VirtualCamera.Instance.DistortionK1 = (float)_nudK1.Value;

        (_nudK2, y) = AddNumeric(parent, "k2:", 0, -0.5m, 0.5m, 4, ref y);
        _nudK2.ValueChanged += (s, e) => VirtualCamera.Instance.DistortionK2 = (float)_nudK2.Value;

        (_nudNoise, y) = AddNumeric(parent, "Noise:", VirtualCamera.Instance.NoiseLevel, 0, 50, ref y);
        _nudNoise.ValueChanged += (s, e) => VirtualCamera.Instance.NoiseLevel = (float)_nudNoise.Value;

        y += 10;
        AddLabel(parent, "ChArUco Board", font, ref y);
        _chkCharuco = new CheckBox { Text = "Draw ChArUco board on bed", Left = 10, Top = y, Width = 200 };
        _chkCharuco.CheckedChanged += (s, e) => { _drawCharuco = _chkCharuco.Checked; if (_drawCharuco) DrawCharucoBoard(); _workArea.Invalidate(); };
        parent.Controls.Add(_chkCharuco);
        y += 25;

        (_nudBoardSquares, y) = AddNumeric(parent, "Squares:", 5, 3, 10, 0, ref y);
        _nudBoardSquares.ValueChanged += (s, e) => { if (_drawCharuco) DrawCharucoBoard(); };
        (_nudBoardSize, y) = AddNumeric(parent, "Board size (mm):", 120, 30, 500, ref y);
        _nudBoardSize.ValueChanged += (s, e) => { if (_drawCharuco) DrawCharucoBoard(); };
        (_nudBoardX, y) = AddNumeric(parent, "Board X (mm):", 50, 0, 380, ref y);
        _nudBoardX.ValueChanged += (s, e) => { _boardX = (float)_nudBoardX.Value; if (_drawCharuco) DrawCharucoBoard(); };
        (_nudBoardY, y) = AddNumeric(parent, "Board Y (mm):", 50, 0, 380, ref y);
        _nudBoardY.ValueChanged += (s, e) => { _boardY = (float)_nudBoardY.Value; if (_drawCharuco) DrawCharucoBoard(); };

        _btnDrawBoard = new Button { Text = "Redraw Board", Left = 10, Top = y, Width = 200 };
        _btnDrawBoard.Click += (s, e) => { if (_drawCharuco) DrawCharucoBoard(); };
        parent.Controls.Add(_btnDrawBoard);
        y += 35;

        y += 10;
        AddLabel(parent, "Jog Controls", font, ref y);
        BuildJogControls(parent, ref y);
    }

    private void BuildJogControls(Panel parent, ref int y)
    {
        var jogTable = new TableLayoutPanel { Left = 10, Top = y, Width = 260, Height = 150, ColumnCount = 3, RowCount = 3 };
        jogTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        jogTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        jogTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        jogTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        jogTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        jogTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33));

        var stepNud = new NumericUpDown { Minimum = 0.1m, Maximum = 100, Value = 10, DecimalPlaces = 1, Width = 60, Dock = DockStyle.Fill };
        var feedNud = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = 1000, DecimalPlaces = 0, Width = 70, Dock = DockStyle.Fill };

        Action<float, float> jog = (dx, dy) =>
        {
            float step = (float)stepNud.Value;
            float feed = (float)feedNud.Value;
            string cmd = $"$J=G91 X{dx * step} Y{dy * step} F{feed}";
            EmulatorLogic.Instance.ParseLine(cmd);
        };

        jogTable.Controls.Add(CreateJogBtn("↖", -1, 1, jog), 0, 0);
        jogTable.Controls.Add(CreateJogBtn("↑", 0, 1, jog), 1, 0);
        jogTable.Controls.Add(CreateJogBtn("↗", 1, 1, jog), 2, 0);
        jogTable.Controls.Add(CreateJogBtn("←", -1, 0, jog), 0, 1);
        jogTable.Controls.Add(new Label { Text = "JOG", TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill }, 1, 1);
        jogTable.Controls.Add(CreateJogBtn("→", 1, 0, jog), 2, 1);
        jogTable.Controls.Add(CreateJogBtn("↙", -1, -1, jog), 0, 2);
        jogTable.Controls.Add(CreateJogBtn("↓", 0, -1, jog), 1, 2);
        jogTable.Controls.Add(CreateJogBtn("↘", 1, -1, jog), 2, 2);

        parent.Controls.Add(jogTable);
        y += 160;

        var pnlParams = new FlowLayoutPanel { Left = 10, Top = y, Width = 260, Height = 30, FlowDirection = FlowDirection.LeftToRight };
        pnlParams.Controls.Add(new Label { Text = "Step:", AutoSize = true });
        pnlParams.Controls.Add(stepNud);
        pnlParams.Controls.Add(new Label { Text = "Feed:", AutoSize = true });
        pnlParams.Controls.Add(feedNud);
        parent.Controls.Add(pnlParams);
        y += 40;
    }

    private static Button CreateJogBtn(string text, float dx, float dy, Action<float, float> jog)
    {
        var btn = new Button { Text = text, Dock = DockStyle.Fill, Font = new Font("Arial", 11, FontStyle.Bold) };
        btn.Click += (s, e) => jog(dx, dy);
        return btn;
    }

    private static void AddLabel(Panel parent, string text, Font font, ref int y)
    {
        var lbl = new Label { Text = text, Font = font, Left = 10, Top = y, Width = 250, AutoSize = true };
        parent.Controls.Add(lbl);
        y += 22;
    }

    private static (NumericUpDown, int) AddNumeric(Panel parent, string label, float value, float min, float max, ref int y)
    {
        var lbl = new Label { Text = label, Left = 10, Top = y, Width = 150, AutoSize = true };
        parent.Controls.Add(lbl);
        var nud = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)value, DecimalPlaces = 1, Left = 160, Top = y, Width = 100 };
        parent.Controls.Add(nud);
        y += 25;
        return (nud, y);
    }

    private static (NumericUpDown, int) AddNumeric(Panel parent, string label, decimal value, decimal min, decimal max, int decimals, ref int y)
    {
        var lbl = new Label { Text = label, Left = 10, Top = y, Width = 150, AutoSize = true };
        parent.Controls.Add(lbl);
        var nud = new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Left = 160, Top = y, Width = 100 };
        parent.Controls.Add(nud);
        y += 25;
        return (nud, y);
    }

    private void DrawCharucoBoard()
    {
        lock (_bedBitmap)
        {
            int squares = (int)_nudBoardSquares.Value;
            float boardSizeMm = (float)_nudBoardSize.Value;
            float squareSizeMm = boardSizeMm / squares;
            float markerSizeMm = squareSizeMm * 0.7f;

            int boardPx = (int)(boardSizeMm * _scale);
            int bx = (int)(_boardX * _scale);
            int by = _bedHeight - (int)(_boardY * _scale) - boardPx;

            using var g = Graphics.FromImage(_bedBitmap);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var dict = new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_50);
            var board = new CharucoBoard(squares, squares, squareSizeMm, markerSizeMm, dict);

            int pxPerSquare = 80;
            int margin = pxPerSquare;
            int imgW = squares * pxPerSquare + 2 * margin;
            int imgH = squares * pxPerSquare + 2 * margin;

            using var boardImg = new Mat();
            ArucoInvoke.GenerateImage(board, new Size(imgW, imgH), boardImg, margin, 1);

            int channels = boardImg.NumberOfChannels;
            int boardW = boardImg.Width;
            int boardH = boardImg.Height;

            using var srcBmp = new Bitmap(boardW, boardH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var bd = srcBmp.LockBits(new Rectangle(0, 0, boardW, boardH),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, srcBmp.PixelFormat);

            if (channels == 1)
            {
                for (int y = 0; y < boardH; y++)
                {
                    IntPtr src = boardImg.DataPointer + y * boardImg.Step;
                    IntPtr dst = bd.Scan0 + y * bd.Stride;
                    for (int x = 0; x < boardW; x++)
                    {
                        byte v = System.Runtime.InteropServices.Marshal.ReadByte(src + x);
                        byte inv = (byte)(255 - v);
                        System.Runtime.InteropServices.Marshal.WriteByte(dst + x * 3, inv);
                        System.Runtime.InteropServices.Marshal.WriteByte(dst + x * 3 + 1, inv);
                        System.Runtime.InteropServices.Marshal.WriteByte(dst + x * 3 + 2, inv);
                    }
                }
            }
            srcBmp.UnlockBits(bd);

            g.DrawImage(srcBmp, bx, by, boardPx, boardPx);
        }
        _workArea.Invalidate();
    }

    private void WorkAreaPaint(object? sender, PaintEventArgs e)
    {
        lock (_bedBitmap)
        {
            e.Graphics.DrawImage(_bedBitmap, 0, 0);
        }

        float lx = EmulatorLogic.Instance.X * _scale;
        float ly = _bedHeight - (EmulatorLogic.Instance.Y * _scale);

        e.Graphics.DrawLine(Pens.Red, lx - 15, ly, lx + 15, ly);
        e.Graphics.DrawLine(Pens.Red, lx, ly - 15, lx, ly + 15);

        var logic = EmulatorLogic.Instance;
        e.Graphics.DrawString($"Pos: {logic.X:F1}, {logic.Y:F1}   State: {logic.State}   Laser: {(logic.IsLaserOn ? "ON" : "OFF")}   S={logic.SpindleSpeed:F0}",
            SystemFonts.DefaultFont, Brushes.Black, 10, 10);

        float camX = lx + VirtualCamera.Instance.OffsetX * _scale;
        float camY = ly - VirtualCamera.Instance.OffsetY * _scale;
        float camW = VirtualCamera.Instance.FovWidth * _scale;
        float camH = VirtualCamera.Instance.FovHeight * _scale;
        e.Graphics.DrawRectangle(Pens.Lime, camX - camW / 2, camY - camH / 2, camW, camH);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        Name = "EmulatorForm";
        ResumeLayout(false);
    }
}
