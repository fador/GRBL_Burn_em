using laser_gui_test.Data;
using System.IO.Ports;

namespace laser_gui_test.Forms;

public class OptionsForm : Form
{
    private ComboBox _cbPorts = null!;
    private ComboBox _cbBaud = null!;
    private ComboBox _cbGenerator = null!;
    private NumericUpDown _numWidth = null!;
    private NumericUpDown _numHeight = null!;
    private ComboBox _cbOrigin = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;

    public OptionsForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        this.Text = "Options";
        this.Size = new Size(300, 400);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;

        var lblPort = new Label { Text = "COM Port:", Location = new Point(20, 20), AutoSize = true };
        _cbPorts = new ComboBox { Location = new Point(100, 17), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        
        var lblBaud = new Label { Text = "Baud Rate:", Location = new Point(20, 60), AutoSize = true };
        _cbBaud = new ComboBox { Location = new Point(100, 57), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbBaud.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 250000 });

        var lblGen = new Label { Text = "Generator:", Location = new Point(20, 100), AutoSize = true };
        _cbGenerator = new ComboBox { Location = new Point(100, 97), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbGenerator.Items.AddRange(new object[] { "Grbl", "GCode", "Dummy" });

        var lblW = new Label { Text = "Width (mm):", Location = new Point(20, 140), AutoSize = true };
        _numWidth = new NumericUpDown { Location = new Point(100, 137), Width = 150, Minimum = 10, Maximum = 2000, DecimalPlaces = 0 };
        
        var lblH = new Label { Text = "Height (mm):", Location = new Point(20, 180), AutoSize = true };
        _numHeight = new NumericUpDown { Location = new Point(100, 177), Width = 150, Minimum = 10, Maximum = 2000, DecimalPlaces = 0 };

        var lblOrg = new Label { Text = "Origin:", Location = new Point(20, 220), AutoSize = true };
        _cbOrigin = new ComboBox { Location = new Point(100, 217), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbOrigin.Items.AddRange(new object[] { "BottomLeft", "TopLeft", "Center" });

        _btnSave = new Button { Text = "Save", Location = new Point(110, 280), DialogResult = DialogResult.OK };
        _btnCancel = new Button { Text = "Cancel", Location = new Point(195, 280), DialogResult = DialogResult.Cancel };

        _btnSave.Click += BtnSave_Click;

        this.Controls.Add(lblPort);
        this.Controls.Add(_cbPorts);
        this.Controls.Add(lblBaud);
        this.Controls.Add(_cbBaud);
        this.Controls.Add(lblGen);
        this.Controls.Add(_cbGenerator);
        this.Controls.Add(lblW);
        this.Controls.Add(_numWidth);
        this.Controls.Add(lblH);
        this.Controls.Add(_numHeight);
        this.Controls.Add(lblOrg);
        this.Controls.Add(_cbOrigin);
        this.Controls.Add(_btnSave);
        this.Controls.Add(_btnCancel);
        
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
        
        string org = AppConfiguration.Instance.WorkOrigin;
        if (_cbOrigin.Items.Contains(org)) _cbOrigin.SelectedItem = org;
        else _cbOrigin.SelectedIndex = 0;
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
        if(_cbOrigin.SelectedItem != null)
             AppConfiguration.Instance.WorkOrigin = _cbOrigin.SelectedItem.ToString() ?? "BottomLeft";

        AppConfiguration.Instance.Save();
        this.Close();
    }
}
