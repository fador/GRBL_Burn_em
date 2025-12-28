using grbl_burn_em.Data;
using grbl_burn_em.Forms;

namespace grbl_burn_em;

public partial class MainForm
{
    private void InitializeLayers()
    {
        _layerPanel.Controls.Clear();
        
        // Add "New Layer" Button
        var btnAdd = new Button
        {
            Text = "+",
            Size = new Size(30, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White
        };
        btnAdd.Click += (s, e) => 
        {
             // Create new layer
             // We need a random color
             var rnd = new Random();
             var color = Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256));
             var newLayer = new Layer($"Layer {ProjectState.Instance.Layers.Count}", color);
             ProjectState.Instance.Layers.Add(newLayer);
             InitializeLayers(); // Refresh
        };
        _layerPanel.Controls.Add(btnAdd);

        foreach (var layer in ProjectState.Instance.Layers)
        {
            var btn = new Button
            {
                BackColor = layer.Color,
                Size = new Size(30, 30),
                FlatStyle = FlatStyle.Flat,
                Tag = layer
            };
            
            // Tooltip for Layer Info
            var tt = new ToolTip();
            tt.SetToolTip(btn, $"{layer.Name}\nS:{layer.Speed} P:{layer.Power}% ({layer.Mode})");

            btn.MouseUp += (s, e) => 
            {
                if (e.Button == MouseButtons.Left)
                {
                     // Assign to selected objects if any
                     var sel = ProjectState.Instance.SelectedObjects;
                     if (sel.Count > 0)
                     {
                         foreach(var obj in sel) obj.LayerId = layer.Id;
                         _objectList.Refresh();
                         _workbench.Invalidate();
                         UpdateSelectedObjects(); // Update props panel
                     }
                     
                     ProjectState.Instance.ActiveLayer = layer;
                     if (s is Button b) UpdateLayerButtons(b);
                }
            };
            
            btn.DoubleClick += (s, e) => 
            {
                 using var dlg = new LayerSettingsForm(layer);
                 if (dlg.ShowDialog() == DialogResult.OK)
                 {
                     layer.Name = dlg.LayerName;
                     layer.Color = dlg.LayerColor;
                     layer.Speed = dlg.LayerSpeed;
                     layer.Power = dlg.LayerPower;
                     layer.Mode = dlg.LayerMode;
                     
                     btn.BackColor = layer.Color;
                     tt.SetToolTip(btn, $"{layer.Name}\nS:{layer.Speed} P:{layer.Power}% ({layer.Mode})");
                     _workbench.Invalidate();
                     UpdateSelectedObjects();
                 }
            };

            _layerPanel.Controls.Add(btn);

            if (ProjectState.Instance.ActiveLayer == layer)
            {
                UpdateLayerButtons(btn);
            }
        }
    }

    private void UpdateLayerButtons(Button activeBtn)
    {
        foreach(Control c in _layerPanel.Controls)
        {
            if (c is Button b)
            {
                if (c == activeBtn)
                {
                    b.FlatAppearance.BorderColor = Color.White;
                    b.FlatAppearance.BorderSize = 3;
                }
                else
                {
                    b.FlatAppearance.BorderColor = Color.Black; // Default
                    b.FlatAppearance.BorderSize = 1;
                }
            }
        }
    }
}
