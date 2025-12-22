using System.Drawing;

namespace grbl_burn_em.Data.GCode;

public enum CommandType
{
    Other,
    Travel, // G0
    Cut     // G1
}

public class GCodeCommand
{
    public string OriginalCommand { get; set; } = "";
    public CommandType Type { get; set; } = CommandType.Other;
    public PointF Start { get; set; }
    public PointF End { get; set; }
    public float Power { get; set; } // 0-1000 usually
    public float Speed { get; set; }
    public int LineIndex { get; set; }

    public override string ToString()
    {
        return OriginalCommand;
    }
}
