/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Drawing;
using System.Linq;
using System.Collections.Generic;

namespace grbl_burn_em.Data;

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
        var state = g.Save();
        if (Rotation != 0)
        {
            var b = GetBounds();
            if (!b.IsEmpty)
            {
                float cx = b.X + b.Width / 2f;
                float cy = b.Y + b.Height / 2f;
                g.TranslateTransform(cx, cy);
                g.RotateTransform(Rotation);
                g.TranslateTransform(-cx, -cy);
            }
        }
        foreach (var child in Children)
        {
            child.Draw(g, scale);
        }
        g.Restore(state);
    }

    /// <summary>
    /// Returns deep copies of the group's children with the group's rotation baked
    /// into their geometry (rotation around the group's bounds center). Used by the
    /// G-code generators so rotated groups cut rotated.
    /// </summary>
    public static List<LaserObject> CreateRotatedChildren(LaserGroup group)
    {
        var result = new List<LaserObject>();
        if (group.Rotation == 0)
        {
            foreach (var child in group.Children) result.Add(child.Clone());
            return result;
        }

        var b = group.GetBounds();
        // Note: a degenerate bounds (e.g. a horizontal line, height 0) is still a
        // valid rotation center - only skip when there are no children at all.
        float cx = b.X + b.Width / 2f;
        float cy = b.Y + b.Height / 2f;
        if (group.Children.Count == 0) return result;
        float rad = group.Rotation * (float)Math.PI / 180f;
        float cos = (float)Math.Cos(rad);
        float sin = (float)Math.Sin(rad);

        foreach (var child in group.Children)
        {
            var clone = child.Clone();

            if (clone is LaserPath path)
            {
                for (int i = 0; i < path.Points.Count; i++)
                    path.Points[i] = RotatePoint(path.Points[i], cx, cy, cos, sin);
                path.UpdateBounds();
                result.Add(path);
            }
            else if (clone is LaserBezier bezier)
            {
                for (int i = 0; i < bezier.Points.Count; i++)
                    bezier.Points[i] = RotatePoint(bezier.Points[i], cx, cy, cos, sin);
                bezier.UpdateBounds();
                result.Add(bezier);
            }
            else if (clone is LaserGroup nestedGroup)
            {
                // Bake the nested group's rotation into its children, then apply the
                // outer rotation to each flattened child.
                foreach (var inner in CreateRotatedChildren(nestedGroup))
                {
                    if (inner is LaserPath ip)
                    {
                        for (int i = 0; i < ip.Points.Count; i++)
                            ip.Points[i] = RotatePoint(ip.Points[i], cx, cy, cos, sin);
                        ip.UpdateBounds();
                    }
                    else if (inner is LaserBezier ib)
                    {
                        for (int i = 0; i < ib.Points.Count; i++)
                            ib.Points[i] = RotatePoint(ib.Points[i], cx, cy, cos, sin);
                        ib.UpdateBounds();
                    }
                    else
                    {
                        inner.Position = RotatePoint(inner.Position, cx, cy, cos, sin);
                        inner.Rotation += group.Rotation;
                    }
                    result.Add(inner);
                }
            }
            else if (clone is LaserText text)
            {
                // Text rotates around its Position (see LaserText.GetPath), so the
                // origin point is rotated and the text's own rotation is added.
                text.Position = RotatePoint(text.Position, cx, cy, cos, sin);
                text.Rotation += group.Rotation;
                result.Add(text);
            }
            else
            {
                // Shapes (rect/circle/image) rotate around their own center: rotate the
                // center around the group center, then re-derive the top-left position.
                PointF center = new PointF(
                    clone.Position.X + clone.Size.Width / 2f,
                    clone.Position.Y + clone.Size.Height / 2f);
                PointF newCenter = RotatePoint(center, cx, cy, cos, sin);
                clone.Position = new PointF(
                    newCenter.X - clone.Size.Width / 2f,
                    newCenter.Y - clone.Size.Height / 2f);
                clone.Rotation += group.Rotation;
                result.Add(clone);
            }
        }
        return result;
    }

    private static PointF RotatePoint(PointF p, float cx, float cy, float cos, float sin)
    {
        float dx = p.X - cx;
        float dy = p.Y - cy;
        return new PointF(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        foreach (var child in Children)
        {
            if (child.HitTest(point, tolerance)) return true;
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
            // Skip only children with no geometry at all (default rect 0,0,0,0).
            // NOTE: a degenerate bounds like a horizontal line (height 0) is still
            // meaningful - RectangleF.IsEmpty would wrongly skip it.
            if (b.Left == 0 && b.Top == 0 && b.Width == 0 && b.Height == 0) continue;
            
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
    public override LaserObject Clone()
    {
        var clone = new LaserGroup
        {
            Id = Guid.NewGuid(),
            Name = this.Name + " (Copy)",
            LayerId = this.LayerId,
            IsEnabled = this.IsEnabled,
            Power = this.Power,
            Speed = this.Speed,
            Position = this.Position,
            Rotation = this.Rotation,
            Size = this.Size,
            Children = this.Children.Select(c => c.Clone()).ToList()
        };
        // Fix parent references if we had them?
        foreach(var child in clone.Children)
        {
            child.Parent = clone;
        }
        return clone;
    }
}
