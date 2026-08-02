/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using grbl_burn_em.Data;

namespace grbl_burn_em.Forms;

public class OptionsForm : Form
{
    private ComboBox _cbProfiles = null!;
    private Button _btnAddProfile = null!;
    private Button _btnDeleteProfile = null!;
    private List<MachineProfile> _editingProfiles = new();
    private int _currentProfileIndex = -1;
    private bool _isUpdatingUI = false;

    private ComboBox _cbDeviceType = null!;
    private TextBox _txtProfileName = null!;

    private ComboBox _cbPorts = null!;
    private ComboBox _cbBaud = null!;
    private ComboBox _cbGenerator = null!;
    private NumericUpDown _numWidth = null!;
    private NumericUpDown _numHeight = null!;
    private NumericUpDown _numInterval = null!;
    private NumericUpDown _numMinSegment = null!;
    private NumericUpDown _numSnapGrid = null!;
    private NumericUpDown _numTravelSpeed = null!;

    // Marlin / Plotter Controls
    private TextBox _txtToolOn = null!;
    private TextBox _txtToolOff = null!;
    private TextBox _txtPwmCmd = null!;
    private CheckBox _chkEnablePwm = null!;

    private CheckBox _chkBicubic = null!;
    private CheckBox _chkDither = null!;
    private CheckBox _chkSkipSplash = null!;
    private ComboBox _cbOrigin = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;
    private NumericUpDown _numSvgQuality = null!;
    private CheckBox _chkEmbedImages = null!;
    private CheckBox _chkSafetyBounds = null!;

    private TabControl _tabs = null!;

    public void SelectTab(string tabName)
    {
        foreach (TabPage tab in _tabs.TabPages)
        {
            if (tab.Text == tabName)
            {
                _tabs.SelectedTab = tab;
                break;
            }
        }
    }

    public OptionsForm(IEnumerable<string>? extraGenerators = null)
    {
        InitializeComponent();
        if (extraGenerators != null)
        {
            foreach (var gen in extraGenerators)
            {
                if (!_cbGenerator.Items.Contains(gen))
                    _cbGenerator.Items.Add(gen);
            }
        }
        LoadSettings();
    }

