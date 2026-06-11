/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Emgu.CV;
using grbl_burn_em.Data;
using static grbl_burn_em.Data.CameraCalibrationEngine;

namespace grbl_burn_em.Forms;

public class StandaloneLensCalibratorForm : Form
{
    private ComboBox _cmbDevices = null!;
    private Button _btnStartStop = null!;
    private PictureBox _picPreview = null!;
    private Label _lblStatus = null!;
    private Label _lblCount = null!;
    private Label _lblResults = null!;
    private Button _btnCapture = null!;
    private Button _btnCalibrate = null!;
    private Button _btnSave = null!;

    private readonly List<Mat> _capturedFrames = new();
    private Mat? _rawFrame;
    private readonly object _rawFrameLock = new();
    private bool _calibrated;
    private CameraIntrinsics? _result;

    public StandaloneLensCalibratorForm()
    {
        InitializeComponent();
        RefreshDevices();
        
        if (!string.IsNullOrEmpty(AppConfiguration.Instance.LastCameraDevice))
        {
            if (_cmbDevices.Items.Contains(AppConfiguration.Instance.LastCameraDevice))
            {
                _cmbDevices.SelectedItem = AppConfiguration.Instance.LastCameraDevice;
            }
        }
    }

    private void InitializeComponent()
    {
        Text = "Standalone Lens Calibrator";
        Size = new Size(1000, 780);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        mainLayout.Controls.Add(_picPreview, 0, 0);

        var sidePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(10), AutoScroll = true };
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // Camera select
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Start/Stop
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Board Setup
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Status
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Count
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Capture
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); // Calibrate buttons
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Results
        sidePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // Info

        // 1. Camera Selection
        var pnlDevice = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        pnlDevice.Controls.Add(new Label { Text = "Camera Device:", AutoSize = true });
        _cmbDevices = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
        pnlDevice.Controls.Add(_cmbDevices);
        sidePanel.Controls.Add(pnlDevice, 0, 0);

        // 2. Start/Stop
        _btnStartStop = new Button { Text = "Start Camera", Dock = DockStyle.Fill, Height = 40, BackColor = Color.LightGreen };
        _btnStartStop.Click += OnStartStopClick;
        sidePanel.Controls.Add(_btnStartStop, 0, 1);

        // 3. Board Setup
        var btnBoardSetup = new Button { Text = "ChArUco Board Setup...", Dock = DockStyle.Fill, Height = 40 };
        btnBoardSetup.Click += (s, e) =>
        {
            using var form = new CharucoBoardSetupForm();
            form.ShowDialog(this);
        };
        sidePanel.Controls.Add(btnBoardSetup, 0, 2);

        // 4. Status
        _lblStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblStatus, 0, 3);

        // 5. Count
        _lblCount = new Label { Text = "0 views captured (need 6+)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblCount, 0, 4);

        // 6. Capture
        _btnCapture = new Button { Text = "Capture Current View", Dock = DockStyle.Fill, Height = 40 };
        _btnCapture.Click += (s, e) => CaptureCurrentView();
        sidePanel.Controls.Add(_btnCapture, 0, 5);

        // 7. Calibrate & Save
        var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        _btnCalibrate = new Button { Text = "Calibrate", Width = 80, Height = 30, Enabled = false };
        _btnCalibrate.Click += (s, e) => RunCalibration();
        var btnSingleCal = new Button { Text = "Quick Calib", Width = 85, Height = 30, BackColor = Color.LightBlue };
        btnSingleCal.Click += (s, e) => RunSingleViewCalibration();
        _btnSave = new Button { Text = "Save", Width = 60, Height = 30, Enabled = false };
        _btnSave.Click += (s, e) => SaveCalibration();
        
        var btnClear = new Button { Text = "Clear", Width = 60, Height = 30 };
        btnClear.Click += (s, e) =>
        {
            foreach (var frame in _capturedFrames) frame.Dispose();
            _capturedFrames.Clear();
            _lblCount.Text = "0 views captured (need 6+)";
            _btnCalibrate.Enabled = false;
        };

        pnlBtns.Controls.Add(_btnCalibrate);
        pnlBtns.Controls.Add(btnSingleCal);
        pnlBtns.Controls.Add(_btnSave);
        pnlBtns.Controls.Add(btnClear);
        sidePanel.Controls.Add(pnlBtns, 0, 6);

        // 8. Results
        _lblResults = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter };
        sidePanel.Controls.Add(_lblResults, 0, 7);

        // 9. Info
        sidePanel.Controls.Add(new Label { Text = "Move the ChArUco board around to capture at least 6 different views.", Font = new Font("Arial", 8), ForeColor = Color.Gray, AutoSize = true, TextAlign = ContentAlignment.BottomCenter }, 0, 8);

        mainLayout.Controls.Add(sidePanel, 1, 0);
        Controls.Add(mainLayout);
    }

    private void RefreshDevices()
    {
        _cmbDevices.Items.Clear();
        var devices = CameraManager.Instance.GetAvailableDevices();
        if (devices.Count > 0)
        {
            _cmbDevices.Items.AddRange(devices.ToArray());
            _cmbDevices.SelectedIndex = 0;
        }
    }

    private async void OnStartStopClick(object? sender, EventArgs e)
    {
        if (CameraManager.Instance.IsRunning)
        {
            CameraManager.Instance.FrameReceived -= OnFrameReceived;
            await CameraManager.Instance.StopCameraAsync();
            _btnStartStop.Text = "Start Camera";
            _btnStartStop.BackColor = Color.LightGreen;
            
            var old = _picPreview.Image;
            _picPreview.Image = null;
            old?.Dispose();
        }
        else
        {
            int index = _cmbDevices.SelectedIndex;
            if (index >= 0 && _cmbDevices.SelectedItem != null)
            {
                var deviceName = _cmbDevices.SelectedItem.ToString() ?? "";
                AppConfiguration.Instance.LastCameraDevice = deviceName;
                AppConfiguration.Instance.Save();
                
                CameraManager.Instance.FrameReceived -= OnFrameReceived;
                await CameraManager.Instance.StartCameraAsync(index);
                CameraManager.Instance.FrameReceived += OnFrameReceived;
                
                _btnStartStop.Text = "Stop Camera";
                _btnStartStop.BackColor = Color.Salmon;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (CameraManager.Instance.IsRunning)
            {
                CameraManager.Instance.FrameReceived -= OnFrameReceived;
                _ = CameraManager.Instance.StopCameraAsync();
            }
            foreach (var frame in _capturedFrames) frame.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnFrameReceived(Bitmap frame)
    {
        if (!IsHandleCreated || IsDisposed) { frame.Dispose(); return; }
        try
        {
            using var mat = BitmapToMat(frame);
            frame.Dispose();

            lock (_rawFrameLock)
            {
                var old = _rawFrame;
                _rawFrame = mat.Clone();
                old?.Dispose();
            }

            var store = CameraManager.Instance.CalibrationStore;
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
                        _lblStatus.Text = $"Board DETECTED: {detection.CharucoIds?.Size ?? 0} corners, {detection.MarkerIds?.Size ?? 0} markers (need 6+)";
                    else if (detection.MarkerIds?.Size >= 6)
                        _lblStatus.Text = $"{detection.MarkerIds.Size} markers found but only {detection.CharucoIds?.Size ?? 0} ChArUco corners (need 6+). Check board config.";
                    else if (detection.MarkerIds?.Size > 0)
                        _lblStatus.Text = $"{detection.MarkerIds.Size} markers found (need 6+) - move board into view";
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
        Mat? rawCopy = null;
        lock (_rawFrameLock)
        {
            if (_rawFrame != null) rawCopy = _rawFrame.Clone();
        }
        if (rawCopy == null) return;

        try
        {
            var store = CameraManager.Instance.CalibrationStore;
            if (store.BoardConfig == null) { rawCopy.Dispose(); return; }

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            var detection = engine.DetectBoard(rawCopy);

            if (!detection.Detected)
            {
                rawCopy.Dispose();
                MessageBox.Show(this, "ChArUco board not detected.", "Warning");
                return;
            }

            _capturedFrames.Add(rawCopy);
            _lblCount.Text = $"{_capturedFrames.Count} views (need 6+)";
            _btnCalibrate.Enabled = _capturedFrames.Count >= 6;
        }
        catch (Exception ex)
        {
            rawCopy?.Dispose();
            MessageBox.Show(this, $"Capture failed: {ex.Message}", "Error");
        }
    }

    private void RunSingleViewCalibration()
    {
        if (_picPreview.Image == null) return;

        try
        {
            var store = CameraManager.Instance.CalibrationStore;
            if (store.BoardConfig == null)
            {
                MessageBox.Show(this, "Please set up ChArUco board first.", "Warning");
                return;
            }

            var engine = new CameraCalibrationEngine(store.BoardConfig);

            Mat? rawCopy = null;
            lock (_rawFrameLock)
            {
                if (_rawFrame != null) rawCopy = _rawFrame.Clone();
            }
            if (rawCopy == null) return;

            using var mat = rawCopy;
            var result = engine.CalibrateSingleView(mat);

            if (result == null)
            {
                var detection = engine.DetectBoard(mat);
                string detail = detection.Detected
                    ? $"Board detected ({detection.CharucoIds!.Size} corners, {detection.MarkerIds!.Size} markers) but calibration optimization diverged.\n\nSingle-view calibration is unstable with strong lens distortion.\nUse multiple views instead."
                    : detection.MarkerIds is { Size: > 0 }
                        ? $"Only {detection.CharucoIds?.Size ?? 0} ChArUco corners from {detection.MarkerIds.Size} markers (need 6+).\nCheck board config (dictionary, square size, marker size) matches the physical board."
                        : "Board not detected. Position the ChArUco board in the camera view.";
                MessageBox.Show(this, detail, "Quick Calib Failed");
                return;
            }

            _result = result;
            _calibrated = true;
            _btnSave.Enabled = true;

            _lblResults.Text = $"Single-view calibrated!\nRMSE: {result.ReprojectionError:F4} px\n" +
                $"fx=fy={result.CameraMatrix[0]:F1}\n" +
                $"cx={result.CameraMatrix[2]:F1} cy={result.CameraMatrix[5]:F1}\n" +
                $"k1={result.DistCoeffs[0]:F4} k2={result.DistCoeffs[1]:F4}";
            _lblCount.Text = "1 view (single-image mode)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Single-view error: {ex.Message}", "Error");
        }
    }

    private void RunCalibration()
    {
        try
        {
            var store = CameraManager.Instance.CalibrationStore;
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

        var store = CameraManager.Instance.CalibrationStore;
        store.Intrinsics = _result;
        store.Save();
        MessageBox.Show(this, "Lens calibration saved.", "Saved");
    }
}
