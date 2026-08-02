/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using grbl_burn_em.Data;

namespace grbl_burn_em.Forms;

public partial class OffsetCalibrationForm : Form
{
    private PictureBox _picPreview = null!;
    private Label _lblInfo = null!;
    private Label _lblOffset = null!;
    private Label _lblHeight = null!;
    private Button _btnChArUco = null!;
    private Button _btnSave = null!;

    private Button _btnPulse = null!;
    private Button _btnJogXMinus = null!, _btnJogXPlus = null!;
    private Button _btnJogYMinus = null!, _btnJogYPlus = null!;
    private NumericUpDown _nudJogStep = null!;
    private Button _btnLockOffset = null!;

    private NumericUpDown _nudBoardX = null!;
    private NumericUpDown _nudBoardY = null!;
    private NumericUpDown _nudBoardRot = null!;

    private float _offsetX, _offsetY, _offsetZ;
    private PointF _manualStartPos;

    public OffsetCalibrationForm()
    {
        InitializeComponent();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateCalibrationStatus();
    }

    private void UpdateCalibrationStatus()
    {
        var store = CameraManager.Instance.CalibrationStore;
        var parts = new System.Text.StringBuilder();

        if (store.BoardConfig == null)
            parts.Append("Board not configured. ");
        else
            parts.Append($"Board: {store.BoardConfig.DictionaryName} {store.BoardConfig.SquaresX}x{store.BoardConfig.SquaresY}. ");

        if (!store.HasIntrinsics)
            parts.Append("Lens not calibrated. ");
        else
            parts.Append($"Lens calibrated (RMSE={store.Intrinsics!.ReprojectionError:F2}). ");

        if (store.HasOffset)
            parts.Append($"Offset: ({store.Offset!.OffsetX:F0},{store.Offset.OffsetY:F0},{store.Offset.OffsetZ:F0})mm. ");

        _lblInfo.Text = parts.ToString().Trim();
        _btnSave.Enabled = _offsetX != 0 || _offsetY != 0 || _offsetZ != 0;
    }

    private void InitializeComponent()
    {
        Text = "Head-Mounted Camera Offset Calibration";
        Size = new Size(780, 620);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        mainLayout.Controls.Add(_picPreview, 0, 0);

        var sidePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 13, Padding = new Padding(8) };

        _lblInfo = new Label { Text = "Checking calibration status...", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = true };
        sidePanel.Controls.Add(_lblInfo, 0, 0);