    private void InitializeComponent()
    {
        this.Text = "Options";
        this.Size = new Size(420, 650);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50 };
        var lblProf = new Label { Text = "Profile:", Location = new Point(20, 15), AutoSize = true };
        _cbProfiles = new ComboBox { Location = new Point(70, 12), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        _btnAddProfile = new Button { Text = "Add", Location = new Point(240, 11), Width = 60 };
        _btnDeleteProfile = new Button { Text = "Delete", Location = new Point(310, 11), Width = 60 };
        
        pnlTop.Controls.Add(lblProf);
        pnlTop.Controls.Add(_cbProfiles);
        pnlTop.Controls.Add(_btnAddProfile);
        pnlTop.Controls.Add(_btnDeleteProfile);

        _btnAddProfile.Click += BtnAddProfile_Click;
        _btnDeleteProfile.Click += BtnDeleteProfile_Click;
        _cbProfiles.SelectedIndexChanged += CbProfiles_SelectedIndexChanged;

        _tabs = new TabControl { Dock = DockStyle.Fill };

        // --- Connection Tab ---
        var tabConnection = new TabPage("Connection");
        
        var lblName = new Label { Text = "Profile Name:", Location = new Point(20, 20), AutoSize = true };
        _txtProfileName = new TextBox { Location = new Point(120, 17), Width = 180 };
        _txtProfileName.TextChanged += (s, e) => { if (!_isUpdatingUI && _currentProfileIndex >= 0) { _editingProfiles[_currentProfileIndex].Name = _txtProfileName.Text; UpdateProfileComboText(); } };

        var lblType = new Label { Text = "Device Type:", Location = new Point(20, 60), AutoSize = true };
        _cbDeviceType = new ComboBox { Location = new Point(120, 57), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbDeviceType.Items.AddRange(Enum.GetNames(typeof(DeviceType)));

        var lblPort = new Label { Text = "COM Port:", Location = new Point(20, 100), AutoSize = true };
        _cbPorts = new ComboBox { Location = new Point(120, 97), Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
        
        var lblBaud = new Label { Text = "Baud Rate:", Location = new Point(20, 140), AutoSize = true };
        _cbBaud = new ComboBox { Location = new Point(120, 137), Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
        _cbBaud.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 250000 });

        tabConnection.Controls.Add(lblName);
        tabConnection.Controls.Add(_txtProfileName);
        tabConnection.Controls.Add(lblType);
        tabConnection.Controls.Add(_cbDeviceType);
        tabConnection.Controls.Add(lblPort);
        tabConnection.Controls.Add(_cbPorts);
        tabConnection.Controls.Add(lblBaud);
        tabConnection.Controls.Add(_cbBaud);
        _tabs.TabPages.Add(tabConnection);

        // --- Machine Tab ---
        var tabMachine = new TabPage("Machine");

        var lblGen = new Label { Text = "Generator:", Location = new Point(20, 30), AutoSize = true };
        _cbGenerator = new ComboBox { Location = new Point(160, 27), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbGenerator.Items.AddRange(new object[] { "Grbl", "Marlin", "Dummy" });

        var lblW = new Label { Text = "Work Width (mm):", Location = new Point(20, 70), AutoSize = true };
        _numWidth = new NumericUpDown { Location = new Point(160, 67), Width = 150, Minimum = 10, Maximum = 2000, DecimalPlaces = 0 };
        
        var lblH = new Label { Text = "Work Height (mm):", Location = new Point(20, 110), AutoSize = true };
        _numHeight = new NumericUpDown { Location = new Point(160, 107), Width = 150, Minimum = 10, Maximum = 2000, DecimalPlaces = 0 };

        var lblOrg = new Label { Text = "Work Origin:", Location = new Point(20, 150), AutoSize = true };
        _cbOrigin = new ComboBox { Location = new Point(160, 147), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbOrigin.Items.AddRange(new object[] { "BottomLeft", "TopLeft", "Center" });

        var lblTravel = new Label { Text = "Travel Speed:", Location = new Point(20, 190), AutoSize = true };
        _numTravelSpeed = new NumericUpDown { Location = new Point(160, 187), Width = 150, Minimum = 100, Maximum = 20000, DecimalPlaces = 0, Increment = 100 };

        tabMachine.Controls.Add(lblGen);
        tabMachine.Controls.Add(_cbGenerator);
        tabMachine.Controls.Add(lblW);
        tabMachine.Controls.Add(_numWidth);
        tabMachine.Controls.Add(lblH);
        tabMachine.Controls.Add(_numHeight);
        tabMachine.Controls.Add(lblOrg);
        tabMachine.Controls.Add(_cbOrigin);
        tabMachine.Controls.Add(lblTravel);
        tabMachine.Controls.Add(_numTravelSpeed);

        var lblOn = new Label { Text = "Tool ON Cmd:", Location = new Point(20, 230), AutoSize = true };
        _txtToolOn = new TextBox { Location = new Point(160, 227), Width = 150, Height = 50, Multiline = true, ScrollBars = ScrollBars.Vertical };

        var lblOff = new Label { Text = "Tool OFF Cmd:", Location = new Point(20, 290), AutoSize = true };
        _txtToolOff = new TextBox { Location = new Point(160, 287), Width = 150, Height = 50, Multiline = true, ScrollBars = ScrollBars.Vertical };

        var lblPwm = new Label { Text = "PWM Cmd:", Location = new Point(20, 350), AutoSize = true };
        _txtPwmCmd = new TextBox { Location = new Point(160, 347), Width = 50 };

        _chkEnablePwm = new CheckBox { Text = "Enable PWM", Location = new Point(220, 349), AutoSize = true };

        tabMachine.Controls.Add(lblOn);
        tabMachine.Controls.Add(_txtToolOn);
        tabMachine.Controls.Add(lblOff);
        tabMachine.Controls.Add(_txtToolOff);
        tabMachine.Controls.Add(lblPwm);
        tabMachine.Controls.Add(_txtPwmCmd);
        tabMachine.Controls.Add(_chkEnablePwm);

        _tabs.TabPages.Add(tabMachine);

        // --- Raster / Image Tab (Global) ---
        var tabRaster = new TabPage("Raster / Image");
        
        var lblInterval = new Label { Text = "Line Interval (mm):", Location = new Point(20, 30), AutoSize = true };
        _numInterval = new NumericUpDown { Location = new Point(160, 27), Width = 120, Minimum = 0.01m, Maximum = 5.0m, DecimalPlaces = 3, Increment = 0.01m };

        var lblMinSeg = new Label { Text = "Min Segment (mm):", Location = new Point(20, 70), AutoSize = true };
        _numMinSegment = new NumericUpDown { Location = new Point(160, 67), Width = 120, Minimum = 0m, Maximum = 10.0m, DecimalPlaces = 2, Increment = 0.1m };

        _chkBicubic = new CheckBox { Text = "Bicubic Resampling", Location = new Point(20, 110), AutoSize = true };
        _chkDither = new CheckBox { Text = "Enable 1-bit Dithering", Location = new Point(20, 140), AutoSize = true };

        tabRaster.Controls.Add(lblInterval);
        tabRaster.Controls.Add(_numInterval);
        tabRaster.Controls.Add(lblMinSeg);
        tabRaster.Controls.Add(_numMinSegment);
        tabRaster.Controls.Add(_chkBicubic);
        tabRaster.Controls.Add(_chkDither);
        _tabs.TabPages.Add(tabRaster);

        // --- View / Grid Tab (Global) ---
        var tabView = new TabPage("View / Grid");

        var lblSnap = new Label { Text = "Snap Grid Size (mm):", Location = new Point(20, 30), AutoSize = true };
        _numSnapGrid = new NumericUpDown { Location = new Point(160, 27), Width = 120, Minimum = 0.1m, Maximum = 100.0m, DecimalPlaces = 2, Increment = 0.5m };

        _chkSkipSplash = new CheckBox { Text = "Skip Splash Screen", Location = new Point(20, 70), AutoSize = true };

        tabView.Controls.Add(lblSnap);
        tabView.Controls.Add(_numSnapGrid);
        tabView.Controls.Add(_chkSkipSplash);
        _tabs.TabPages.Add(tabView);

        // --- Import / Files Tab (Global) ---
        var tabImport = new TabPage("Files");
        
        var lblSvgQ = new Label { Text = "SVG Curve Flatness:", Location = new Point(20, 30), AutoSize = true };
        _numSvgQuality = new NumericUpDown { Location = new Point(160, 27), Width = 100, Minimum = 0.001m, Maximum = 10.0m, DecimalPlaces = 4, Increment = 0.001m };

        tabImport.Controls.Add(lblSvgQ);
        tabImport.Controls.Add(_numSvgQuality);
        
        _chkEmbedImages = new CheckBox { Text = "Embed Images in Project File (Base64)", Location = new Point(20, 70), AutoSize = true, Width = 300 };
        tabImport.Controls.Add(_chkEmbedImages);

        _chkSafetyBounds = new CheckBox { Text = "Enable Safety Boundary Check", Location = new Point(20, 100), AutoSize = true, Width = 300 };
        tabImport.Controls.Add(_chkSafetyBounds);

        _tabs.TabPages.Add(tabImport);

        // --- Bottom Panel for Buttons ---
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
        
        _btnSave = new Button { Text = "Save", Location = new Point(210, 10), DialogResult = DialogResult.OK, Width = 80 };
        _btnCancel = new Button { Text = "Cancel", Location = new Point(300, 10), DialogResult = DialogResult.Cancel, Width = 80 };
        
        _btnSave.Click += BtnSave_Click;
        
        pnlBottom.Controls.Add(_btnSave);
        pnlBottom.Controls.Add(_btnCancel);

        this.Controls.Add(_tabs);
        this.Controls.Add(pnlTop);
        this.Controls.Add(pnlBottom);
        
        this.AcceptButton = _btnSave;
        this.CancelButton = _btnCancel;
    }

    private void UpdateProfileComboText()
    {
        if (_cbProfiles.SelectedIndex >= 0 && _currentProfileIndex >= 0)
        {
            int idx = _cbProfiles.SelectedIndex;
            _cbProfiles.Items[idx] = _editingProfiles[_currentProfileIndex].Name;
        }
    }

    private void CbProfiles_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingUI) return;
        
        // Save current to editing list
        if (_currentProfileIndex >= 0 && _currentProfileIndex < _editingProfiles.Count)
        {
            SaveUIToProfile(_editingProfiles[_currentProfileIndex]);
        }

        _currentProfileIndex = _cbProfiles.SelectedIndex;
        
        if (_currentProfileIndex >= 0 && _currentProfileIndex < _editingProfiles.Count)
        {
            LoadProfileToUI(_editingProfiles[_currentProfileIndex]);
        }
    }

    private void BtnAddProfile_Click(object? sender, EventArgs e)
    {
        using (var dlg = new PresetSelectionForm())
        {
            if (dlg.ShowDialog() == DialogResult.OK && dlg.SelectedProfile != null)
            {
                var newProfile = dlg.SelectedProfile;
                _editingProfiles.Add(newProfile);
                _cbProfiles.Items.Add(newProfile.Name);
                _cbProfiles.SelectedIndex = _cbProfiles.Items.Count - 1;
            }
        }
    }

    private void BtnDeleteProfile_Click(object? sender, EventArgs e)
    {
        if (_editingProfiles.Count <= 1)
        {
            MessageBox.Show("You must have at least one machine profile.");
            return;
        }

        if (_currentProfileIndex >= 0)
        {
            _editingProfiles.RemoveAt(_currentProfileIndex);
            _cbProfiles.Items.RemoveAt(_currentProfileIndex);
            _currentProfileIndex = -1;
            _cbProfiles.SelectedIndex = 0;
        }
    }

    private void LoadSettings()
    {
        // Clone profiles
        _editingProfiles.Clear();
        foreach (var p in AppConfiguration.Instance.MachineProfiles)
        {
            _editingProfiles.Add(p.Clone());
        }

        // Populate Ports globally
        var ports = SerialInterface.Instance.GetAvailablePorts();
        _cbPorts.Items.Clear();
        _cbPorts.Items.AddRange(ports);

        _isUpdatingUI = true;
        _cbProfiles.Items.Clear();
        foreach (var p in _editingProfiles)
        {
            _cbProfiles.Items.Add(p.Name);
        }
        _isUpdatingUI = false;

        // Load Global Settings
        _numInterval.Value = (decimal)AppConfiguration.Instance.RasterLineInterval;
        _numMinSegment.Value = (decimal)AppConfiguration.Instance.MinRasterSegmentLength;
        _numSnapGrid.Value = (decimal)AppConfiguration.Instance.SnapGridSize;
        
        _chkBicubic.Checked = AppConfiguration.Instance.EnableBicubicResampling;
        _chkDither.Checked = AppConfiguration.Instance.Enable1BitDithering;
        _chkSkipSplash.Checked = AppConfiguration.Instance.SkipSplashScreen;
        
        decimal q = (decimal)AppConfiguration.Instance.SvgCurveQuality;
        if (q < _numSvgQuality.Minimum) q = _numSvgQuality.Minimum;
        if (q > _numSvgQuality.Maximum) q = _numSvgQuality.Maximum;
        _numSvgQuality.Value = q;
        
        _chkEmbedImages.Checked = AppConfiguration.Instance.EmbedImagesInProject;
        _chkSafetyBounds.Checked = AppConfiguration.Instance.EnableSafetyBoundsCheck;

        // Select Active Profile
        int activeIdx = _editingProfiles.FindIndex(p => p.Id == AppConfiguration.Instance.ActiveProfileId);
        if (activeIdx >= 0)
        {
            _cbProfiles.SelectedIndex = activeIdx;
        }
        else if (_editingProfiles.Count > 0)
        {
            _cbProfiles.SelectedIndex = 0;
        }
    }

    private void LoadProfileToUI(MachineProfile profile)
    {
        _isUpdatingUI = true;
        _txtProfileName.Text = profile.Name;
        
        if (_cbDeviceType.Items.Contains(profile.Type.ToString()))
            _cbDeviceType.SelectedItem = profile.Type.ToString();
        else
            _cbDeviceType.SelectedIndex = 0;

        if (!string.IsNullOrEmpty(profile.PortName) && !_cbPorts.Items.Contains(profile.PortName))
        {
             _cbPorts.Items.Add(profile.PortName);
        }
        _cbPorts.Text = profile.PortName;

        if (_cbBaud.Items.Contains(profile.BaudRate))
        {
            _cbBaud.SelectedItem = profile.BaudRate;
        }
        else
        {
            _cbBaud.Text = profile.BaudRate.ToString();
        }

        if (_cbGenerator.Items.Contains(profile.GCodeGenerator))
            _cbGenerator.SelectedItem = profile.GCodeGenerator;
        else
            _cbGenerator.SelectedIndex = 0;

        _numWidth.Value = (decimal)profile.WorkAreaWidth;
        _numHeight.Value = (decimal)profile.WorkAreaHeight;
        
        if (_cbOrigin.Items.Contains(profile.WorkOrigin)) 
            _cbOrigin.SelectedItem = profile.WorkOrigin;
        else 
            _cbOrigin.SelectedIndex = 0;

        _numTravelSpeed.Value = (decimal)profile.DefaultTravelSpeed;

        _txtToolOn.Text = profile.ToolOnCommand;
        _txtToolOff.Text = profile.ToolOffCommand;
        _txtPwmCmd.Text = profile.PwmCommand;
        _chkEnablePwm.Checked = profile.EnablePWM;
        _isUpdatingUI = false;
    }

    private void SaveUIToProfile(MachineProfile profile)
    {
        profile.Name = _txtProfileName.Text;
        if (Enum.TryParse<DeviceType>(_cbDeviceType.SelectedItem?.ToString(), out var dt))
        {
            profile.Type = dt;
        }
        profile.PortName = _cbPorts.Text;
        if (int.TryParse(_cbBaud.Text, out int baud)) profile.BaudRate = baud;
        
        if (_cbGenerator.SelectedItem != null)
            profile.GCodeGenerator = _cbGenerator.SelectedItem.ToString() ?? "Grbl";

        profile.WorkAreaWidth = (float)_numWidth.Value;
        profile.WorkAreaHeight = (float)_numHeight.Value;
        if (_cbOrigin.SelectedItem != null)
             profile.WorkOrigin = _cbOrigin.SelectedItem.ToString() ?? "BottomLeft";

        profile.DefaultTravelSpeed = (float)_numTravelSpeed.Value;
        profile.ToolOnCommand = _txtToolOn.Text;
        profile.ToolOffCommand = _txtToolOff.Text;
        profile.PwmCommand = _txtPwmCmd.Text;
        profile.EnablePWM = _chkEnablePwm.Checked;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        // Save current UI state to the selected profile
        if (_currentProfileIndex >= 0 && _currentProfileIndex < _editingProfiles.Count)
        {
            SaveUIToProfile(_editingProfiles[_currentProfileIndex]);
        }

        // Apply global settings
        AppConfiguration.Instance.RasterLineInterval = (float)_numInterval.Value;
        AppConfiguration.Instance.MinRasterSegmentLength = (float)_numMinSegment.Value;
        AppConfiguration.Instance.SnapGridSize = (float)_numSnapGrid.Value;

        AppConfiguration.Instance.EnableBicubicResampling = _chkBicubic.Checked;
        AppConfiguration.Instance.Enable1BitDithering = _chkDither.Checked;
        AppConfiguration.Instance.SkipSplashScreen = _chkSkipSplash.Checked;
        AppConfiguration.Instance.SvgCurveQuality = (float)_numSvgQuality.Value;
        AppConfiguration.Instance.EmbedImagesInProject = _chkEmbedImages.Checked;
        AppConfiguration.Instance.EnableSafetyBoundsCheck = _chkSafetyBounds.Checked;

        // Apply profiles
        AppConfiguration.Instance.MachineProfiles = _editingProfiles.Select(p => p.Clone()).ToList();
        
        if (_currentProfileIndex >= 0 && _currentProfileIndex < AppConfiguration.Instance.MachineProfiles.Count)
        {
            AppConfiguration.Instance.ActiveProfileId = AppConfiguration.Instance.MachineProfiles[_currentProfileIndex].Id;
        }
        else if (AppConfiguration.Instance.MachineProfiles.Count > 0)
        {
            AppConfiguration.Instance.ActiveProfileId = AppConfiguration.Instance.MachineProfiles[0].Id;
        }

        AppConfiguration.Instance.Save();
        this.Close();
    }
}
