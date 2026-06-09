/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using System.Windows.Forms;
using Emgu.CV;
using grbl_burn_em.Data;

using static grbl_burn_em.Data.CameraCalibrationEngine;

namespace grbl_burn_em.Forms;

public partial class CameraRegistrationForm : Form
{
    private PictureBox _picPreview = null!;
    private Label _lblStatus = null!;
    private Label _lblResults = null!;
    private NumericUpDown _nudBoardX = null!;
    private NumericUpDown _nudBoardY = null!;
    private NumericUpDown _nudBoardRot = null!;
    private Button _btnCompute = null!;
    private Button _btnSave = null!;

    private StationaryRegistration? _registration;

    public CameraRegistrationForm()
    {
        InitializeComponent();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!CameraManager.Instance.IsRunning)
            _lblStatus.Text = "Camera not running.";
    }

    private void InitializeComponent()
    {
        Text = "Camera Registration (Stationary)";
        Size = new Size(750, 600);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        mainLayout.Controls.Add(_picPreview, 0, 0);

        var sidePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(10) };
        sidePanel.Controls.Add(new Label { Text = "Board World Position:", Font = new Font("Arial", 10, FontStyle.Bold), AutoSize = true }, 0, 0);
        sidePanel.Controls.Add(new Label { Text = "X (mm):", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _nudBoardX = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 2, Dock = DockStyle.Fill };
        sidePanel.Controls.Add(_nudBoardX, 0, 1);

        sidePanel.Controls.Add(new Label { Text = "Y (mm):", TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        _nudBoardY = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 2, Dock = DockStyle.Fill };
        sidePanel.Controls.Add(_nudBoardY, 0, 2);

        sidePanel.Controls.Add(new Label { Text = "Rotation (deg):", TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        _nudBoardRot = new NumericUpDown { Minimum = -360, Maximum = 360, DecimalPlaces = 1, Dock = DockStyle.Fill };
        sidePanel.Controls.Add(_nudBoardRot, 0, 3);

        _lblStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, Height = 40 };
        sidePanel.Controls.Add(_lblStatus, 0, 4);

        _btnCompute = new Button { Text = "Compute Registration", Dock = DockStyle.Fill, Height = 40 };
        _btnCompute.Click += (s, e) => ComputeRegistration();
        sidePanel.Controls.Add(_btnCompute, 0, 5);

        _lblResults = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, AutoSize = false, Height = 100 };
        sidePanel.Controls.Add(_lblResults, 0, 6);

        _btnSave = new Button { Text = "Save", Dock = DockStyle.Fill, Enabled = false };
        _btnSave.Click += (s, e) => SaveRegistration();
        sidePanel.Controls.Add(_btnSave, 0, 7);

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
            using var mat = BitmapToMat(frame);
            frame.Dispose();

            var store = CalibrationStore.Load();
            if (store.BoardConfig == null || !store.HasIntrinsics)
            {
                this.BeginInvoke(() => _lblStatus.Text = "Need board config + lens calibration first");
                return;
            }

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            var detection = engine.DetectBoard(mat);

            using var display = mat.Clone();
            engine.DrawDetectedBoard(display, detection);
            var displayBmp = MatToBitmap(display);

            this.BeginInvoke(new Action(() =>
            {
                if (_picPreview.IsDisposed) { displayBmp.Dispose(); return; }
                var old = _picPreview.Image;
                _picPreview.Image = displayBmp;
                old?.Dispose();

                _lblStatus.Text = detection.Detected
                    ? $"Board detected: {detection.CharucoIds?.Size ?? 0} corners"
                    : "Board not detected";
            }));
        }
        catch
        {
            frame.Dispose();
        }
    }

    private void ComputeRegistration()
    {
        try
        {
            var store = CalibrationStore.Load();
            if (store.BoardConfig == null || !store.HasIntrinsics)
            {
                MessageBox.Show(this, "Need ChArUco board config and lens calibration first.", "Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_picPreview.Image == null)
            {
                MessageBox.Show(this, "No camera frame available.", "Error");
                return;
            }

            using var bmp = new Bitmap(_picPreview.Image);
            using var mat = BitmapToMat(bmp);

            var engine = new CameraCalibrationEngine(store.BoardConfig);
            var homography = engine.ComputeWorkAreaHomography(
                mat, store.Intrinsics!,
                (float)_nudBoardX.Value, (float)_nudBoardY.Value, (float)_nudBoardRot.Value);

            if (homography == null)
            {
                MessageBox.Show(this, "Registration failed. Ensure board is detected and lens is calibrated.", "Error");
                return;
            }

            var pose = engine.SolveCameraPose(mat, store.Intrinsics!);
            if (pose == null) return;

            _registration = new StationaryRegistration
            {
                Homography = homography,
                Rvec = pose.Value.rvec,
                Tvec = pose.Value.tvec,
                ReprojectionError = pose.Value.reprojError
            };

            _btnSave.Enabled = true;
            _lblResults.Text = $"Registration OK\nRMSE: {pose.Value.reprojError:F4} px\n" +
                $"tvec: ({pose.Value.tvec[0]:F1}, {pose.Value.tvec[1]:F1}, {pose.Value.tvec[2]:F1}) mm";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Registration error: {ex.Message}", "Error");
        }
    }

    private void SaveRegistration()
    {
        if (_registration == null) return;
        var store = CalibrationStore.Load();
        store.Registration = _registration;
        store.Save();
        MessageBox.Show(this, "Registration saved.", "Saved");
        DialogResult = DialogResult.OK;
        Close();
    }
}
