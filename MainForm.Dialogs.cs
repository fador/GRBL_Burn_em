using grbl_burn_em.Data;
using grbl_burn_em.Forms;
using grbl_burn_em.Data.Commands;

namespace grbl_burn_em;

public partial class MainForm
{
    public void EditText(LaserText? textObj = null)
    {
        if (textObj == null)
        {
            var sel = ProjectState.Instance.SelectedObjects;
            textObj = sel.OfType<LaserText>().FirstOrDefault();
        }

        if (textObj != null)
        {
            using (var form = new TextEditorForm(textObj.Text, textObj.FontName, textObj.FontSize, textObj.FontStyle))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    textObj.Text = form.TextValue;
                    textObj.FontName = form.FontName;
                    textObj.FontSize = form.FontSize;
                    textObj.FontStyle = form.FontStyle;

                    // Recalc Size
                    if (textObj.PathId != Guid.Empty)
                    {
                        textObj.UpdateWarpedBounds();
                    }
                    else
                    {
                        textObj.UpdateTextSize();
                    }

                    _workbench.Invalidate();
                    UpdateSelectedObjects();
                }
            }
        }
    }

    public void ShowArrayModifierDialog()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        if (sel.Count == 0) return;
        
        using var dlg = new GridArrayForm();
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var cmd = new CloneArrayCommand(sel, dlg.Parameters);
            CommandManager.Instance.Execute(cmd);
            if (_workbench != null) _workbench.Invalidate();
        }
    }

    public void ShowScaleLayerDialog()
    {
        var layer = ProjectState.Instance.ActiveLayer;
        if (layer == null)
        {
            MessageBox.Show("Please select a layer first.", "No Layer Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new ScaleLayerForm(ProjectState.Instance.Layers.ToList(), layer);
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var targetLayer = dlg.TargetLayer;
            if (dlg.ScaleByPower)
            {
                targetLayer.ScaleToPower(dlg.ResultValue);
            }
            else
            {
                targetLayer.ScaleToSpeed(dlg.ResultValue);
            }
            
            // Refresh UI
            InitializeLayers(); 
            _layerList.Refresh();
            _workbench.Invalidate();
            UpdateSelectedObjects();
        }
    }

    public void ShowMathShapeDialog()
    {
        using var dlg = new MathShapeForm();
        if (dlg.ShowDialog() == DialogResult.OK && dlg.ResultPath != null)
        {
            var lp = dlg.ResultPath;
            if (ProjectState.Instance.ActiveLayer != null)
            {
                lp.LayerId = ProjectState.Instance.ActiveLayer.Id;
            }
            
            var cmd = new AddObjectCommand(lp);
            CommandManager.Instance.Execute(cmd);
            _workbench.Invalidate();
        }
    }

    public void ShowPowerSpeedCalibrationDialog()
    {
        using var dlg = new PowerSpeedCalibrationForm();
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            var objects = dlg.Generator.Generate();
            if (objects.Count > 0)
            {
                var cmd = new AddObjectCommand(objects);
                CommandManager.Instance.Execute(cmd);
                ProjectState.Instance.SelectedObjects = objects;
                _workbench.Invalidate();
            }
        }
    }
}
