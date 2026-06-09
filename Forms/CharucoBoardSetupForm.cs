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

public partial class CharucoBoardSetupForm : Form
{
    private ComboBox _cmbDict = null!;
    private NumericUpDown _nudSquaresX = null!;
    private NumericUpDown _nudSquaresY = null!;
    private NumericUpDown _nudSquareLen = null!;
    private NumericUpDown _nudMarkerLen = null!;
    private PictureBox _picPreview = null!;
    private Button _btnPreview = null!;
    private Button _btnSavePng = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    public CharucoBoardSetupForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "ChArUco Board Setup";
        Size = new Size(500, 550);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        layout.Controls.Add(new Label { Text = "Dictionary:", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _cmbDict = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbDict.Items.AddRange(CharucoBoardConfig.AvailableDictionaries);
        _cmbDict.SelectedIndex = 0;
        layout.Controls.Add(_cmbDict, 1, 0);

        layout.Controls.Add(new Label { Text = "Squares X:", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _nudSquaresX = new NumericUpDown { Minimum = 2, Maximum = 20, Value = 5, Dock = DockStyle.Fill };
        layout.Controls.Add(_nudSquaresX, 1, 1);

        layout.Controls.Add(new Label { Text = "Squares Y:", TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        _nudSquaresY = new NumericUpDown { Minimum = 2, Maximum = 20, Value = 7, Dock = DockStyle.Fill };
        layout.Controls.Add(_nudSquaresY, 1, 2);

        layout.Controls.Add(new Label { Text = "Square Length (mm):", TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        _nudSquareLen = new NumericUpDown { Minimum = 5, Maximum = 200, Value = 20, DecimalPlaces = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(_nudSquareLen, 1, 3);

        layout.Controls.Add(new Label { Text = "Marker Length (mm):", TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        _nudMarkerLen = new NumericUpDown { Minimum = 3, Maximum = 199, Value = 15, DecimalPlaces = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(_nudMarkerLen, 1, 4);

        var pnlBtns = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Left, FlowDirection = FlowDirection.LeftToRight };
        _btnPreview = new Button { Text = "Preview", Width = 100 };
        _btnPreview.Click += (s, e) => UpdatePreview();
        pnlBtns.Controls.Add(_btnPreview);
        _btnSavePng = new Button { Text = "Save as PNG...", Width = 120 };
        _btnSavePng.Click += (s, e) => SavePng();
        pnlBtns.Controls.Add(_btnSavePng);
        layout.Controls.Add(pnlBtns, 1, 5);

        _picPreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        layout.SetColumnSpan(_picPreview, 2);
        layout.Controls.Add(_picPreview, 0, 6);

        var pnlOk = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, FlowDirection = FlowDirection.RightToLeft };
        _btnCancel = new Button { Text = "Cancel", Width = 80 };
        _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnOk = new Button { Text = "OK", Width = 80 };
        _btnOk.Click += (s, e) => { SaveSettings(); DialogResult = DialogResult.OK; Close(); };
        pnlOk.Controls.Add(_btnCancel);
        pnlOk.Controls.Add(_btnOk);
        layout.SetColumnSpan(pnlOk, 2);
        layout.Controls.Add(pnlOk, 0, 7);

        Controls.Add(layout);
    }

    private void LoadSettings()
    {
        var store = CalibrationStore.Load();
        if (store.BoardConfig != null)
        {
            _cmbDict.SelectedItem = store.BoardConfig.DictionaryName;
            _nudSquaresX.Value = store.BoardConfig.SquaresX;
            _nudSquaresY.Value = store.BoardConfig.SquaresY;
            _nudSquareLen.Value = (decimal)store.BoardConfig.SquareLengthMm;
            _nudMarkerLen.Value = (decimal)store.BoardConfig.MarkerLengthMm;
        }
    }

    private void SaveSettings()
    {
        var store = CalibrationStore.Load();
        store.BoardConfig = new CharucoBoardConfig
        {
            DictionaryName = _cmbDict.SelectedItem?.ToString() ?? "DICT_4X4_50",
            SquaresX = (int)_nudSquaresX.Value,
            SquaresY = (int)_nudSquaresY.Value,
            SquareLengthMm = (float)_nudSquareLen.Value,
            MarkerLengthMm = (float)_nudMarkerLen.Value
        };
        store.Save();
    }

    private void UpdatePreview()
    {
        try
        {
            var config = new CharucoBoardConfig
            {
                DictionaryName = _cmbDict.SelectedItem?.ToString() ?? "DICT_4X4_50",
                SquaresX = (int)_nudSquaresX.Value,
                SquaresY = (int)_nudSquaresY.Value,
                SquareLengthMm = (float)_nudSquareLen.Value,
                MarkerLengthMm = (float)_nudMarkerLen.Value
            };
            using var bmp = config.GeneratePreviewImage();
            var old = _picPreview.Image;
            _picPreview.Image = new Bitmap(bmp);
            old?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Preview failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SavePng()
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "PNG Image|*.png",
            DefaultExt = ".png",
            Title = "Save ChArUco Board Image"
        };
        if (sfd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var config = new CharucoBoardConfig
                {
                    DictionaryName = _cmbDict.SelectedItem?.ToString() ?? "DICT_4X4_50",
                    SquaresX = (int)_nudSquaresX.Value,
                    SquaresY = (int)_nudSquaresY.Value,
                    SquareLengthMm = (float)_nudSquareLen.Value,
                    MarkerLengthMm = (float)_nudMarkerLen.Value
                };
                config.SavePreviewImage(sfd.FileName);
                MessageBox.Show(this, "Board image saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
