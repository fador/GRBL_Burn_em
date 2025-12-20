using System;
using System.Drawing;
using System.Windows.Forms;
using laser_gui_test.Data;

namespace laser_gui_test.Forms
{
    public class ScaleLayerForm : Form
    {
        private RadioButton _rbScalePower;
        private RadioButton _rbScaleSpeed;
        private NumericUpDown _nudNewValue;
        private Label _lblCurrentPower;
        private Label _lblCurrentSpeed;
        private Button _btnOk;
        private Button _btnCancel;

        public Layer TargetLayer { get; private set; }
        public bool ScaleByPower => _rbScalePower.Checked;
        public float ResultValue => (float)_nudNewValue.Value;

        public ScaleLayerForm(Layer layer)
        {
            TargetLayer = layer;
            InitializeComponent();
            UpdateLabels();
        }

        private void InitializeComponent()
        {
            this.Text = "Scale Layer Output";
            this.Size = new Size(350, 250);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                RowCount = 5,
                ColumnCount = 2,
                AutoSize = true
            };

            // Current Values
            mainLayout.Controls.Add(new Label { Text = "Current Power:", AutoSize = true, Font = new Font(this.Font, FontStyle.Bold) }, 0, 0);
            _lblCurrentPower = new Label { Text = $"{TargetLayer.Power:F1}%", AutoSize = true };
            mainLayout.Controls.Add(_lblCurrentPower, 1, 0);

            mainLayout.Controls.Add(new Label { Text = "Current Speed:", AutoSize = true, Font = new Font(this.Font, FontStyle.Bold) }, 0, 1);
            _lblCurrentSpeed = new Label { Text = $"{TargetLayer.Speed:F0} mm/min", AutoSize = true };
            mainLayout.Controls.Add(_lblCurrentSpeed, 1, 1);

            // Radio Buttons
            var panelRadios = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
            _rbScalePower = new RadioButton { Text = "Scale to Power", Checked = true, AutoSize = true };
            _rbScaleSpeed = new RadioButton { Text = "Scale to Speed", AutoSize = true };
            
            _rbScalePower.CheckedChanged += OptionChanged;
            _rbScaleSpeed.CheckedChanged += OptionChanged;

            panelRadios.Controls.Add(_rbScalePower);
            panelRadios.Controls.Add(_rbScaleSpeed);
            
            mainLayout.Controls.Add(panelRadios, 0, 2);
            mainLayout.SetColumnSpan(panelRadios, 2);

            // New Value Input
            var panelInput = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            panelInput.Controls.Add(new Label { Text = "New Value:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right });
            
            _nudNewValue = new NumericUpDown 
            { 
                DecimalPlaces = 1, 
                Width = 100, 
                Minimum = 0, 
                Maximum = 100000 
            };
            panelInput.Controls.Add(_nudNewValue);
            
            mainLayout.Controls.Add(panelInput, 0, 3);
            mainLayout.SetColumnSpan(panelInput, 2);

            // Buttons
            var panelButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true };
            _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            _btnOk = new Button { Text = "Scale", DialogResult = DialogResult.OK };
            
            panelButtons.Controls.Add(_btnCancel);
            panelButtons.Controls.Add(_btnOk);
            
            mainLayout.Controls.Add(panelButtons, 0, 4);
            mainLayout.SetColumnSpan(panelButtons, 2);

            this.Controls.Add(mainLayout);
            this.AcceptButton = _btnOk;
            this.CancelButton = _btnCancel;

            OptionChanged(null, null); // Set initial state
        }

        private void OptionChanged(object? sender, EventArgs? e)
        {
            if (_rbScalePower.Checked)
            {
                _nudNewValue.Maximum = 100; // Power %
                _nudNewValue.Value = (decimal)TargetLayer.Power;
            }
            else
            {
                _nudNewValue.Maximum = 100000; // Speed msg/min
                _nudNewValue.Value = (decimal)TargetLayer.Speed;
            }
        }

        private void UpdateLabels()
        {
            _lblCurrentPower.Text = $"{TargetLayer.Power:F1}%";
            _lblCurrentSpeed.Text = $"{TargetLayer.Speed:F0} mm/min";
        }
    }
}
