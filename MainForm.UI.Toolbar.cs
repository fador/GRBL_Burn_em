using grbl_burn_em.Data;
using grbl_burn_em.Data.Commands;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace grbl_burn_em;

public partial class MainForm
{
    private void InitializeTopToolbar()
    {
        _topToolbarPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Color.FromName("Control")
        };
        
        // Row 1: Mouse Position
        var tsRow1 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _lblMousePos = new ToolStripLabel("Mouse: 0.00, 0.00");
        tsRow1.Items.Add(_lblMousePos);
        
        // Row 2: Properties
        var tsRow2 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        
        tsRow2.Items.Add(new ToolStripLabel("X:"));
        _nudPosX = new NumericUpDown { Width = 60, DecimalPlaces = 2, Minimum = -100000, Maximum = 100000, Increment = 10 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudPosX));
        
        tsRow2.Items.Add(new ToolStripLabel("Y:"));
        _nudPosY = new NumericUpDown { Width = 60, DecimalPlaces = 2, Minimum = -100000, Maximum = 100000, Increment = 10 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudPosY));
        
        tsRow2.Items.Add(new ToolStripLabel("W:"));
        _nudSizeW = new NumericUpDown { Width = 60, DecimalPlaces = 2, Minimum = 0, Maximum = 100000, Increment = 10 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudSizeW));
        
        tsRow2.Items.Add(new ToolStripLabel("H:"));
        _nudSizeH = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 100000, Width = 60 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudSizeH));

        tsRow2.Items.Add(new ToolStripSeparator());
        tsRow2.Items.Add(new ToolStripLabel("R:"));
        _nudRotation = new NumericUpDown { DecimalPlaces = 1, Minimum = -3600, Maximum = 3600, Width = 60 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudRotation));
        
        _lblLayerInfo = new ToolStripLabel("-");
        tsRow2.Items.Add(_lblLayerInfo);
        
        _topToolbarPanel.Controls.Add(tsRow1);
        _topToolbarPanel.Controls.Add(tsRow2);
        
        // Row 3: Text Controls
        var tsRow3 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        
        tsRow3.Items.Add(new ToolStripLabel("Text:"));
        _txtContent = new ToolStripTextBox { Width = 150 };
        tsRow3.Items.Add(_txtContent);
        
        tsRow3.Items.Add(new ToolStripLabel("Font:"));
        _cmbFont = new ToolStripComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var family in FontFamily.Families)
        {
            _cmbFont.Items.Add(family.Name);
        }
        tsRow3.Items.Add(_cmbFont);
        
        tsRow3.Items.Add(new ToolStripLabel("Size:"));
        _nudFontSize = new NumericUpDown { Width = 60, Minimum = 0.1m, Maximum = 10000, DecimalPlaces = 2 };
        tsRow3.Items.Add(new ToolStripControlHost(_nudFontSize));
        
        _btnBold = new ToolStripButton ("B") { CheckOnClick = true, Font = new Font(this.Font, FontStyle.Bold) };
        tsRow3.Items.Add(_btnBold);
        
        _btnItalic = new ToolStripButton ("I") { CheckOnClick = true, Font = new Font(this.Font, FontStyle.Italic) };
        tsRow3.Items.Add(_btnItalic);
        
        // Row 4: Path Controls [NEW]
        var tsRow4 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        
        tsRow4.Items.Add(new ToolStripLabel("Path Pos:"));
        _trkPathOffset = new TrackBar { Width = 200, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Height = 20 };
        tsRow4.Items.Add(new ToolStripControlHost(_trkPathOffset));
        
        tsRow4.Items.Add(new ToolStripLabel("V-Offset:"));
        _nudVerticalOffset = new NumericUpDown { Width = 60, DecimalPlaces = 1, Minimum = -10000, Maximum = 10000, Increment = 0.5m };
        tsRow4.Items.Add(new ToolStripControlHost(_nudVerticalOffset));

        _chkReversePath = new CheckBox { Text = "Reverse", AutoSize = true };
        tsRow4.Items.Add(new ToolStripControlHost(_chkReversePath));

        _chkUpsideDown = new CheckBox { Text = "Flip", AutoSize = true };
        tsRow4.Items.Add(new ToolStripControlHost(_chkUpsideDown));
        
        tsRow4.Items.Add(new ToolStripLabel("Method:"));
        _cmbWarpMethod = new ToolStripComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbWarpMethod.Items.AddRange(new object[] { "Stretch", "Align" });
        tsRow4.Items.Add(_cmbWarpMethod);
        
        _topToolbarPanel.Controls.Add(tsRow4);
        
        this.Controls.Add(_topToolbarPanel); 
        
        // Wire Properties Logic
        EventHandler valChanged = (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1)
            {
                var obj = sel[0];
                
                float nx = (float)_nudPosX.Value;
                float ny = (float)_nudPosY.Value;
                float nw = (float)_nudSizeW.Value;
                float nh = (float)_nudSizeH.Value;
                float nRot = (float)_nudRotation.Value; 
                
                // Position Change
                if(Math.Abs(obj.Position.X - nx) > 0.01 || Math.Abs(obj.Position.Y - ny) > 0.01)
                {
                     float dx = nx - obj.Position.X;
                     float dy = ny - obj.Position.Y;
                     CommandManager.Instance.Execute(new MoveCommand(sel, dx, dy));
                     _workbench.Invalidate();
                }
                
                // Size Change
                if(Math.Abs(obj.Size.Width - nw) > 0.01 || Math.Abs(obj.Size.Height - nh) > 0.01)
                {
                    obj.Size = new SizeF(nw, nh);
                    _workbench.Invalidate();
                }

                // Rotation Change
                if (Math.Abs(obj.Rotation - nRot) > 0.01)
                {
                    obj.Rotation = nRot;
                    _workbench.Invalidate();
                }
            }
        };
        
        _nudPosX.ValueChanged += valChanged;
        _nudPosY.ValueChanged += valChanged;
        _nudSizeW.ValueChanged += valChanged;
        _nudSizeH.ValueChanged += valChanged;
        _nudRotation.ValueChanged += valChanged;

        // Wire Text Logic
        EventHandler textChanged = (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                txt.Text = _txtContent.Text;
                if (_cmbFont.SelectedItem != null) txt.FontName = _cmbFont.SelectedItem?.ToString() ?? "Arial";
                txt.FontSize = (float)_nudFontSize.Value;
                
                // Recalc Size
                if (txt.PathId != Guid.Empty)
                {
                    txt.UpdateWarpedBounds();
                }
                else
                {
                    txt.UpdateTextSize();
                }
                
                _workbench.Invalidate();
            }
        };

        _txtContent.TextChanged += textChanged;
        _cmbFont.SelectedIndexChanged += textChanged;
        _nudFontSize.ValueChanged += textChanged;

        _btnBold.Click += (s, e) => 
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                if (_btnBold.Checked) txt.FontStyle |= FontStyle.Bold;
                else txt.FontStyle &= ~FontStyle.Bold;
                txt.UpdateWarpedBounds();
                _workbench.Invalidate();
            }
        };

        _btnItalic.Click += (s, e) => 
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                if (_btnItalic.Checked) txt.FontStyle |= FontStyle.Italic;
                else txt.FontStyle &= ~FontStyle.Italic;
                txt.UpdateWarpedBounds();
                _workbench.Invalidate();
            }
        };

        // Wire Path Logic
        _trkPathOffset.Scroll += (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                txt.PathOffset = _trkPathOffset.Value / 10f; // Multiplier for precision
                txt.UpdateWarpedBounds();
                _workbench.Invalidate();
            }
        };

        _nudVerticalOffset.ValueChanged += (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                txt.VerticalOffset = (float)_nudVerticalOffset.Value;
                txt.UpdateWarpedBounds();
                _workbench.Invalidate();
            }
        };

        _chkReversePath.CheckedChanged += (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                txt.ReversePath = _chkReversePath.Checked;
                txt.UpdateWarpedBounds();
                _workbench.Invalidate();
            }
        };

        _chkUpsideDown.CheckedChanged += (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                txt.UpsideDown = _chkUpsideDown.Checked;
                txt.UpdateWarpedBounds();
                _workbench.Invalidate();
            }
        };

        _cmbWarpMethod.SelectedIndexChanged += (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                txt.WarpMethod = (TextWarpMethod)_cmbWarpMethod.SelectedIndex;
                txt.UpdateWarpedBounds();
                _workbench.Invalidate();
                UpdateSelectedObjects(); // Bounds change
            }
        };
    }
}
