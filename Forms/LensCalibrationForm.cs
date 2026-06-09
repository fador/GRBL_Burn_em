/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
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

    private NumericUpDown _nudGridRows = null!;
    private NumericUpDown _nudGridCols = null!;
    private NumericUpDown _nudStepSize = null!;

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
        Size = new Size(800, 700);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        mainLayout.Controls.Add(_picPreview, 0, 0);

        var sidePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(10) };
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _lblCount = new Label { Text = "0 views captured (need 5+)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblCount, 0, 0);

        _lblStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblStatus, 0, 1);

        var gridGroup = new GroupBox { Text = "Auto Capture Grid", Dock = DockStyle.Fill };
        var gridLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        gridLayout.Controls.Add(new Label { Text = "Rows:" }, 0, 0);
        _nudGridRows = new NumericUpDown { Minimum = 2, Maximum = 10, Value = 5, Width = 60 };
        gridLayout.Controls.Add(_nudGridRows, 1, 0);

        gridLayout.Controls.Add(new Label { Text = "Cols:" }, 0, 1);
        _nudGridCols = new NumericUpDown { Minimum = 2, Maximum = 10, Value = 5, Width = 60 };
        gridLayout.Controls.Add(_nudGridCols, 1, 1);

        gridLayout.Controls.Add(new Label { Text = "Step (mm):" }, 0, 2);
        _nudStepSize = new NumericUpDown { Minimum = 5, Maximum = 200, Value = 30, Width = 60 };
        gridLayout.Controls.Add(_nudStepSize, 1, 2);
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
        var btnCancel = new Button { Text = "Cancel", Width = 80, Height = 30 };
        btnCancel.Click += (s, e) => Close();
        pnlBtns.Controls.Add(_btnCalibrate);
        pnlBtns.Controls.Add(_btnSave);
        pnlBtns.Controls.Add(btnCancel);
        sidePanel.Controls.Add(pnlBtns, 0, 5);

        _lblResults = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter };
        sidePanel.Controls.Add(_lblResults, 0, 6);

        sidePanel.Controls.Add(new Label { Text = "Tip: Move machine so board is in camera view, then use Auto Capture.\nEmulator: check 'Draw ChArUco board' and set board Y=0.", Font = new Font("Arial", 7), ForeColor = Color.Gray, AutoSize = true }, 0, 7);

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
                        _lblStatus.Text = $"Board DETECTED: {detection.CharucoIds?.Size ?? 0} corners, {detection.MarkerIds?.Size ?? 0} markers";
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
            _lblCount.Text = $"{_capturedFrames.Count} views (need 5+)";
            _btnCalibrate.Enabled = _capturedFrames.Count >= 3;
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
            MessageBox.Show(this, "Please set up ChArUco board first.", "Warning");
            return;
        }

        _btnAutoCapture.Enabled = false;
        _btnCapture.Enabled = false;

        int rows = (int)_nudGridRows.Value;
        int cols = (int)_nudGridCols.Value;
        float step = (float)_nudStepSize.Value;
        int totalPoints = rows * cols;

        try
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float dx = (c - (cols - 1) / 2f) * step;
                    float dy = (r - (rows - 1) / 2f) * step;

                    if (SerialInterface.Instance.IsConnected)
                    {
                        string cmd = $"$J=G91 X{dx:F1} Y{dy:F1} F1000";
                        SerialInterface.Instance.Write(cmd + "\n");
                    }

                    await Task.Delay(600);

                    bool detected = TryCaptureBoard();
                    int idx = r * cols + c + 1;
                    string mark = detected ? "✓" : "✗";
                    this.BeginInvoke(new Action(() =>
                    {
                        _lblStatus.Text = $"Grid {idx}/{totalPoints} {mark} (dx={dx:F0}, dy={dy:F0})";
                        _lblCount.Text = $"{_capturedFrames.Count} views (need 5+)";
                        _btnCalibrate.Enabled = _capturedFrames.Count >= 3;
                    }));

                    if (!SerialInterface.Instance.IsConnected)
                    {
                        string cmdRevert = $"$J=G91 X{-dx:F1} Y{-dy:F1} F1000";
                        SerialInterface.Instance.Write(cmdRevert + "\n");
                        await Task.Delay(300);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Auto capture error: {ex.Message}", "Error");
        }
        finally
        {
            _btnAutoCapture.Enabled = true;
            _btnCapture.Enabled = true;
            _lblStatus.Text = $"Done. {_capturedFrames.Count} views captured.";
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
                MessageBox.Show(this, "Calibration failed. Need 3+ valid views with detected board.", "Error");
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
