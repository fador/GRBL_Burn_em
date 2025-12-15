using laser_gui_test.Data;

namespace laser_gui_test.Tools;

public enum ToolType
{
    Select,
    DrawLine,
    DrawBox,
    Ruler,
    Text,
    DrawCircle,
    DrawBezier
}

public class ToolManager
{
    private static ToolManager? _instance;
    public static ToolManager Instance => _instance ??= new ToolManager();

    public ToolType CurrentTool { get; set; } = ToolType.Select;

    public event EventHandler<ToolType>? ToolChanged;

    public void SetTool(ToolType tool)
    {
        CurrentTool = tool;
        ToolChanged?.Invoke(this, tool);
    }
}
