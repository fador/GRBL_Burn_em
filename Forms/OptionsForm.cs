using grbl_burn_em.Data;


namespace grbl_burn_em.Forms;

public class OptionsForm : Form
{
    private ComboBox _cbPorts = null!;
    private ComboBox _cbBaud = null!;
    private ComboBox _cbGenerator = null!;
    private NumericUpDown _numWidth = null!;
    private NumericUpDown _numHeight = null!;
    private NumericUpDown _numInterval = null!;
    private NumericUpDown _numMinSegment = null!;
    private NumericUpDown _numSnapGrid = null!;
    private CheckBox _chkBicubic = null!;
    private CheckBox _chkDither = null!;
    private CheckBox _chkSkipSplash = null!;
    private ComboBox _cbOrigin = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;
    private NumericUpDown _numSvgQuality = null!;
    private CheckBox _chkEmbedImages = null!;

    public OptionsForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        this.Text = "Options";
        this.Size = new Size(400, 480);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        // --- Connection Tab ---
        var tabConnection = new TabPage("Connection");
        var lblPort = new Label { Text = "COM Port:", Location = new Point(20, 30), AutoSize = true };
        _cbPorts = new ComboBox { Location = new Point(120, 27), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        
        var lblBaud = new Label { Text = "Baud Rate:", Location = new Point(20, 70), AutoSize = true };
        _cbBaud = new ComboBox { Location = new Point(120, 67), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbBaud.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 250000 });

        tabConnection.Controls.Add(lblPort);
        tabConnection.Controls.Add(_cbPorts);
        tabConnection.Controls.Add(lblBaud);
        tabConnection.Controls.Add(_cbBaud);
        tabs.TabPages.Add(tabConnection);

        // --- Machine Tab ---
        var tabMachine = new TabPage("Machine");

