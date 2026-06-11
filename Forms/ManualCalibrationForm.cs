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

namespace grbl_burn_em.Forms;

public partial class ManualCalibrationForm : Form
{
    private NumericUpDown _nudFx = null!, _nudFy = null!, _nudCx = null!, _nudCy = null!;
    private NumericUpDown _nudK1 = null!, _nudK2 = null!, _nudP1 = null!, _nudP2 = null!, _nudK3 = null!;
    private NumericUpDown _nudImgW = null!, _nudImgH = null!;

    public ManualCalibrationForm()
    {
        InitializeComponent();
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        var store = CameraManager.Instance.CalibrationStore;
        if (store.HasIntrinsics)
        {
            var ci = store.Intrinsics!;
            _nudFx.Value = (decimal)ci.CameraMatrix[0];
            _nudFy.Value = (decimal)ci.CameraMatrix[4];
            _nudCx.Value = (decimal)ci.CameraMatrix[2];
            _nudCy.Value = (decimal)ci.CameraMatrix[5];
            _nudK1.Value = (decimal)ci.DistCoeffs[0];
            _nudK2.Value = (decimal)ci.DistCoeffs[1];
            _nudP1.Value = (decimal)ci.DistCoeffs[2];
            _nudP2.Value = (decimal)ci.DistCoeffs[3];
            _nudK3.Value = (decimal)ci.DistCoeffs[4];
            _nudImgW.Value = ci.CalibratedImageWidth > 0 ? ci.CalibratedImageWidth : 1280;
            _nudImgH.Value = ci.CalibratedImageHeight > 0 ? ci.CalibratedImageHeight : 960;
        }
    }

    private void InitializeComponent()
    {
        Text = "Manual Calibration Entry";
        Size = new Size(380, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 13, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        layout.Controls.Add(new Label { Text = "Camera Matrix", Font = new Font("Arial", 10, FontStyle.Bold), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.SetColumnSpan(layout.Controls[0], 2);

        (_nudFx, layout) = AddRow(layout, 1, "fx:", 800, 1, 1000000);
        (_nudFy, layout) = AddRow(layout, 2, "fy:", 800, 1, 1000000);
        (_nudCx, layout) = AddRow(layout, 3, "cx:", 640, -10000, 100000);
        (_nudCy, layout) = AddRow(layout, 4, "cy:", 480, -10000, 100000);

        layout.Controls.Add(new Label { Text = "Distortion Coeffs", Font = new Font("Arial", 10, FontStyle.Bold), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        (_nudK1, layout) = AddRow(layout, 6, "k1:", 0m, -100m, 100m, 4);
        (_nudK2, layout) = AddRow(layout, 7, "k2:", 0m, -100m, 100m, 4);
        (_nudP1, layout) = AddRow(layout, 8, "p1:", 0m, -50m, 50m, 4);
        (_nudP2, layout) = AddRow(layout, 9, "p2:", 0m, -50m, 50m, 4);
        (_nudK3, layout) = AddRow(layout, 10, "k3:", 0m, -100m, 100m, 4);

        layout.Controls.Add(new Label { Text = "Image Size", Font = new Font("Arial", 10, FontStyle.Bold), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 11);
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        _nudImgW = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 1280, Width = 80 };
        _nudImgH = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 960, Width = 80 };
        var imgPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        imgPanel.Controls.Add(new Label { Text = "W:" });
        imgPanel.Controls.Add(_nudImgW);
        imgPanel.Controls.Add(new Label { Text = " H:" });
        imgPanel.Controls.Add(_nudImgH);
        layout.SetColumnSpan(imgPanel, 2);
        layout.Controls.Add(imgPanel, 0, 12);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var btnOk = new Button { Text = "Save", Width = 80 };
        btnOk.Click += (s, e) => { SaveAndClose(); };
        var btnCancel = new Button { Text = "Cancel", Width = 80 };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(btnPanel);
    }

    private static (NumericUpDown nud, TableLayoutPanel tlp) AddRow(TableLayoutPanel tlp, int row, string label, float value, float min, float max)
    {
        tlp.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        var nud = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)value, DecimalPlaces = 1, Dock = DockStyle.Fill };
        tlp.Controls.Add(nud, 1, row);
        return (nud, tlp);
    }

    private static (NumericUpDown nud, TableLayoutPanel tlp) AddRow(TableLayoutPanel tlp, int row, string label, decimal value, decimal min, decimal max, int decimals)
    {
        tlp.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        var nud = new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Dock = DockStyle.Fill };
        tlp.Controls.Add(nud, 1, row);
        return (nud, tlp);
    }

    private void SaveAndClose()
    {
        var store = CalibrationStore.Load();
        store.Intrinsics = new CameraIntrinsics
        {
            CameraMatrix = new[]
            {
                (double)_nudFx.Value, 0, (double)_nudCx.Value,
                0, (double)_nudFy.Value, (double)_nudCy.Value,
                0, 0, 1
            },
            DistCoeffs = new[]
            {
                (double)_nudK1.Value, (double)_nudK2.Value,
                (double)_nudP1.Value, (double)_nudP2.Value,
                (double)_nudK3.Value
            },
            CalibratedImageWidth = (int)_nudImgW.Value,
            CalibratedImageHeight = (int)_nudImgH.Value,
            ReprojectionError = 0,
            UsedViewCount = 0
        };
        store.Save();
        MessageBox.Show(this, "Calibration parameters saved.", "Saved");
        DialogResult = DialogResult.OK;
        Close();
    }
}
