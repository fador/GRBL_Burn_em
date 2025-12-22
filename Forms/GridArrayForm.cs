namespace grbl_burn_em.Forms;

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

public class GridArrayForm : Form
{
    public int Rows { get; private set; }
    public int Cols { get; private set; }
    public float GapX { get; private set; }
    public float GapY { get; private set; }

    private NumericUpDown _numRows = null!;
    private NumericUpDown _numCols = null!;
    private NumericUpDown _numGapX = null!;
    private NumericUpDown _numGapY = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    public GridArrayForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "Create Array";
        this.Size = new Size(300, 250);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            RowCount = 5,
            ColumnCount = 2
        };

        // Columns
        layout.Controls.Add(new Label { Text = "Columns (X):", AutoSize = true }, 0, 0);
        _numCols = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 2 };
        layout.Controls.Add(_numCols, 1, 0);

        // Rows
        layout.Controls.Add(new Label { Text = "Rows (Y):", AutoSize = true }, 0, 1);
        _numRows = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 1 };
        layout.Controls.Add(_numRows, 1, 1);

        // Gap X
        layout.Controls.Add(new Label { Text = "Gap X (mm):", AutoSize = true }, 0, 2);
        _numGapX = new NumericUpDown { Minimum = -1000, Maximum = 1000, Value = 5, DecimalPlaces = 2 };
        layout.Controls.Add(_numGapX, 1, 2);

        // Gap Y
        layout.Controls.Add(new Label { Text = "Gap Y (mm):", AutoSize = true }, 0, 3);
        _numGapY = new NumericUpDown { Minimum = -1000, Maximum = 1000, Value = 5, DecimalPlaces = 2 };
        layout.Controls.Add(_numGapY, 1, 3);

        // Buttons
        var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Height = 40 };
        _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        _btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
        
        _btnOk.Click += (s, e) =>
        {
            Rows = (int)_numRows.Value;
            Cols = (int)_numCols.Value;
            GapX = (float)_numGapX.Value;
            GapY = (float)_numGapY.Value;
            this.Close();
        };

        btnPanel.Controls.Add(_btnCancel);
        btnPanel.Controls.Add(_btnOk);

        layout.Controls.Add(btnPanel, 0, 4);
        layout.SetColumnSpan(btnPanel, 2);

        this.Controls.Add(layout);
        this.AcceptButton = _btnOk;
        this.CancelButton = _btnCancel;
    }
}
