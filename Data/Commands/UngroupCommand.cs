using System.Collections.Generic;
using System.Linq;

namespace grbl_burn_em.Data.Commands;

public class UngroupCommand : ICommand
{
    private readonly List<LaserGroup> _groupsToUngroup;
    private readonly Dictionary<LaserGroup, List<LaserObject>> _groupChildren = new();
    
    public string Description => "Ungroup objects";

    public UngroupCommand(IEnumerable<LaserObject> objects)
    {
        _groupsToUngroup = objects.OfType<LaserGroup>().ToList();
        foreach(var grp in _groupsToUngroup)
        {
            _groupChildren[grp] = new List<LaserObject>(grp.Children);
        }
    }

    public void Execute()
    {
        var newSelection = new List<LaserObject>();
        
        foreach(var grp in _groupsToUngroup)
        {
            ProjectState.Instance.RemoveObject(grp);
            
            foreach(var child in _groupChildren[grp])
            {
                child.Parent = null;
                ProjectState.Instance.AddObject(child);
                newSelection.Add(child);
            }
        }
        
        ProjectState.Instance.SelectedObjects = newSelection;
    }

    public void Undo()
    {
        // Re-create groups
        foreach(var grp in _groupsToUngroup)
        {
            foreach(var child in _groupChildren[grp])
            {
                ProjectState.Instance.RemoveObject(child);
                child.Parent = grp;
            }
            ProjectState.Instance.AddObject(grp);
        }
        
        ProjectState.Instance.SelectedObjects = new List<LaserObject>(_groupsToUngroup);
    }
}
