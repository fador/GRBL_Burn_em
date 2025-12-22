using System;
using System.Drawing;
using System.Windows.Forms;
using grbl_burn_em.Data;

namespace grbl_burn_em.Forms;

public class LayerSettingsForm : Form
{
    public string LayerName { get; private set; }
    public Color LayerColor { get; private set; }
    public float LayerSpeed { get; private set; }
    public float LayerPower { get; private set; }
    public LayerMode LayerMode { get; private set; }

    private TextBox _txtName = null!;
    private Button _btnColor = null!;
    private NumericUpDown _numSpeed = null!;
    private NumericUpDown _numPower = null!;
    private ComboBox _cmbMode = null!;

    public LayerSettingsForm(Layer layer)
    {
        Text = "Layer Settings";
        Size = new Size(300, 350);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        LayerName = layer.Name;
        LayerColor = layer.Color;
        LayerSpeed = layer.Speed;
        LayerPower = layer.Power;
        LayerMode = layer.Mode;

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(10),
            AutoSize = true
        };
        
        // Name
        layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Left }, 0, 0);
        _txtName = new TextBox { Text = LayerName, Width = 150 };
        layout.Controls.Add(_txtName, 1, 0);

        // Color
        layout.Controls.Add(new Label { Text = "Color:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _btnColor = new Button { BackColor = LayerColor, Width = 100, Text = "" };
        _btnColor.Click += (s, e) => 
        {
            using var cd = new ColorDialog { Color = LayerColor };
            if (cd.ShowDialog() == DialogResult.OK)
            {
                LayerColor = cd.Color;
                _btnColor.BackColor = LayerColor;
            }
        };
        layout.Controls.Add(_btnColor, 1, 1);

        // Mode
        layout.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _cmbMode = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbMode.DataSource = Enum.GetValues(typeof(LayerMode));
        _cmbMode.SelectedItem = LayerMode;
        layout.Controls.Add(_cmbMode, 1, 2);

        // Speed
        layout.Controls.Add(new Label { Text = "Speed (mm/min):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        _numSpeed = new NumericUpDown { Minimum = 1, Maximum = 20000, Value = (decimal)LayerSpeed, Width = 100 };
        layout.Controls.Add(_numSpeed, 1, 3);

        // Power
        layout.Controls.Add(new Label { Text = "Power (%):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        _numPower = new NumericUpDown { Minimum = 0, Maximum = 100, Value = (decimal)LayerPower, Width = 100 };
        layout.Controls.Add(_numPower, 1, 4);

        // Buttons
        var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 40 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK };
        
        btnOK.Click += (s, e) => 
        {
            LayerName = _txtName.Text;
            LayerSpeed = (float)_numSpeed.Value;
            LayerPower = (float)_numPower.Value;
            LayerMode = (LayerMode)_cmbMode.SelectedItem;
        };

        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOK);

        this.Controls.Add(layout);
        this.Controls.Add(btnPanel);
        
        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;
    }
}
