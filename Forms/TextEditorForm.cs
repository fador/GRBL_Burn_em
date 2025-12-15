using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;

namespace laser_gui_test.Forms
{
    public class TextEditorForm : Form
    {
        public string TextValue { get; private set; } = "";
        public string FontName { get; private set; } = "Arial";
        public float FontSize { get; private set; }

        private TextBox _textBox = null!;
        private ComboBox _fontComboBox = null!;
        private NumericUpDown _fontSizeNumeric = null!;
        private Button _okButton = null!;
        private Button _cancelButton = null!;

        public TextEditorForm(string text, string fontName, float fontSize)
        {
            Text = "Edit Text";
            Size = new Size(400, 250);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            InitializeControls(text, fontName, fontSize);
        }

        private void InitializeControls(string text, string fontName, float fontSize)
        {
            var labelText = new Label { Text = "Text:", Location = new Point(10, 10), AutoSize = true };
            _textBox = new TextBox { Text = text, Location = new Point(10, 30), Width = 360, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            var labelFont = new Label { Text = "Font:", Location = new Point(10, 70), AutoSize = true };
            _fontComboBox = new ComboBox { Location = new Point(10, 90), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            
            // Populate Fonts
            foreach (var family in FontFamily.Families)
            {
                _fontComboBox.Items.Add(family.Name);
            }
            
            if (_fontComboBox.Items.Contains(fontName))
            {
                _fontComboBox.SelectedItem = fontName;
            }
            else if (_fontComboBox.Items.Count > 0)
            {
                _fontComboBox.SelectedIndex = 0;
            }

            var labelSize = new Label { Text = "Size (pt):", Location = new Point(230, 70), AutoSize = true };
            _fontSizeNumeric = new NumericUpDown { Location = new Point(230, 90), Width = 100, Minimum = 1, Maximum = 1000, Value = (decimal)fontSize, DecimalPlaces = 1 };

            _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(210, 160), Width = 80 };
            _cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(300, 160), Width = 80 };

            _okButton.Click += (s, e) =>
            {
                TextValue = _textBox.Text;
                FontName = _fontComboBox.SelectedItem?.ToString() ?? "Arial";
                FontSize = (float)_fontSizeNumeric.Value;
                Close();
            };

            Controls.AddRange(new Control[] { labelText, _textBox, labelFont, _fontComboBox, labelSize, _fontSizeNumeric, _okButton, _cancelButton });
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }
    }
}
