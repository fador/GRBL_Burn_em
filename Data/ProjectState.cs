using System.ComponentModel;
using System.Drawing;

namespace laser_gui_test.Data;

public class ProjectState : INotifyPropertyChanged
{
    private static ProjectState? _instance;
    public static ProjectState Instance => _instance ??= new ProjectState();

    public BindingList<LaserObject> Objects { get; set; } = new();
    public BindingList<Layer> Layers { get; set; } = new();
    
    private Layer? _activeLayer;
    public Layer? ActiveLayer 
    { 
        get => _activeLayer; 
        set 
        { 
            _activeLayer = value; 
            OnPropertyChanged(nameof(ActiveLayer));
        } 
    }

    private List<LaserObject> _selectedObjects = new();
    public List<LaserObject> SelectedObjects
    {
        get => _selectedObjects;
        set
        {
            _selectedObjects = value;
            OnPropertyChanged(nameof(SelectedObjects));
            // Backward compatibility / Primary selection
             OnPropertyChanged(nameof(SelectedObject));
        }
    }

    public LaserObject? SelectedObject
    {
        get => _selectedObjects.FirstOrDefault();
        set
        {
            _selectedObjects.Clear();
            if(value != null) _selectedObjects.Add(value);
            OnPropertyChanged(nameof(SelectedObjects));
            OnPropertyChanged(nameof(SelectedObject));
        }
    }

    public ProjectState()
    {
        // Default Layer
        var defaultLayer = new Layer("Layer 0", Color.Black);
        Layers.Add(defaultLayer);
        ActiveLayer = defaultLayer;
    }

    public Guid AddObject(LaserObject obj)
    {
        if(ActiveLayer != null)
        {
            obj.LayerId = ActiveLayer.Id;
        }
        Objects.Add(obj);
        return obj.Id;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