        var boardGroup = new GroupBox { Text = "Board position on work area", Dock = DockStyle.Fill };
        var boardLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        boardLayout.Controls.Add(new Label { Text = "Board X (mm):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _nudBoardX = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 2, Width = 85 };
        boardLayout.Controls.Add(_nudBoardX, 1, 0);
        boardLayout.Controls.Add(new Label { Text = "Board Y (mm):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _nudBoardY = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 2, Width = 85 };
        boardLayout.Controls.Add(_nudBoardY, 1, 1);
        boardLayout.Controls.Add(new Label { Text = "Rotation (deg):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        _nudBoardRot = new NumericUpDown { Minimum = -360, Maximum = 360, DecimalPlaces = 1, Width = 85 };
        boardLayout.Controls.Add(_nudBoardRot, 1, 2);
        boardGroup.Controls.Add(boardLayout);
        sidePanel.Controls.Add(boardGroup, 0, 1);

        _btnChArUco = new Button { Text = "Auto (ChArUco Board)", Dock = DockStyle.Fill, Height = 40 };
        _btnChArUco.Click += async (s, e) => await AutoCalibrate();
        sidePanel.Controls.Add(_btnChArUco, 0, 2);

        var sep = new Label { Text = "--- or ---", TextAlign = ContentAlignment.MiddleCenter, Height = 22 };
        sidePanel.Controls.Add(sep, 0, 3);

        // Manual controls
        var manualPanel = new Panel { Dock = DockStyle.Fill, Height = 160 };
        var manualLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(0) };

        _btnPulse = new Button { Text = "Pulse Laser", Dock = DockStyle.Fill, BackColor = Color.OrangeRed, ForeColor = Color.White, Font = new Font("Arial", 9, FontStyle.Bold) };
        _btnPulse.Click += (s, e) => PulseLaser();
        manualLayout.SetColumnSpan(_btnPulse, 3);
        manualLayout.Controls.Add(_btnPulse, 0, 0);

        _btnJogYPlus = new Button { Text = "Y+", Dock = DockStyle.Fill };
        _btnJogYPlus.Click += (s, e) => Jog(0, 1);
        _btnJogYMinus = new Button { Text = "Y-", Dock = DockStyle.Fill };
        _btnJogYMinus.Click += (s, e) => Jog(0, -1);
        _btnJogXMinus = new Button { Text = "X-", Dock = DockStyle.Fill };
        _btnJogXMinus.Click += (s, e) => Jog(-1, 0);
        _btnJogXPlus = new Button { Text = "X+", Dock = DockStyle.Fill };
        _btnJogXPlus.Click += (s, e) => Jog(1, 0);

        manualLayout.Controls.Add(_btnJogYPlus, 1, 1);
        manualLayout.Controls.Add(_btnJogXMinus, 0, 2);
        manualLayout.Controls.Add(_btnJogXPlus, 2, 2);
        manualLayout.Controls.Add(_btnJogYMinus, 1, 2);

        var stepPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        stepPanel.Controls.Add(new Label { Text = "Step:", AutoSize = true });
        _nudJogStep = new NumericUpDown { Minimum = 0.1m, Maximum = 100, Value = 1, DecimalPlaces = 1, Width = 55 };
        stepPanel.Controls.Add(_nudJogStep);
        manualLayout.SetColumnSpan(stepPanel, 3);
        manualLayout.Controls.Add(stepPanel, 0, 3);

        manualPanel.Controls.Add(manualLayout);
        sidePanel.Controls.Add(manualPanel, 0, 4);

        _btnLockOffset = new Button { Text = "Lock Current Offset", Dock = DockStyle.Fill, Height = 35, Enabled = false };
        _btnLockOffset.Click += (s, e) => LockManualOffset();
        sidePanel.Controls.Add(_btnLockOffset, 0, 5);

        _lblOffset = new Label { Text = "Offset: -- mm", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, Height = 25 };
        sidePanel.Controls.Add(_lblOffset, 0, 6);

        _lblHeight = new Label { Text = "Height: -- mm", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, Height = 25 };
        sidePanel.Controls.Add(_lblHeight, 0, 7);

        _btnSave = new Button { Text = "Save Offset", Dock = DockStyle.Fill, Height = 35, Enabled = false };
        _btnSave.Click += (s, e) => SaveOffset();
        sidePanel.Controls.Add(_btnSave, 0, 8);

        var movePanel = new Panel { Dock = DockStyle.Fill };
        var moveLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        moveLayout.Controls.Add(new Label { Text = "Go to:", AutoSize = true });
        var goX = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 1, Width = 55, Value = 0 };
        var goY = new NumericUpDown { Minimum = -5000, Maximum = 5000, DecimalPlaces = 1, Width = 55, Value = 0 };
        var btnGo = new Button { Text = "Go", Width = 40, Height = 23 };
        btnGo.Click += (s, e) =>
        {
            if (!SerialInterface.Instance.IsConnected) return;
            string cmd = string.Create(CultureInfo.InvariantCulture,
                $"$J=G90 X{(float)goX.Value:F1} Y{(float)goY.Value:F1} F2000");
            SerialInterface.Instance.Write(cmd + "\n");
        };
        moveLayout.Controls.Add(goX);
        moveLayout.Controls.Add(goY);
        moveLayout.Controls.Add(btnGo);
        movePanel.Controls.Add(moveLayout);
        sidePanel.Controls.Add(movePanel, 0, 9);

        sidePanel.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 10);
        sidePanel.Controls.Add(new Label { Text = "Manual: place material under laser, pulse to burn mark, jog until camera crosshair aligns with mark, then Lock.", Font = new Font("Arial", 7), ForeColor = Color.Gray, AutoSize = true }, 0, 11);

        var btnRefresh = new Button { Text = "Refresh Status", Dock = DockStyle.Fill, Height = 25 };
        btnRefresh.Click += (s, e) => UpdateCalibrationStatus();
        sidePanel.Controls.Add(btnRefresh, 0, 12);

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

    private async System.Threading.Tasks.Task AutoCalibrate()
    {
        if (_picPreview.Image == null)
        {
            MessageBox.Show(this, "No camera frame available yet. Wait for the camera preview to appear.", "Error");
            return;
        }

        var store = CameraManager.Instance.CalibrationStore;

        var missing = new System.Text.StringBuilder();
        if (store.BoardConfig == null) missing.Append("ChArUco board not configured. ");
        if (!store.HasIntrinsics) missing.Append("Lens not calibrated. ");

        if (missing.Length > 0)
        {
            MessageBox.Show(this,
                $"Cannot auto-calibrate:\n{missing}\n\nSet up the ChArUco board and calibrate the lens first.",
                "Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnChArUco.Enabled = false;
        _lblInfo.Text = "Computing...";

        try
        {
            using var bmp = new Bitmap(_picPreview.Image);
            var boardConfig = store.BoardConfig!;
            var intrinsics = store.Intrinsics!;

            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                using var mat = CameraCalibrationEngine.BitmapToMat(bmp);
                var engine = new CameraCalibrationEngine(boardConfig);
                var pose = engine.SolveCameraPose(mat, intrinsics);
                if (pose == null)
                {
                    var detection = engine.DetectBoard(mat);
                    return (null, detection);
                }
                return (pose, (CameraCalibrationEngine.DetectionResult?)null);
            });

            if (result.Item1 == null)
            {
                var detection = result.Item2!;
                if (detection.MarkerIds == null || detection.MarkerIds.Size < 6)
                    MessageBox.Show(this,
                        $"Not enough ArUco markers found ({detection.MarkerIds?.Size ?? 0}/6). Move the camera so more of the board is visible.",
                        "Detection Failed");
                else
                    MessageBox.Show(this,
                        $"Board partially detected ({detection.MarkerIds.Size} markers, {detection.CharucoIds?.Size ?? 0} corners) but pose estimation failed.\nTry repositioning the board or camera.",
                        "Pose Failed");
                return;
            }

            var (rvec, tvec, reproj) = result.Item1.Value;

            float boardWx = (float)_nudBoardX.Value;
            float boardWy = (float)_nudBoardY.Value;
            float boardRotDeg = (float)_nudBoardRot.Value;
            float machineX = 0f, machineY = 0f;
            if (SerialInterface.Instance.IsConnected)
            {
                var pos = SerialInterface.Instance.MachinePosition;
                machineX = pos.X; machineY = pos.Y;
            }

            // Camera center in the board coordinate frame: C_b = -R^T * tvec,
            // where R/tvec map board coordinates to camera coordinates.
            using (var rvecMat = new Mat(3, 1, DepthType.Cv64F, 1))
            {
                Marshal.Copy(rvec, 0, rvecMat.DataPointer, 3);
                using var rotMat = new Mat();
                CvInvoke.Rodrigues(rvecMat, rotMat);
                double[] R = new double[9];
                Marshal.Copy(rotMat.DataPointer, R, 0, 9);

                double t0 = tvec[0], t1 = tvec[1], t2 = tvec[2];
                double cbx = -(R[0] * t0 + R[3] * t1 + R[6] * t2);
                double cby = -(R[1] * t0 + R[4] * t1 + R[7] * t2);
                double cbz = -(R[2] * t0 + R[5] * t1 + R[8] * t2);

                // Board -> world: rotate by the entered angle, translate to the board origin.
                double rad = boardRotDeg * Math.PI / 180.0;
                double cosR = Math.Cos(rad), sinR = Math.Sin(rad);
                double camWorldX = cosR * cbx - sinR * cby + boardWx;
                double camWorldY = sinR * cbx + cosR * cby + boardWy;
                double camWorldZ = cbz;

                _offsetX = (float)(camWorldX - machineX);
                _offsetY = (float)(camWorldY - machineY);
                _offsetZ = (float)camWorldZ;
            }

            _lblOffset.Text = $"Offset: ({_offsetX:F1}, {_offsetY:F1}) mm";
            _lblHeight.Text = $"Height: {_offsetZ:F1} mm";
            _lblInfo.Text = $"Detected! RMSE: {reproj:F3} px  Board at ({boardWx:F0},{boardWy:F0})";
            _btnSave.Enabled = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                MessageBox.Show(this, $"Auto calibration error: {ex.Message}", "Error");
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnChArUco.Enabled = true;
                if (_lblInfo.Text == "Computing...") _lblInfo.Text = "Ready";
            }
        }
    }

    private void PulseLaser()
    {
        if (!SerialInterface.Instance.IsConnected)
        {
            MessageBox.Show(this, "Machine not connected.", "Error");
            return;
        }

        _manualStartPos = SerialInterface.Instance.MachinePosition;
        _btnLockOffset.Enabled = true;
        UpdateManualOffset();

        var pos = _manualStartPos;
        string cmd = string.Create(CultureInfo.InvariantCulture,
            $"G0 X{pos.X:F2} Y{pos.Y:F2}\nM3 S500\nG4 P0.3\nM5");
        SerialInterface.Instance.Write(cmd + "\n");

        _lblInfo.Text = $"Pulsed at ({pos.X:F1},{pos.Y:F1}). Jog until camera crosshair aligns with burn mark.";
    }

    private void Jog(float dx, float dy)
    {
        if (!SerialInterface.Instance.IsConnected) return;
        float step = (float)_nudJogStep.Value;
        string cmd = string.Create(CultureInfo.InvariantCulture,
            $"$J=G91 X{dx * step:F1} Y{dy * step:F1} F1000");
        SerialInterface.Instance.Write(cmd + "\n");
        Task.Run(async () =>
        {
            await Task.Delay(500);
            this.BeginInvoke(new Action(UpdateManualOffset));
        });
    }

    private void UpdateManualOffset()
    {
        if (!SerialInterface.Instance.IsConnected) return;
        var currentPos = SerialInterface.Instance.MachinePosition;
        _offsetX = _manualStartPos.X - currentPos.X;
        _offsetY = _manualStartPos.Y - currentPos.Y;
        _lblOffset.Text = $"Offset: ({_offsetX:F1}, {_offsetY:F1}) mm";
    }

    private void LockManualOffset()
    {
        UpdateManualOffset();
        _btnSave.Enabled = true;
        _btnLockOffset.Enabled = false;
        _lblInfo.Text = $"Offset locked: ({_offsetX:F1}, {_offsetY:F1}) mm. Enter height manually if needed, then Save.";
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
        MessageBox.Show(this, "Offset saved.", "Saved");
        DialogResult = DialogResult.OK;
        Close();
    }
}