        var lblGen = new Label { Text = "Generator:", Location = new Point(20, 30), AutoSize = true };
        _cbGenerator = new ComboBox { Location = new Point(140, 27), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbGenerator.Items.AddRange(new object[] { "Grbl", "GCode", "Dummy" });

        var lblW = new Label { Text = "Work Width (mm):", Location = new Point(20, 70), AutoSize = true };
        _numWidth = new NumericUpDown { Location = new Point(140, 67), Width = 150, Minimum = 10, Maximum = 2000, DecimalPlaces = 0 };
        
        var lblH = new Label { Text = "Work Height (mm):", Location = new Point(20, 110), AutoSize = true };
        _numHeight = new NumericUpDown { Location = new Point(140, 107), Width = 150, Minimum = 10, Maximum = 2000, DecimalPlaces = 0 };

        var lblOrg = new Label { Text = "Work Origin:", Location = new Point(20, 150), AutoSize = true };
        _cbOrigin = new ComboBox { Location = new Point(140, 147), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbOrigin.Items.AddRange(new object[] { "BottomLeft", "TopLeft", "Center" });

        tabMachine.Controls.Add(lblGen);
        tabMachine.Controls.Add(_cbGenerator);
        tabMachine.Controls.Add(lblW);
        tabMachine.Controls.Add(_numWidth);
        tabMachine.Controls.Add(lblH);
        tabMachine.Controls.Add(_numHeight);
        tabMachine.Controls.Add(lblOrg);
        tabMachine.Controls.Add(_cbOrigin);
        tabs.TabPages.Add(tabMachine);

        // --- Raster / Image Tab ---
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
        tabs.TabPages.Add(tabRaster);

        // --- View / Grid Tab ---
        var tabView = new TabPage("View / Grid");

        var lblSnap = new Label { Text = "Snap Grid Size (mm):", Location = new Point(20, 30), AutoSize = true };
        _numSnapGrid = new NumericUpDown { Location = new Point(160, 27), Width = 120, Minimum = 0.1m, Maximum = 100.0m, DecimalPlaces = 2, Increment = 0.5m };

        _chkSkipSplash = new CheckBox { Text = "Skip Splash Screen", Location = new Point(20, 70), AutoSize = true };

        tabView.Controls.Add(lblSnap);
        tabView.Controls.Add(_numSnapGrid);
        tabView.Controls.Add(_chkSkipSplash);
        tabs.TabPages.Add(tabView);

        // --- Import / Files Tab ---
        var tabImport = new TabPage("Files");
        
        var lblSvgQ = new Label { Text = "SVG Curve Flatness (Lower=More Points):", Location = new Point(20, 30), AutoSize = true };
        _numSvgQuality = new NumericUpDown { Location = new Point(250, 27), Width = 100, Minimum = 0.001m, Maximum = 10.0m, DecimalPlaces = 4, Increment = 0.001m };

        tabImport.Controls.Add(lblSvgQ);
        tabImport.Controls.Add(_numSvgQuality);
        
        _chkEmbedImages = new CheckBox { Text = "Embed Images in Project File (Base64)", Location = new Point(20, 70), AutoSize = true, Width = 300 };
        tabImport.Controls.Add(_chkEmbedImages);

        tabs.TabPages.Add(tabImport);

        // --- Bottom Panel for Buttons ---
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
        
        _btnSave = new Button { Text = "Save", Location = new Point(180, 10), DialogResult = DialogResult.OK, Width = 80 };
        _btnCancel = new Button { Text = "Cancel", Location = new Point(270, 10), DialogResult = DialogResult.Cancel, Width = 80 };
        
        _btnSave.Click += BtnSave_Click;
        
        pnlBottom.Controls.Add(_btnSave);
        pnlBottom.Controls.Add(_btnCancel);

        this.Controls.Add(tabs); // Tabs Fill
        this.Controls.Add(pnlBottom); // Bottom panel dock
        
        this.AcceptButton = _btnSave;
        this.CancelButton = _btnCancel;
    }

    private void LoadSettings()
    {
        // Populate Ports
        var ports = SerialInterface.Instance.GetAvailablePorts();
        _cbPorts.Items.Clear();
        _cbPorts.Items.AddRange(ports);

        // Select Configured Port
        string lastPort = AppConfiguration.Instance.LastPortName;
        if (!string.IsNullOrEmpty(lastPort) && _cbPorts.Items.Contains(lastPort))
        {
            _cbPorts.SelectedItem = lastPort;
        }
        else if (_cbPorts.Items.Count > 0)
        {
            _cbPorts.SelectedIndex = 0;
        }

        // Select Configured Baud
        int baud = AppConfiguration.Instance.BaudRate;
        if (_cbBaud.Items.Contains(baud))
        {
            _cbBaud.SelectedItem = baud;
        }
        else
        {
            _cbBaud.SelectedItem = 115200;
        }

        // Select Configured Generator
        string gen = AppConfiguration.Instance.GCodeGenerator;
        if (_cbGenerator.Items.Contains(gen))
        {
            _cbGenerator.SelectedItem = gen;
        }
        else
        {
            _cbGenerator.SelectedIndex = 0;
        }

        _numWidth.Value = (decimal)AppConfiguration.Instance.WorkAreaWidth;
        _numHeight.Value = (decimal)AppConfiguration.Instance.WorkAreaHeight;
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
        
        string org = AppConfiguration.Instance.WorkOrigin;
        if (_cbOrigin.Items.Contains(org)) _cbOrigin.SelectedItem = org;
        else _cbOrigin.SelectedIndex = 0;
        
        _chkEmbedImages.Checked = AppConfiguration.Instance.EmbedImagesInProject;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_cbPorts.SelectedItem != null)
        {
            AppConfiguration.Instance.LastPortName = _cbPorts.SelectedItem.ToString() ?? "";
        }
        
        if (_cbBaud.SelectedItem != null && int.TryParse(_cbBaud.SelectedItem.ToString(), out int baud))
        {
            AppConfiguration.Instance.BaudRate = baud;
        }

        if (_cbGenerator.SelectedItem != null)
        {
            AppConfiguration.Instance.GCodeGenerator = _cbGenerator.SelectedItem.ToString() ?? "Grbl";
        }

        AppConfiguration.Instance.WorkAreaWidth = (float)_numWidth.Value;
        AppConfiguration.Instance.WorkAreaHeight = (float)_numHeight.Value;
        AppConfiguration.Instance.RasterLineInterval = (float)_numInterval.Value;
        AppConfiguration.Instance.MinRasterSegmentLength = (float)_numMinSegment.Value;
        AppConfiguration.Instance.SnapGridSize = (float)_numSnapGrid.Value;
        AppConfiguration.Instance.EnableBicubicResampling = _chkBicubic.Checked;
        AppConfiguration.Instance.Enable1BitDithering = _chkDither.Checked;
        AppConfiguration.Instance.SkipSplashScreen = _chkSkipSplash.Checked;
        AppConfiguration.Instance.SvgCurveQuality = (float)_numSvgQuality.Value;
        AppConfiguration.Instance.EmbedImagesInProject = _chkEmbedImages.Checked;

        if(_cbOrigin.SelectedItem != null)
             AppConfiguration.Instance.WorkOrigin = _cbOrigin.SelectedItem.ToString() ?? "BottomLeft";

        AppConfiguration.Instance.Save();
        this.Close();
    }
}
