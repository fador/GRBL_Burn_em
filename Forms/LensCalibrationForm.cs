/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using grbl_burn_em.Data;

using static grbl_burn_em.Data.CameraCalibrationEngine;

namespace grbl_burn_em.Forms;

public partial class LensCalibrationForm : Form
{
    private PictureBox _picPreview = null!;
    private Label _lblStatus = null!;
    private Label _lblCount = null!;
    private Label _lblResults = null!;
    private Button _btnCapture = null!;
    private Button _btnAutoCapture = null!;
    private Button _btnCalibrate = null!;
    private Button _btnSave = null!;
    private Button _btnCancelScan = null!;

    private NumericUpDown _nudGridRows = null!;
    private NumericUpDown _nudGridCols = null!;
    private NumericUpDown _nudStepSize = null!;
    private NumericUpDown _nudFeedRate = null!;
    private ComboBox _cmbDirection = null!;

    private bool _captureCancelled;
    private CancellationTokenSource? _captureCts;

    private readonly List<Mat> _capturedFrames = new();
    private bool _calibrated;
    private CameraIntrinsics? _result;

    public LensCalibrationForm()
    {
        InitializeComponent();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!CameraManager.Instance.IsRunning)
            _lblStatus.Text = "Camera not running. Start camera first.";
    }

    private void InitializeComponent()
    {
        Text = "Lens Calibration (ChArUco)";
        Size = new Size(900, 780);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        mainLayout.Controls.Add(_picPreview, 0, 0);

        var sidePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(10) };
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _lblCount = new Label { Text = "0 views captured (need 6+)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblCount, 0, 0);

        _lblStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblStatus, 0, 1);

        var gridGroup = new GroupBox { Text = "Auto Capture Grid", Dock = DockStyle.Fill };
        var gridLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        gridLayout.Controls.Add(new Label { Text = "Rows:" }, 0, 0);
        _nudGridRows = new NumericUpDown { Minimum = 2, Maximum = 10, Value = 5, Width = 60 };
        gridLayout.Controls.Add(_nudGridRows, 1, 0);

        gridLayout.Controls.Add(new Label { Text = "Cols:" }, 0, 1);
        _nudGridCols = new NumericUpDown { Minimum = 2, Maximum = 10, Value = 5, Width = 60 };
        gridLayout.Controls.Add(_nudGridCols, 1, 1);

        gridLayout.Controls.Add(new Label { Text = "Step (mm):" }, 0, 2);
        _nudStepSize = new NumericUpDown { Minimum = 5, Maximum = 200, Value = 60, Width = 60 };
        gridLayout.Controls.Add(_nudStepSize, 1, 2);

        gridLayout.Controls.Add(new Label { Text = "Speed:" }, 0, 3);
        _nudFeedRate = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = 1000, Width = 70 };
        gridLayout.Controls.Add(_nudFeedRate, 1, 3);

        gridLayout.Controls.Add(new Label { Text = "Dir:" }, 0, 4);
        _cmbDirection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        _cmbDirection.Items.AddRange(new[] { "Row-major", "Snake" });
        _cmbDirection.SelectedIndex = 0;
        gridLayout.Controls.Add(_cmbDirection, 1, 4);
        gridGroup.Controls.Add(gridLayout);
        sidePanel.Controls.Add(gridGroup, 0, 2);

        _btnCapture = new Button { Text = "Capture Current View", Dock = DockStyle.Fill, Height = 35 };
        _btnCapture.Click += (s, e) => CaptureCurrentView();
        sidePanel.Controls.Add(_btnCapture, 0, 3);

        _btnAutoCapture = new Button { Text = "Auto Capture (scan grid)", Dock = DockStyle.Fill, Height = 35, BackColor = Color.LightGreen };
        _btnAutoCapture.Click += async (s, e) => await AutoCapture();
        sidePanel.Controls.Add(_btnAutoCapture, 0, 4);

        var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _btnCalibrate = new Button { Text = "Calibrate", Width = 100, Height = 30, Enabled = false };
        _btnCalibrate.Click += (s, e) => RunCalibration();
        _btnSave = new Button { Text = "Save", Width = 80, Height = 30, Enabled = false };
        _btnSave.Click += (s, e) => SaveCalibration();
        _btnCancelScan = new Button { Text = "Stop", Width = 80, Height = 30, Enabled = false };
        _btnCancelScan.Click += (s, e) => { _captureCancelled = true; _captureCts?.Cancel(); };
        var btnClose = new Button { Text = "Close", Width = 80, Height = 30 };
        btnClose.Click += (s, e) => { _captureCancelled = true; _captureCts?.Cancel(); Close(); };
        pnlBtns.Controls.Add(_btnCalibrate);
        pnlBtns.Controls.Add(_btnSave);
        pnlBtns.Controls.Add(_btnCancelScan);
        pnlBtns.Controls.Add(btnClose);
        sidePanel.Controls.Add(pnlBtns, 0, 5);

        _lblResults = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter };
        sidePanel.Controls.Add(_lblResults, 0, 6);

        sidePanel.Controls.Add(new Label { Text = "Tip: Move machine so board is in camera view, then use Auto Capture.\nEmulator: check 'Draw ChArUco board' and set board Y=0.", Font = new Font("Arial", 7), ForeColor = Color.Gray, AutoSize = true }, 0, 7);

        var movePanel = new Panel { Dock = DockStyle.Fill };
        var moveLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        moveLayout.Controls.Add(new Label { Text = "Go to:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        var nudGoX = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 1, Width = 55, Value = 0 };
        var nudGoY = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 1, Width = 55, Value = 0 };
        var btnGo = new Button { Text = "Go", Width = 40, Height = 23 };
        btnGo.Click += (s, e) =>
        {
            if (!SerialInterface.Instance.IsConnected) return;
            string cmd = string.Create(CultureInfo.InvariantCulture,
                $"$J=G90 X{(float)nudGoX.Value:F1} Y{(float)nudGoY.Value:F1} F2000");
            SerialInterface.Instance.Write(cmd + "\n");
        };
        moveLayout.Controls.Add(nudGoX);
        moveLayout.Controls.Add(nudGoY);
        moveLayout.Controls.Add(btnGo);
        movePanel.Controls.Add(moveLayout);
        sidePanel.Controls.Add(movePanel, 0, 8);

        mainLayout.Controls.Add(sidePanel, 1, 0);
        Controls.Add(mainLayout);

        CameraManager.Instance.FrameReceived += OnFrameReceived;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CameraManager.Instance.FrameReceived -= OnFrameReceived;
            foreach (var frame in _capturedFrames) frame.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnFrameReceived(Bitmap frame)
    {
        if (!IsHandleCreated) { frame.Dispose(); return; }
        try
        {
            using var mat = BitmapToMat(frame);
            frame.Dispose();

            var store = CalibrationStore.Load();
            if (store.BoardConfig == null) return;

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            var detection = engine.DetectBoard(mat);

            if (_picPreview.IsDisposed) return;

            using var display = mat.Clone();
            engine.DrawDetectedBoard(display, detection);

            var displayBmp = MatToBitmap(display);
            this.BeginInvoke(new Action(() =>
            {
                if (!_picPreview.IsDisposed)
                {
                    var old = _picPreview.Image;
                    _picPreview.Image = displayBmp;
                    old?.Dispose();

                    if (detection.Detected)
                        _lblStatus.Text = $"Board DETECTED: {detection.CharucoIds?.Size ?? 0} corners, {detection.MarkerIds?.Size ?? 0} markers (need 4+)";
                    else if (detection.MarkerIds?.Size > 0)
                        _lblStatus.Text = $"{detection.MarkerIds.Size} markers found (need 4+) - board not detected";
                    else
                        _lblStatus.Text = "Board not detected";
                }
                else
                {
                    displayBmp.Dispose();
                }
            }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Preview error: {ex.Message}");
            frame.Dispose();
        }
    }

    private void CaptureCurrentView()
    {
        if (_picPreview.Image == null) return;

        try
        {
            var store = CalibrationStore.Load();
            if (store.BoardConfig == null)
            {
                MessageBox.Show(this, "Please set up ChArUco board first.", "Warning");
                return;
            }

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            using var bmp = new Bitmap(_picPreview.Image);
            using var mat = BitmapToMat(bmp);
            var detection = engine.DetectBoard(mat);

            if (!detection.Detected)
            {
                MessageBox.Show(this, "ChArUco board not detected in this frame.\nEnsure the board is visible in the camera view.", "Warning");
                return;
            }

            _capturedFrames.Add(mat.Clone());
            _lblCount.Text = $"{_capturedFrames.Count} views (need 6+)";
            _btnCalibrate.Enabled = _capturedFrames.Count >= 6;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Capture failed: {ex.Message}", "Error");
        }
    }

    private bool TryCaptureBoard()
    {
        if (_picPreview.Image == null) return false;

        try
        {
            var store = CalibrationStore.Load();
            if (store.BoardConfig == null) return false;

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            using var bmp = new Bitmap(_picPreview.Image);
            using var mat = BitmapToMat(bmp);
            var detection = engine.DetectBoard(mat);

            if (detection.Detected)
            {
                _capturedFrames.Add(mat.Clone());
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task AutoCapture()
    {
        var store = CalibrationStore.Load();
        if (store.BoardConfig == null)
        {
            if (!IsDisposed) MessageBox.Show(this, "Please set up ChArUco board first.", "Warning");
            return;
        }

        if (!SerialInterface.Instance.IsConnected)
        {
            if (!IsDisposed) MessageBox.Show(this, "Machine not connected. Click 'Connect Emulator' in Camera Settings.", "Warning");
            return;
        }

        _captureCancelled = false;
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;
        _btnAutoCapture.Enabled = false;
        _btnCapture.Enabled = false;
        _btnCancelScan.Enabled = true;

        int rows = (int)_nudGridRows.Value;
        int cols = (int)_nudGridCols.Value;
        float step = (float)_nudStepSize.Value;
        float feedRate = (float)_nudFeedRate.Value;
        bool snake = _cmbDirection.SelectedIndex == 1;
        int total = rows * cols;

        var startPos = SerialInterface.Instance.MachinePosition;
        float startX = startPos.X;
        float startY = startPos.Y;

        try
        {
            for (int r = 0; r < rows; r++)
            {
                int colStart = (snake && r % 2 == 1) ? cols - 1 : 0;
                int colEnd = (snake && r % 2 == 1) ? -1 : cols;
                int colStep = (snake && r % 2 == 1) ? -1 : 1;

                for (int c = colStart; c != colEnd; c += colStep)
                {
                    if (_captureCancelled || token.IsCancellationRequested) break;

                    float dx = (c - (cols - 1) / 2f) * step;
                    float dy = (r - (rows - 1) / 2f) * step;
                    float targetX = startX + dx;
                    float targetY = startY + dy;

                    string cmd = string.Create(CultureInfo.InvariantCulture,
                        $"$J=G90 X{targetX:F1} Y{targetY:F1} F{feedRate:F0}");
                    SerialInterface.Instance.Write(cmd + "\n");

                    try { await WaitForIdle(15000, token); }
                    catch (OperationCanceledException) { break; }
                    await Task.Delay(250, token);

                    bool detected = TryCaptureBoard();
                    int idx = r * cols + c + 1;
                    string mark = detected ? "✓" : "✗";
                    this.BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        _lblStatus.Text = $"Grid {idx}/{total} {mark} ({targetX:F0},{targetY:F0})";
                        _lblCount.Text = $"{_capturedFrames.Count} views (need 6+)";
                        _btnCalibrate.Enabled = _capturedFrames.Count >= 6;
                    }));
                }
                if (_captureCancelled || token.IsCancellationRequested) break;
            }

            string cmdReturn = string.Create(CultureInfo.InvariantCulture,
                $"$J=G90 X{startX:F1} Y{startY:F1} F{feedRate:F0}");
            SerialInterface.Instance.Write(cmdReturn + "\n");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed)
                MessageBox.Show(this, $"Auto capture error: {ex.Message}", "Error");
        }
        finally
        {
            _captureCts?.Dispose();
            _captureCts = null;
            if (!IsDisposed)
            {
                _btnAutoCapture.Enabled = true;
                _btnCapture.Enabled = true;
                _btnCancelScan.Enabled = false;
                _lblStatus.Text = _captureCancelled
                    ? $"Cancelled. {_capturedFrames.Count} views captured."
                    : $"Done. {_capturedFrames.Count} views captured.";
            }
        }
    }

    private static async Task WaitForIdle(int timeoutMs, CancellationToken token)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            if (!SerialInterface.Instance.IsConnected) return;
            if (SerialInterface.Instance.MachineState.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(100, token);
            waited += 100;
        }
    }

    private void RunCalibration()
    {
        try
        {
            var store = CalibrationStore.Load();
            if (store.BoardConfig == null) return;

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            var result = engine.CalibrateLens(_capturedFrames);

            if (result == null)
            {
                MessageBox.Show(this, "Calibration failed. Need 6+ valid views with detected board.", "Error");
                return;
            }

            _result = result;
            _calibrated = true;
            _btnSave.Enabled = true;

            _lblResults.Text = $"Calibrated! {result.UsedViewCount} views\nRMSE: {result.ReprojectionError:F4} px\n" +
                $"fx={result.CameraMatrix[0]:F1} fy={result.CameraMatrix[4]:F1}\n" +
                $"cx={result.CameraMatrix[2]:F1} cy={result.CameraMatrix[5]:F1}\n" +
                $"k1={result.DistCoeffs[0]:F4} k2={result.DistCoeffs[1]:F4}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Calibration error: {ex.Message}", "Error");
        }
    }

    private void SaveCalibration()
    {
        if (_result == null) return;

        var store = CalibrationStore.Load();
        store.Intrinsics = _result;
        store.Save();
        MessageBox.Show(this, "Lens calibration saved.", "Saved");
        DialogResult = DialogResult.OK;
        Close();
    }

    public IReadOnlyList<Mat> CapturedFrames => _capturedFrames;
    public bool IsCalibrated => _calibrated;
}
