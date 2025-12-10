using System.Drawing;
using System.Linq;
using System.Collections.Generic;

namespace laser_gui_test.Data;

public class LaserGroup : LaserObject
{
    public List<LaserObject> Children { get; set; } = new();

    public LaserGroup()
    {
        Type = LaserObjectType.Group;
        Name = "Group";
    }

    public override void Draw(Graphics g, float scale)
    {
        foreach (var child in Children)
        {
            child.Draw(g, scale);
        }
    }

    public override bool HitTest(PointF point)
    {
        foreach (var child in Children)
        {
            if (child.HitTest(point)) return true;
        }
        return false;
    }

    public override RectangleF GetBounds()
    {
        if (Children.Count == 0) return RectangleF.Empty;
        
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool hasBounds = false;
        
        foreach(var child in Children)
        {
            var b = child.GetBounds();
            if (b.IsEmpty) continue;
            
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
            hasBounds = true;
        }
        
        if (!hasBounds) return RectangleF.Empty;
        
        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
    
    // Add logic to update bounds based on children if needed.
    // For now, Position/Size on Group might act as a user-manipulatable transform origin?
    // Or just a bounding box of children.
    // Simplifying: Group doesn't draw itself, children draw themselves at their world pos.
    // If we move Group, we move Children. 
    // Wait, LaserObject.Position is absolute? 
    // Current design: LaserObjects store absolute Position (or Points).
    // If grouped, do they become relative?
    // User Review said: "Clicking a child selects Group. Transformations apply to group."
    // Implementation:
    // If we move Group.Position, we must delta-move all children.
    // So LaserGroup.Position setter should shift children?
    // Or we keep it simple: Workbench moves selected objects. If selected object is Group, it moves Group.
    // Group.Position setter could iterate children.
    
    // Let's implement Position updating logic later in Workbench or Command.
    // For now, pure data structure.
}
