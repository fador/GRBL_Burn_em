using System.Collections.Generic;
using System.Linq;

namespace grbl_burn_em.Data.Commands;

public class GroupCommand : ICommand
{
    private readonly List<LaserObject> _objectsToGroup;
    private readonly LaserGroup _group;
    
    public string Description => "Group objects";

    public GroupCommand(IEnumerable<LaserObject> objects)
    {
        _objectsToGroup = objects.ToList();
        _group = new LaserGroup();
        _group.Children.AddRange(_objectsToGroup);
        // Calculate Bounding Box? 
        // Group name?
        _group.Name = "Group " + _group.Id.ToString().Substring(0, 4);
    }

    public void Execute()
    {
        // Remove individual objects from Project
        foreach(var obj in _objectsToGroup)
        {
            ProjectState.Instance.RemoveObject(obj);
            obj.Parent = _group;
        }
        
        // Add Group
        ProjectState.Instance.AddObject(_group);
        
        // Select the group
        ProjectState.Instance.SelectedObjects = new List<LaserObject> { _group };
    }

    public void Undo()
    {
        // Remove Group
        ProjectState.Instance.RemoveObject(_group);
        
        // Restore objects
        foreach(var obj in _objectsToGroup)
        {
            obj.Parent = null;
            ProjectState.Instance.AddObject(obj);
        }
        
        // Restore selection
        ProjectState.Instance.SelectedObjects = new List<LaserObject>(_objectsToGroup);
    }
}
