/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Drawing;
using System.Windows.Forms;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em.Forms;

public class PowerSpeedCalibrationForm : Form
{
    private NumericUpDown _numMinSpeed = null!;
    private NumericUpDown _numMaxSpeed = null!;
    private NumericUpDown _numMinPower = null!;
    private NumericUpDown _numMaxPower = null!;
    private NumericUpDown _numRows = null!;
    private NumericUpDown _numCols = null!;
    private NumericUpDown _numCellSize = null!;
    private NumericUpDown _numSpacing = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    private RadioButton _rbCut = null!;
    private RadioButton _rbEngrave = null!;

    public CalibrationGridGenerator Generator { get; private set; } = new CalibrationGridGenerator();

    public PowerSpeedCalibrationForm()
    {
        InitializeComponent();
        
        // Load Defaults
        _numMinSpeed.Value = (decimal)Generator.MinSpeed;
        _numMaxSpeed.Value = (decimal)Generator.MaxSpeed;
        _numMinPower.Value = (decimal)Generator.MinPower;
        _numMaxPower.Value = (decimal)Generator.MaxPower;
        _numRows.Value = Generator.Rows;
        _numCols.Value = Generator.Cols;
        _numCellSize.Value = (decimal)Generator.CellSize;
        _numSpacing.Value = (decimal)Generator.Spacing;
        
        if (Generator.IsEngrave) _rbEngrave.Checked = true;
        else _rbCut.Checked = true;
    }

    private void InitializeComponent()
    {
        this.Text = "Power/Speed Calibration";
        this.Size = new Size(450, 480);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 10,
            ColumnCount = 2,
            Padding = new Padding(10),
            AutoSize = true
        };
        
        int row = 0;
        
        // Mode Selection
        panel.Controls.Add(new Label { Text = "Mode:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        var modePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        _rbCut = new RadioButton { Text = "Cut (Line)", Checked = true, AutoSize = true };
        _rbEngrave = new RadioButton { Text = "Engrave (Fill)", AutoSize = true };
        
        modePanel.Controls.Add(_rbCut);
        modePanel.Controls.Add(_rbEngrave);
        panel.Controls.Add(modePanel, 1, row++);

        // Speed Range
        panel.Controls.Add(new Label { Text = "Min Speed (mm/min):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _numMinSpeed = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 1000, DecimalPlaces = 0, Width = 100 };
        panel.Controls.Add(_numMinSpeed, 1, row++);
        
        panel.Controls.Add(new Label { Text = "Max Speed (mm/min):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _numMaxSpeed = new NumericUpDown { Minimum = 1, Maximum = 100000, Value = 5000, DecimalPlaces = 0, Width = 100 };
        panel.Controls.Add(_numMaxSpeed, 1, row++);

        // Power Range
        panel.Controls.Add(new Label { Text = "Min Power (%):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _numMinPower = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 10, DecimalPlaces = 1, Width = 100 };
        panel.Controls.Add(_numMinPower, 1, row++);
        
        panel.Controls.Add(new Label { Text = "Max Power (%):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _numMaxPower = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 100, DecimalPlaces = 1, Width = 100 };
        panel.Controls.Add(_numMaxPower, 1, row++);

        // Grid Dimensions
        var lblRows = new Label { Text = "Rows (Power Axis):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        panel.Controls.Add(lblRows, 0, row);
        _numRows = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 5, DecimalPlaces = 0, Width = 100 };
        panel.Controls.Add(_numRows, 1, row++);
        
        var lblCols = new Label { Text = "Cols (Speed Axis):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        panel.Controls.Add(lblCols, 0, row);
        _numCols = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 5, DecimalPlaces = 0, Width = 100 };
        panel.Controls.Add(_numCols, 1, row++);
        
        // Helper to update labels based on mode
        void UpdateLabels()
        {
            if (_rbEngrave.Checked)
            {
                lblRows.Text = "Rows (Speed Axis):";
                lblCols.Text = "Cols (Power Axis):";
            }
            else
            {
                lblRows.Text = "Rows (Power Axis):";
                lblCols.Text = "Cols (Speed Axis):";
            }
        }
        
        // Attach events NOW that labels exist
        _rbCut.CheckedChanged += (s, e) => UpdateLabels();
        _rbEngrave.CheckedChanged += (s, e) => UpdateLabels();

        // Cell Settings
        panel.Controls.Add(new Label { Text = "Cell Size (mm):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _numCellSize = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 10, DecimalPlaces = 1, Width = 100 };
        panel.Controls.Add(_numCellSize, 1, row++);
        
        panel.Controls.Add(new Label { Text = "Spacing (mm):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _numSpacing = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = 2, DecimalPlaces = 1, Width = 100 };
        panel.Controls.Add(_numSpacing, 1, row++);

        // Buttons
        var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Height = 40, AutoSize = true };
        _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        _btnOk = new Button { Text = "Generate", DialogResult = DialogResult.OK };
        
        _btnOk.Click += (s, e) =>
        {
            Generator.IsEngrave = _rbEngrave.Checked;
            Generator.MinSpeed = (float)_numMinSpeed.Value;
            Generator.MaxSpeed = (float)_numMaxSpeed.Value;
            Generator.MinPower = (float)_numMinPower.Value;
            Generator.MaxPower = (float)_numMaxPower.Value;
            Generator.Rows = (int)_numRows.Value;
            Generator.Cols = (int)_numCols.Value;
            Generator.CellSize = (float)_numCellSize.Value;
            Generator.Spacing = (float)_numSpacing.Value;
            this.Close();
        };

        btnPanel.Controls.Add(_btnCancel);
        btnPanel.Controls.Add(_btnOk);

        panel.Controls.Add(btnPanel, 0, row);
        panel.SetColumnSpan(btnPanel, 2);

        this.Controls.Add(panel);
        this.AcceptButton = _btnOk;
        this.CancelButton = _btnCancel;
        
        // Initial Label Update
        UpdateLabels();
    }
}
