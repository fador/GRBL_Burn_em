using System.Collections.Generic;
using grbl_burn_em.Data;

namespace grbl_burn_em.Data.Commands;

public class RemoveObjectCommand : ICommand
{
    private readonly List<LaserObject> _objects;
    
    public string Description => $"Remove {_objects.Count} objects";

    public RemoveObjectCommand(LaserObject obj)
    {
        _objects = new List<LaserObject> { obj };
    }
    
    public RemoveObjectCommand(IEnumerable<LaserObject> objects)
    {
        _objects = new List<LaserObject>(objects);
    }

    public void Execute()
    {
        foreach(var obj in _objects)
        {
            ProjectState.Instance.RemoveObject(obj);
        }
    }

    public void Undo()
    {
        foreach(var obj in _objects)
        {
            ProjectState.Instance.AddObject(obj);
        }
    }
}
