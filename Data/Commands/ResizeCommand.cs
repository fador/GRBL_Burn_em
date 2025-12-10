using System.Collections.Generic;
using System.Drawing;
using laser_gui_test.Data;

namespace laser_gui_test.Data.Commands;

public class ResizeCommand : ICommand
{
    private class ObjectState
    {
        public PointF Position;
        public SizeF Size;
        public List<PointF>? Points; // For paths
    }

    private readonly Dictionary<LaserObject, ObjectState> _oldStates = new();
    private readonly Dictionary<LaserObject, ObjectState> _newStates = new();
    
    public string Description => "Resize objects";

    public ResizeCommand(Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points)> oldStates,
                         Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points)> newStates)
    {
        foreach(var kvp in oldStates)
        {
            _oldStates[kvp.Key] = new ObjectState 
            { 
                Position = kvp.Value.Pos, 
                Size = kvp.Value.Size,
                Points = kvp.Value.Points != null ? new List<PointF>(kvp.Value.Points) : null
            };
        }
        
        foreach(var kvp in newStates)
        {
            _newStates[kvp.Key] = new ObjectState 
            { 
                Position = kvp.Value.Pos, 
                Size = kvp.Value.Size,
                Points = kvp.Value.Points != null ? new List<PointF>(kvp.Value.Points) : null
            };
        }
    }

    public void Execute()
    {
        ApplyStates(_newStates);
    }

    public void Undo()
    {
        ApplyStates(_oldStates);
    }
    
    private void ApplyStates(Dictionary<LaserObject, ObjectState> states)
    {
        foreach(var kvp in states)
        {
            var obj = kvp.Key;
            var state = kvp.Value;
            
            obj.Position = state.Position;
            obj.Size = state.Size;
            
            if (obj is LaserPath path && state.Points != null)
            {
                path.Points = new List<PointF>(state.Points);
            }
        }
    }
}
