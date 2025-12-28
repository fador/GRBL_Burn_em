using grbl_burn_em.Tools;
using System.Reflection;

namespace grbl_burn_em;

public partial class MainForm
{
    private void InitializeTools()
    {
        var toolMap = new Dictionary<string, (ToolType Type, string Icon)>
        {
            { "Select", (ToolType.Select, "tool_select.png") },
            { "Line", (ToolType.DrawLine, "tool_line.png") },
            { "Box", (ToolType.DrawBox, "tool_box.png") },
            { "Circle", (ToolType.DrawCircle, "tool_circle.png") },
            { "Bezier", (ToolType.DrawBezier, "tool_bezier.png") },
            { "Text", (ToolType.Text, "tool_text.png") },
            { "Rotate", (ToolType.Rotate, "tool_rotate.png") },
            { "Ruler", (ToolType.Ruler, "tool_ruler.png") },
            { "Move Laser", (ToolType.ClickToMove, "tool_move.png") }
        };

        foreach (var kvp in toolMap)
        {
            var btn = new Button
            {
                Size = new Size(50, 50),
                Margin = new Padding(2),
                Tag = kvp.Value.Type,
                BackgroundImageLayout = ImageLayout.Zoom
            };

            // Load from Embedded Resource
            string resourceName = $"grbl_burn_em.Icons.{kvp.Value.Icon}";
            bool iconLoaded = false;
            
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var originalImage = Image.FromStream(stream))
                        {
                           btn.BackgroundImage = ResizeImage(originalImage, 40, 40);
                        }
                        iconLoaded = true;
                    }
                }
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load icon {resourceName}: {ex.Message}");
            }

            if (!iconLoaded)
            {
                btn.Text = kvp.Key;
            }
            
            _toolTip.SetToolTip(btn, kvp.Key);

            btn.Click += (s, e) => 
            {
                ToolManager.Instance.SetTool((ToolType)btn.Tag);
                // Visual feedback (simple)
                foreach(Control c in _toolsPanel.Controls) c.BackColor = Color.FromName("Control");
                btn.BackColor = Color.LightBlue;
            };

            _toolsPanel.Controls.Add(btn);
        }
    }
}
