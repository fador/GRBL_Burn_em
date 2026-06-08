/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using System.Windows.Forms;
using grbl_burn_em.Data;
using grbl_burn_em.Tools;

namespace grbl_burn_em.Forms;

public partial class OffsetCalibrationForm : Form
{
    private PictureBox _picPreview = null!;
    private Label _lblInfo = null!;
    private Label _lblOffset = null!;
    private Label _lblHeight = null!;
    private Button _btnChArUco = null!;
    private Button _btnManual = null!;
    private Button _btnSave = null!;

    private float _offsetX, _offsetY, _offsetZ;

    public OffsetCalibrationForm()
    {
        InitializeComponent();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!CameraManager.Instance.IsRunning)
            _lblInfo.Text = "Camera not running. Start camera first.";
    }

    private void InitializeComponent()
    {
        Text = "Head-Mounted Camera Offset Calibration";
        Size = new Size(700, 550);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        mainLayout.Controls.Add(_picPreview, 0, 0);

        var sidePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(10) };

        _lblInfo = new Label { Text = "Select calibration method:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        sidePanel.Controls.Add(_lblInfo, 0, 0);

        _btnChArUco = new Button { Text = "Auto (ChArUco Board)", Dock = DockStyle.Fill, Height = 45 };
        _btnChArUco.Click += (s, e) => AutoCalibrate();
        sidePanel.Controls.Add(_btnChArUco, 0, 1);

        var sep = new Label { Text = "--- or ---", TextAlign = ContentAlignment.MiddleCenter, Height = 30 };
        sidePanel.Controls.Add(sep, 0, 2);

        _btnManual = new Button { Text = "Manual (Burn Mark)", Dock = DockStyle.Fill, Height = 45 };
        _btnManual.Click += (s, e) => ManualCalibrate();
        sidePanel.Controls.Add(_btnManual, 0, 3);

        _lblOffset = new Label { Text = "Offset: -- mm", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, Height = 40 };
        sidePanel.Controls.Add(_lblOffset, 0, 4);

        _lblHeight = new Label { Text = "Height: -- mm", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, Height = 40 };
        sidePanel.Controls.Add(_lblHeight, 0, 5);

        _btnSave = new Button { Text = "Save Offset", Dock = DockStyle.Fill, Height = 40, Enabled = false };
        _btnSave.Click += (s, e) => SaveOffset();
        sidePanel.Controls.Add(_btnSave, 0, 6);

        mainLayout.Controls.Add(sidePanel, 1, 0);
        Controls.Add(mainLayout);

        CameraManager.Instance.FrameReceived += OnFrameReceived;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CameraManager.Instance.FrameReceived -= OnFrameReceived;
        base.Dispose(disposing);
    }

    private void OnFrameReceived(Bitmap frame)
    {
        if (!IsHandleCreated) { frame.Dispose(); return; }
        try
        {
            this.BeginInvoke(new Action(() =>
            {
                if (_picPreview.IsDisposed) { frame.Dispose(); return; }
                var old = _picPreview.Image;
                _picPreview.Image = new Bitmap(frame);
                old?.Dispose();
                frame.Dispose();
            }));
        }
        catch
        {
            frame.Dispose();
        }
    }

    private void AutoCalibrate()
    {
        var store = CalibrationStore.Load();
        if (store.BoardConfig == null || !store.HasIntrinsics)
        {
            MessageBox.Show("Need ChArUco board config and lens calibration first.", "Missing",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_picPreview.Image == null)
        {
            MessageBox.Show("No camera frame available.", "Error");
            return;
        }

        try
        {
            using var bmp = new Bitmap(_picPreview.Image);
            using var mat = CameraCalibrationEngine.BitmapToMat(bmp);
            var engine = new CameraCalibrationEngine(store.BoardConfig);
            var pose = engine.SolveCameraPose(mat, store.Intrinsics!);

            if (pose == null)
            {
                MessageBox.Show("ChArUco board not detected in current frame.", "Error");
                return;
            }

            var (rvec, tvec, reproj) = pose.Value;
            _offsetX = (float)tvec[0];
            _offsetY = (float)tvec[1];
            _offsetZ = (float)tvec[2];

            _lblOffset.Text = $"Offset: ({_offsetX:F1}, {_offsetY:F1}) mm";
            _lblHeight.Text = $"Height: {_offsetZ:F1} mm";
            _lblInfo.Text = $"Detected! RMSE: {reproj:F3} px";
            _btnSave.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Auto calibration error: {ex.Message}", "Error");
        }
    }

    private void ManualCalibrate()
    {
        if (!SerialInterface.Instance.IsConnected)
        {
            MessageBox.Show("Machine not connected.", "Error");
            return;
        }

        var startPos = SerialInterface.Instance.MachinePosition;

        var result = MessageBox.Show(
            "Manual Offset Calibration:\n\n" +
            "1. Place material on the work area\n" +
            "2. Click 'Pulse Laser' to create a burn mark at current position\n" +
            "3. Use jog controls to align camera crosshair with the burn mark\n" +
            "4. Click OK when aligned",
            "Manual Offset", MessageBoxButtons.OKCancel);

        if (result != DialogResult.OK) return;

        var currentPos = SerialInterface.Instance.MachinePosition;
        _offsetX = startPos.X - currentPos.X;
        _offsetY = startPos.Y - currentPos.Y;

        _lblOffset.Text = $"Offset: ({_offsetX:F1}, {_offsetY:F1}) mm";
        _lblHeight.Text = "Height: manually entered";
        _btnSave.Enabled = true;
    }

    private void SaveOffset()
    {
        var store = CalibrationStore.Load();
        store.Offset = new HeadMountedOffset
        {
            OffsetX = _offsetX,
            OffsetY = _offsetY,
            OffsetZ = _offsetZ
        };
        store.Save();
        MessageBox.Show("Offset saved.", "Saved");
        DialogResult = DialogResult.OK;
        Close();
    }
}
