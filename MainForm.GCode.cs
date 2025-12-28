using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;
using grbl_burn_em.Forms;

namespace grbl_burn_em;

public partial class MainForm
{
    private void GenerateGCode()
    {
        if (!CheckSafetyBounds(ProjectState.Instance.Objects.ToList())) return;

        string generatorName = AppConfiguration.Instance.GCodeGenerator;

        IGCodeGenerator? generator = null;

        if (generatorName == "Grbl") generator = new GrblGenerator();
        else if (generatorName == "Marlin") generator = new MarlinGenerator();
        // Add others here


        if (generator == null)
        {
             // Check plugins
             generator = _gcodeGenerators.FirstOrDefault(g => g.Name == generatorName);
        }

        if (generator == null)
        {
             // Default
             generator = new GrblGenerator();
        }

        try
        {
            var lines = generator.Generate(ProjectState.Instance.Objects);
            var gcode = string.Join(Environment.NewLine, lines);
            
            using var dlg = new DebugCodeForm(gcode);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Generation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowPreview()
    {
        if (!CheckSafetyBounds(ProjectState.Instance.Objects.ToList())) return;

        string generatorName = AppConfiguration.Instance.GCodeGenerator;
        
        IGCodeGenerator? generator = null;

        if (generatorName == "Grbl") generator = new GrblGenerator();
        else if (generatorName == "Marlin") generator = new MarlinGenerator();
        // Add others here


        if (generator == null)
        {
             // Check plugins
             generator = _gcodeGenerators.FirstOrDefault(g => g.Name == generatorName);
        }

        if (generator == null)
        {
             // Default
             generator = new GrblGenerator();
        }

        try
        {
            var lines = generator.Generate(ProjectState.Instance.Objects);
            var gcode = string.Join(Environment.NewLine, lines);
            
            using var dlg = new PreviewForm(gcode);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Preview generation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool CheckSafetyBounds(IEnumerable<LaserObject> objects)
    {
        if (!AppConfiguration.Instance.EnableSafetyBoundsCheck) return true;

        var enabledObjects = objects.Where(o => o.IsEnabled).ToList();
        if (!enabledObjects.Any()) return true;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var obj in enabledObjects)
        {
            var b = obj.GetBounds();
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
        }

        float workW = AppConfiguration.Instance.WorkAreaWidth;
        float workH = AppConfiguration.Instance.WorkAreaHeight;

        bool outOfBounds = minX < 0 || minY < 0 || maxX > workW || maxY > workH;

        if (outOfBounds)
        {
            string msg = $"Warning: The job boundbox ({minX:F1}, {minY:F1}) to ({maxX:F1}, {maxY:F1}) exceeds the machine bed limits (0, 0) to ({workW:F1}, {workH:F1}).\n\nDo you want to continue anyway?";
            var result = MessageBox.Show(msg, "Safety Boundary Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            return result == DialogResult.Yes;
        }

        return true;
    }
}
