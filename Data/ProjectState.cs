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

    private LaserObject? _selectedObject;
    public LaserObject? SelectedObject
    {
        get => _selectedObject;
        set
        {
            _selectedObject = value;
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
