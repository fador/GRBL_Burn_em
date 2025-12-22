using grbl_burn_em.Data;

namespace grbl_burn_em.Tools;

public enum ToolType
{
    Select,
    DrawLine,
    DrawBox,
    Ruler,
    Text,
    DrawCircle,
    DrawBezier,
    Rotate,
    ClickToMove
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
