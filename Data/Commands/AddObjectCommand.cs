using System.Collections.Generic;
using laser_gui_test.Data;

namespace laser_gui_test.Data.Commands;

public class AddObjectCommand : ICommand
{
    private readonly List<LaserObject> _objects;
    
    public string Description => $"Add {_objects.Count} objects";

    public AddObjectCommand(LaserObject obj)
    {
        _objects = new List<LaserObject> { obj };
    }
    
    public AddObjectCommand(IEnumerable<LaserObject> objects)
    {
        _objects = new List<LaserObject>(objects);
    }

    public void Execute()
    {
        foreach(var obj in _objects)
        {
            ProjectState.Instance.AddObject(obj);
        }
    }

    public void Undo()
    {
        foreach(var obj in _objects)
        {
            ProjectState.Instance.RemoveObject(obj);
        }
    }
}
