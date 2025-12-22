using System.Collections.Generic;
using System.Drawing;
using grbl_burn_em.Data;

namespace grbl_burn_em.Data.Commands;

public class MoveCommand : ICommand
{
    private readonly List<LaserObject> _objects;
    private readonly float _dx;
    private readonly float _dy;

    public string Description => $"Move {_objects.Count} objects";

    public MoveCommand(List<LaserObject> objects, float dx, float dy)
    {
        _objects = new List<LaserObject>(objects);
        _dx = dx;
        _dy = dy;
    }

    // Since we create this command AFTER the move has finished (Interact End), 
    // Execute doesn't need to do anything if we push it then.
    // BUT standard pattern Execute() performs the action.
    // If we want to record an action that JUST happened, we assume it's "Done".
    // Alternatively, we capture State Before and State After.
    // Let's stick to "Execute applies the change".
    // So if we create this command, we might NOT call Execute immediately if we manually did it.
    // Better: We capture the move, undo reverses it, execute re-applies it.
    
    public void Execute()
    {
        Move(_dx, _dy);
    }

    public void Undo()
    {
        Move(-_dx, -_dy);
    }
    
    private void Move(float x, float y)
    {
        foreach (var obj in _objects)
        {
            MoveObject(obj, x, y);
        }
    }
    
    private void MoveObject(LaserObject obj, float x, float y)
    {
        if (obj is LaserPath path)
        {
            // Move points
            for(int i=0; i<path.Points.Count; i++)
            {
                path.Points[i] = new PointF(path.Points[i].X + x, path.Points[i].Y + y);
            }
            path.UpdateBounds();
        }
        else if (obj is LaserBezier bezier)
        {
            // Move points
            for(int i=0; i<bezier.Points.Count; i++)
            {
                bezier.Points[i] = new PointF(bezier.Points[i].X + x, bezier.Points[i].Y + y);
            }
            bezier.UpdateBounds();
        }
        else if (obj is LaserGroup group)
        {
            foreach(var child in group.Children)
            {
                MoveObject(child, x, y);
            }
        }
        else
        {
             obj.Position = new PointF(obj.Position.X + x, obj.Position.Y + y);
        }
    }
}
