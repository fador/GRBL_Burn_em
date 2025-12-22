using System.Collections.Generic;
using System.Drawing;
using grbl_burn_em.Data;

namespace grbl_burn_em.Data.Commands;

public class ResizeCommand : ICommand
{
    private class ObjectState
    {
        public PointF Position;
        public SizeF Size;
        public List<PointF>? Points; // For paths
        public float FontSize; // For text
        public float Rotation;
    }

    private readonly Dictionary<LaserObject, ObjectState> _oldStates = new();
    private readonly Dictionary<LaserObject, ObjectState> _newStates = new();
    
    public string Description => "Resize objects";

    public ResizeCommand(Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)> oldStates,
                         Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)> newStates)
    {
        foreach(var kvp in oldStates)
        {
            _oldStates[kvp.Key] = new ObjectState 
            { 
                Position = kvp.Value.Pos, 
                Size = kvp.Value.Size,
                Points = kvp.Value.Points != null ? new List<PointF>(kvp.Value.Points) : null,
                FontSize = kvp.Value.FontSize,
                Rotation = kvp.Value.Rotation
            };
        }
        
        foreach(var kvp in newStates)
        {
            _newStates[kvp.Key] = new ObjectState 
            { 
                Position = kvp.Value.Pos, 
                Size = kvp.Value.Size,
                Points = kvp.Value.Points != null ? new List<PointF>(kvp.Value.Points) : null,
                FontSize = kvp.Value.FontSize,
                Rotation = kvp.Value.Rotation
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
            obj.Rotation = state.Rotation;
            
            if (obj is LaserPath path && state.Points != null)
            {
                path.Points = new List<PointF>(state.Points);
            } else if (obj is LaserBezier bez && state.Points != null)
            {
                 bez.Points = new List<PointF>(state.Points);
                 bez.UpdateBounds();
            }
            
            if (obj is LaserText txt && state.FontSize > 0)
            {
                txt.FontSize = state.FontSize;
            }
        }
    }
}
