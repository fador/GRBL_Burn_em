using laser_gui_test.Data;
using System.IO.Ports;

namespace laser_gui_test.Forms;

public class OptionsForm : Form
{
    private ComboBox _cbPorts = null!;
    private ComboBox _cbBaud = null!;
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
        this.Size = new Size(300, 200);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;

        var lblPort = new Label { Text = "COM Port:", Location = new Point(20, 20), AutoSize = true };
        _cbPorts = new ComboBox { Location = new Point(100, 17), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        
        var lblBaud = new Label { Text = "Baud Rate:", Location = new Point(20, 60), AutoSize = true };
        _cbBaud = new ComboBox { Location = new Point(100, 57), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbBaud.Items.AddRange(new object[] { 9600, 19200, 38400, 57600, 115200, 230400, 250000 });

        _btnSave = new Button { Text = "Save", Location = new Point(110, 110), DialogResult = DialogResult.OK };
        _btnCancel = new Button { Text = "Cancel", Location = new Point(195, 110), DialogResult = DialogResult.Cancel };

        _btnSave.Click += BtnSave_Click;

        this.Controls.Add(lblPort);
        this.Controls.Add(_cbPorts);
        this.Controls.Add(lblBaud);
        this.Controls.Add(_cbBaud);
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

        AppConfiguration.Instance.Save();
        this.Close();
    }
}
