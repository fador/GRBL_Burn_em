/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
namespace grbl_burn_em.Forms;

public class DebugCodeForm : Form
{
    private TextBox _txtGCode = null!;
    private Button _btnSave = null!;
    private Button _btnClose = null!;

    public DebugCodeForm(string gcode)
    {
        InitializeComponent();
        _txtGCode.Text = gcode;
        _txtGCode.Select(0, 0); // Deselect
    }

    private void InitializeComponent()
    {
        this.Text = "Generated G-Code Output";
        this.Size = new Size(600, 500);
        this.StartPosition = FormStartPosition.CenterParent;

        _txtGCode = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Top,
            Height = 400,
            ReadOnly = true,
            Font = new Font("Consolas", 10)
        };

        _btnSave = new Button { Text = "Save to File", Location = new Point(10, 410), Width = 100 };
        _btnClose = new Button { Text = "Close", Location = new Point(480, 410), Width = 100 };

        _btnSave.Click += BtnSave_Click;
        _btnClose.Click += (s, e) => this.Close();

        this.Controls.Add(_txtGCode);
        this.Controls.Add(_btnSave);
        this.Controls.Add(_btnClose);
        
        // Resize handling
        this.Resize += (s, e) => {
            _txtGCode.Height = this.ClientSize.Height - 50;
            _btnSave.Top = this.ClientSize.Height - 40;
            _btnClose.Top = this.ClientSize.Height - 40;
            _btnClose.Left = this.ClientSize.Width - 110;
        };
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "G-Code files (*.nc;*.gcode)|*.nc;*.gcode|All files (*.*)|*.*",
            FileName = "output.nc"
        };
        
        if (sfd.ShowDialog() == DialogResult.OK)
        {
            File.WriteAllText(sfd.FileName, _txtGCode.Text);
            MessageBox.Show("File saved.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
