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
using grbl_burn_em.Data;

namespace grbl_burn_em.Forms;

public partial class WorkspaceScanForm : Form
{
    private Label _lblStatus = null!;
    private ProgressBar _progressBar = null!;
    private NumericUpDown _nudOverlap = null!;
    private Button _btnStart = null!;
    private Button _btnCancel = null!;
    private Button _btnClear = null!;

    private bool _cancelRequested;
    private CancellationTokenSource? _scanCts;

    public WorkspaceScanForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Workspace Scan";
        Size = new Size(400, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(15) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Grid Scan: Moves machine, captures frames at each grid point,\nreconstructs full work area image.", AutoSize = true }, 0, 0);

        var pnlOverlap = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        pnlOverlap.Controls.Add(new Label { Text = "Overlap (%):", AutoSize = true });
        _nudOverlap = new NumericUpDown { Minimum = 0, Maximum = 90, Value = 20, Width = 60 };
        pnlOverlap.Controls.Add(_nudOverlap);
        layout.Controls.Add(pnlOverlap, 0, 1);

        _progressBar = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
        _progressBar.Visible = false;
        layout.Controls.Add(_progressBar, 0, 2);

        var pnlBtns = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        _btnStart = new Button { Text = "Start Scan", Width = 100, Height = 35 };
        _btnStart.Click += async (s, e) => await StartScanAsync();
        _btnCancel = new Button { Text = "Cancel", Width = 80, Enabled = false };
        _btnCancel.Click += (s, e) => { _cancelRequested = true; _scanCts?.Cancel(); };
        _btnClear = new Button { Text = "Clear Scan", Width = 100 };
        _btnClear.Click += (s, e) => { CameraManager.Instance.CapturedFrames.Clear(); };

        pnlBtns.Controls.Add(_btnStart);
        pnlBtns.Controls.Add(_btnCancel);
        pnlBtns.Controls.Add(_btnClear);
        layout.Controls.Add(pnlBtns, 0, 3);

        _lblStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter };
        layout.Controls.Add(_lblStatus, 0, 4);

        Controls.Add(layout);
    }

    private async Task StartScanAsync()
    {
        if (!SerialInterface.Instance.IsConnected)
        {
            MessageBox.Show(this, "Machine not connected. Click 'Connect Emulator' in Camera Settings.", "Error");
            return;
        }

        _cancelRequested = false;
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        _btnStart.Enabled = false;
        _btnCancel.Enabled = true;
        _progressBar.Visible = true;
        _lblStatus.Text = "Starting scan...";

        try
        {
            var store = CalibrationStore.Load();
            var config = AppConfiguration.Instance;
            float workW = config.WorkAreaWidth;
            float workH = config.WorkAreaHeight;

            float fovW = config.CameraOverlayWidth;
            float fovH = config.CameraOverlayHeight;
            float shiftX = 0f;
            float shiftY = 0f;

            var memStore = CameraManager.Instance.CalibrationStore;
            if (memStore.HasIntrinsics && memStore.HasOffset && memStore.Offset!.OffsetZ > 0)
            {
                var intrinsics = memStore.Intrinsics!;
                if (intrinsics.CameraMatrix != null && intrinsics.CameraMatrix.Length >= 9 && intrinsics.CameraMatrix[0] > 0 && intrinsics.CameraMatrix[4] > 0)
                {
                    float fx = (float)intrinsics.CameraMatrix[0];
                    float fy = (float)intrinsics.CameraMatrix[4];
                    float cx = (float)intrinsics.CameraMatrix[2];
                    float cy = (float)intrinsics.CameraMatrix[5];
                    float w = intrinsics.CalibratedImageWidth;
                    float h = intrinsics.CalibratedImageHeight;
                    float z = memStore.Offset.OffsetZ;

                    fovW = w * z / fx;
                    fovH = h * z / fy;

                    shiftX = (w / 2f - cx) * z / fx;
                    shiftY = (cy - h / 2f) * z / fy;
                }
            }

            if (fovW <= 10) fovW = config.CameraOverlayWidth;
            if (fovH <= 10) fovH = config.CameraOverlayHeight;
            if (fovW <= 10 || fovH <= 10)
            {
                MessageBox.Show(this, "Camera FOV not defined. Configure overlay size or calibrate camera first.", "Error");
                return;
            }

            float overlap = (float)_nudOverlap.Value / 100f;
            float stepX = fovW * (1f - overlap);
            float stepY = fovH * (1f - overlap);

            float offX = store.Offset?.OffsetX ?? config.CameraOverlayX;
            float offY = store.Offset?.OffsetY ?? config.CameraOverlayY;

            var points = new List<PointF>();
            for (float y = -offY; y <= workH - offY; y += stepY)
            {
                for (float x = -offX; x <= workW - offX; x += stepX)
                {
                    float cx = Math.Clamp(x, 0, workW);
                    float cy = Math.Clamp(y, 0, workH);

                    bool isDuplicate = false;
                    foreach (var p in points)
                    {
                        if (Math.Abs(p.X - cx) < 0.1f && Math.Abs(p.Y - cy) < 0.1f)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                    if (!isDuplicate)
                        points.Add(new PointF(cx, cy));
                }
            }

            CameraManager.Instance.CapturedFrames.Clear();
            int total = points.Count;
            _lblStatus.Text = $"Scanning 0/{total}...";

            for (int i = 0; i < total; i++)
            {
                if (_cancelRequested || token.IsCancellationRequested) break;

                var pt = points[i];
                string cmd = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"$J=G90 X{pt.X:F2} Y{pt.Y:F2} F{config.FramingSpeed}");
                bool sent = SerialInterface.Instance.Write(cmd + "\n");

                if (!sent)
                {
                    if (!IsDisposed)
                        MessageBox.Show(this, "Connection lost during scan.", "Error");
                    break;
                }

                try { await WaitForPosition(pt.X, pt.Y, token); }
                catch (OperationCanceledException) { break; }

                if (token.IsCancellationRequested) break;
                await Task.Delay(500, token);

                CameraManager.Instance.CaptureCurrentFrame(pt.X + offX + shiftX, pt.Y + offY + shiftY, fovW, fovH);

                this.BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed)
                        _lblStatus.Text = $"Scanning... {i + 1}/{total}";
                }));
            }

            if (!IsDisposed)
            {
                string msg = _cancelRequested
                    ? $"Cancelled. {CameraManager.Instance.CapturedFrames.Count} frames captured."
                    : $"Done. {CameraManager.Instance.CapturedFrames.Count} frames captured.";
                _lblStatus.Text = msg;
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) _lblStatus.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, $"Scan error: {ex.Message}", "Error");
        }
        finally
        {
            _scanCts?.Dispose();
            _scanCts = null;
            if (!IsDisposed)
            {
                _btnStart.Enabled = true;
                _btnCancel.Enabled = false;
                _progressBar.Visible = false;
            }
        }
    }

    private static async Task WaitForPosition(float targetX, float targetY, CancellationToken token)
    {
        int timeout = 10000;
        while (timeout > 0)
        {
            token.ThrowIfCancellationRequested();
            if (!SerialInterface.Instance.IsConnected) return;
            var pos = SerialInterface.Instance.MachinePosition;
            float dist = (float)Math.Sqrt(
                Math.Pow(pos.X - targetX, 2) + Math.Pow(pos.Y - targetY, 2));
            if (dist < 1.0f) return;
            await Task.Delay(100, token);
            timeout -= 100;
        }
    }
}
