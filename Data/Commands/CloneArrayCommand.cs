using System.Drawing;
using System.Linq;
using System.Collections.Generic;

namespace laser_gui_test.Data.Commands;

public class CloneArrayCommand : ICommand
{
    private List<LaserObject> _newObjects = new();
    private List<LaserObject> _sourceObjects;
    private int _rows;
    private int _cols;
    private float _gapX;
    private float _gapY;

    public CloneArrayCommand(IEnumerable<LaserObject> source, int rows, int cols, float gapX, float gapY)
    {
        _sourceObjects = source.ToList();
        _rows = rows;
        _cols = cols;
        _gapX = gapX;
        _gapY = gapY;
    }

    public void Execute()
    {
        _newObjects.Clear();
        
        foreach (var obj in _sourceObjects)
        {
            var bounds = obj.GetBounds();
            float w = bounds.Width;
            float h = bounds.Height;
            float stepX = w + _gapX;
            float stepY = h + _gapY;

            // Loop rows/cols
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    // Skip original if we consider (0,0) as original. 
                    // But typically Array *Tool* creates copies. 
                    // If we want to keep original, we shouldn't create a copy at 0,0?
                    // "Array" usually means "Create N copies".
                    // If user selects object and says 3x3 array. Do they imply 9 objects TOTAL (including original) or 9 NEW objects?
                    // Standard vector app behavior (Inkscape/Illustrator): Rows/Cols includes original.
                    // So if 1x1 -> No change.
                    // If 2x2 -> 3 new objects.
                    
                    if (r == 0 && c == 0) continue; // Original is here

                    var clone = obj.Clone();
                    
                    // Move
                    float dx = c * stepX;
                    float dy = -(r * stepY); // Standard Y is UP? No, Graphics Y is DOWN.
                    // If we want array to go DOWN (visual), we add Y.
                    // If "Rows" implies going down? Yes.
                    // Check Coordinate system:
                    // LaserObject.Position: usually Bottom-Left?
                    // Wait, LaserImage.Draw says: "Position.Y + Size.Height" is Top.
                    // So Position.Y is Bottom in their coordinate mind, but GDI+ treats it as Top-Left usually?
                    // Let's check GrblGenerator: "G1 X.. Y.."
                    // Typically CNC is Y+ Up.
                    // If GDI+ Y+ Down, then visual array down means increasing Y.
                    // If CNC Y+ Up, visual array down means decreasing Y.
                    
                    // Let's assume visual "Layout" logic match screen Y (Down).
                    // If Coordinate system is Standard Cartesian (Y Up), then "Rows" implies going UP or DOWN?
                    // Usually "Grid" expands Right and Up (Quadrant I). 
                    // But standard text/reading is Right and Down.
                    // Let's do Right (+X) and Up (+Y) for positive Gap?
                    // User requested "Distance between".
                    // Let's use +Y for Rows (Up) as default for CNC.
                    // If they want Down, they can use negative Gap?
                    // Or we just implement +Y (Up) and +X (Right).
                    
                    dy = r * stepY; 

                    ShiftObject(clone, dx, dy);
                    _newObjects.Add(clone);
                }
            }
        }
        
        foreach (var newObj in _newObjects)
        {
            ProjectState.Instance.Objects.Add(newObj);
        }
    }

    private void ShiftObject(LaserObject obj, float dx, float dy)
    {
        obj.Position = new PointF(obj.Position.X + dx, obj.Position.Y + dy);
        if (obj is LaserPath path)
        {
            for (int i = 0; i < path.Points.Count; i++)
            {
                path.Points[i] = new PointF(path.Points[i].X + dx, path.Points[i].Y + dy);
            }
        }
        else if (obj is LaserGroup group)
        {
             // If Group Position is just a reference, we need to shift children?
             // LaserGroup.Clone() clones children.
             // But LaserGroup.Position logic is weak in current codebase.
             // If we shift Group Position, does it affect children?
             // Check LaserGroup.Draw -> calls child.Draw.
             // Child.Draw uses Child.Position.
             // So changing Group.Position does NOTHING unless we propagate.
             // BUT: Clone() makes new children.
             // We provided ShiftObject.
             // We need to Recurse.
             
             foreach(var child in group.Children)
             {
                 ShiftObject(child, dx, dy);
             }
        }
    }

    public void Undo()
    {
        foreach (var obj in _newObjects)
        {
            ProjectState.Instance.Objects.Remove(obj);
        }
        ProjectState.Instance.SelectedObjects.Clear();
        // Select original?
    }

    public string Description => $"Array Clone {_rows}x{_cols}";
}
