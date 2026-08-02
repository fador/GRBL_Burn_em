using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using grbl_burn_em.Data;

namespace grbl_burn_em.Forms;

public class PresetSelectionForm : Form
{
    private ListBox _lstPresets = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;
    private Label _lblDetails = null!;

    public MachineProfile? SelectedProfile { get; private set; }

    private List<MachineProfile> _presets;

    public PresetSelectionForm()
    {
        _presets = PresetManager.LoadPresets();
        
        // Add a completely blank preset
        var blankProfile = new MachineProfile { Name = "Blank Profile" };
        _presets.Insert(0, blankProfile);

        InitializeComponent();
        PopulateList();
    }

    private void InitializeComponent()
    {
        this.Text = "Select Machine Preset";
        this.Size = new Size(350, 400);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;

        var lblPrompt = new Label 
        { 
            Text = "Choose a starting preset for your new machine:",
            Location = new Point(15, 15),
            AutoSize = true
        };

        _lstPresets = new ListBox
        {
            Location = new Point(15, 40),
            Size = new Size(300, 160)
        };
        _lstPresets.SelectedIndexChanged += LstPresets_SelectedIndexChanged;
        _lstPresets.DoubleClick += (s, e) => { if (_lstPresets.SelectedIndex >= 0) _btnOk.PerformClick(); };

        _lblDetails = new Label
        {
            Location = new Point(15, 210),
            Size = new Size(300, 100),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(5)
        };

        _btnOk = new Button
        {
            Text = "OK",
            Location = new Point(160, 320),
            Width = 75,
            DialogResult = DialogResult.OK,
            Enabled = false
        };
        _btnOk.Click += BtnOk_Click;

        _btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(240, 320),
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        this.Controls.Add(lblPrompt);
        this.Controls.Add(_lstPresets);
        this.Controls.Add(_lblDetails);
        this.Controls.Add(_btnOk);
        this.Controls.Add(_btnCancel);
        this.AcceptButton = _btnOk;
        this.CancelButton = _btnCancel;
    }

    private void PopulateList()
    {
        _lstPresets.Items.Clear();
        foreach (var p in _presets)
        {
            _lstPresets.Items.Add(p.Name);
        }
        if (_lstPresets.Items.Count > 0)
        {
            _lstPresets.SelectedIndex = 0;
        }
    }

    private void LstPresets_SelectedIndexChanged(object? sender, EventArgs e)
    {
        int idx = _lstPresets.SelectedIndex;
        _btnOk.Enabled = idx >= 0;

        if (idx >= 0 && idx < _presets.Count)
        {
            var p = _presets[idx];
            if (p.Name == "Blank Profile")
            {
                _lblDetails.Text = "Creates a new machine profile with default empty settings.";
            }
            else
            {
                _lblDetails.Text = $"Type: {p.Type}\n" +
                                   $"Generator: {p.GCodeGenerator}\n" +
                                   $"Work Area: {p.WorkAreaWidth}x{p.WorkAreaHeight}mm\n" +
                                   $"Tool Commands: ON={p.ToolOnCommand}, OFF={p.ToolOffCommand}\n" +
                                   $"PWM Enabled: {p.EnablePWM}";
            }
        }
        else
        {
            _lblDetails.Text = "";
        }
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        int idx = _lstPresets.SelectedIndex;
        if (idx >= 0 && idx < _presets.Count)
        {
            var chosen = _presets[idx].Clone();
            
            // If they chose Blank Profile, we name it "New Machine". 
            // If they chose a generic preset, we might name it "New Generic Laser" or keep the preset name.
            if (chosen.Name.Contains("(Copy)"))
            {
                chosen.Name = chosen.Name.Replace(" (Copy)", "");
            }
            if (_presets[idx].Name == "Blank Profile")
            {
                chosen.Name = "New Machine";
            }
            
            SelectedProfile = chosen;
        }
    }
}
