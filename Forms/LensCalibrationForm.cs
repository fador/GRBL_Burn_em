/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
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
        Size = new Size(750, 650);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        mainLayout.Controls.Add(_picPreview, 0, 0);

        var sidePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10) };
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        _lblCount = new Label { Text = "0 views captured (need 5+)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblCount, 0, 0);

        _lblStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblStatus, 0, 1);

        _btnCapture = new Button { Text = "Capture View", Dock = DockStyle.Fill, Height = 40 };
        _btnCapture.Click += (s, e) => CaptureCurrentView();
        sidePanel.Controls.Add(_btnCapture, 0, 2);

        _btnAutoCapture = new Button { Text = "Auto Capture (grid)", Dock = DockStyle.Fill, Height = 40 };
        _btnAutoCapture.Click += async (s, e) => await AutoCapture();
        sidePanel.Controls.Add(_btnAutoCapture, 0, 3);

        _lblResults = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, AutoSize = false, Height = 80 };
        sidePanel.Controls.Add(_lblResults, 0, 4);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _btnCalibrate = new Button { Text = "Calibrate", Width = 100, Enabled = false };
        _btnCalibrate.Click += (s, e) => RunCalibration();
        _btnSave = new Button { Text = "Save", Width = 80, Enabled = false };
        _btnSave.Click += (s, e) => SaveCalibration();
        btnPanel.Controls.Add(_btnCalibrate);
        btnPanel.Controls.Add(_btnSave);
        sidePanel.Controls.Add(btnPanel, 0, 5);

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
                        _lblStatus.Text = $"Board detected: {detection.CharucoIds?.Size ?? 0} corners";
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
                MessageBox.Show("Please set up ChArUco board first.", "Warning");
                return;
            }

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            using var bmp = new Bitmap(_picPreview.Image);
            using var mat = BitmapToMat(bmp);
            var detection = engine.DetectBoard(mat);

            if (!detection.Detected)
            {
                MessageBox.Show("ChArUco board not detected in this frame.", "Warning");
                return;
            }

            _capturedFrames.Add(mat.Clone());
            _lblCount.Text = $"{_capturedFrames.Count} views captured (need 5+)";
            _btnCalibrate.Enabled = _capturedFrames.Count >= 3;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Capture failed: {ex.Message}", "Error");
        }
    }

    private async System.Threading.Tasks.Task AutoCapture()
    {
        var store = CalibrationStore.Load();
        if (store.BoardConfig == null)
        {
            MessageBox.Show("Please set up ChArUco board first.", "Warning");
            return;
        }

        for (int i = 0; i < 9; i++)
        {
            float dx = (i % 3 - 1) * 10;
            float dy = (i / 3 - 1) * 10;

            if (SerialInterface.Instance.IsConnected)
            {
                var pos = SerialInterface.Instance.MachinePosition;
                string cmd = $"$J=G91 X{dx} Y{dy} F500";
                SerialInterface.Instance.Write(cmd + "\n");
                await System.Threading.Tasks.Task.Delay(750);
            }

            await System.Threading.Tasks.Task.Delay(250);
            CaptureCurrentView();
            await System.Threading.Tasks.Task.Delay(100);
        }

        _lblCount.Text = $"{_capturedFrames.Count} views captured (need 5+)";
        _btnCalibrate.Enabled = _capturedFrames.Count >= 3;
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
                MessageBox.Show("Calibration failed. Need 3+ valid views with detected board.", "Error");
                return;
            }

            _result = result;
            _calibrated = true;
            _btnSave.Enabled = true;

            _lblResults.Text = $"Calibrated!\nRMSE: {result.ReprojectionError:F4} px\n" +
                $"fx={result.CameraMatrix[0]:F1} fy={result.CameraMatrix[4]:F1}\n" +
                $"cx={result.CameraMatrix[2]:F1} cy={result.CameraMatrix[5]:F1}\n" +
                $"k1={result.DistCoeffs[0]:F4} k2={result.DistCoeffs[1]:F4}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Calibration error: {ex.Message}", "Error");
        }
    }

    private void SaveCalibration()
    {
        if (_result == null) return;

        var store = CalibrationStore.Load();
        store.Intrinsics = _result;
        store.Save();
        MessageBox.Show("Lens calibration saved.", "Saved");
        DialogResult = DialogResult.OK;
        Close();
    }
    
    /// <summary>
    /// Expose captured frames for external use
    /// </summary>
    public IReadOnlyList<Mat> CapturedFrames => _capturedFrames;
    public bool IsCalibrated => _calibrated;
}
