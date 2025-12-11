using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;
using laser_gui_test.Data;
using laser_gui_test.Tools;
using laser_gui_test.Data.Commands;
using laser_gui_test.Forms;

namespace laser_gui_test.Controls;

public class WorkbenchControl : Control
{
    private float _zoom = 1.0f;
    private PointF _panOffset = new PointF(0, 0);
    private Point _lastMousePos;
    private bool _isPanning = false;

    // Grid settings
    private const float GridSizeCm = 1.0f; // 1 cm
    private const float Dpi = 96.0f; // Standard screen DPI
    private float GridInPixels => GridSizeCm / 2.54f * Dpi;

    public WorkbenchControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.WhiteSmoke;
        
        // Connect to data updates
        ProjectState.Instance.Objects.ListChanged += (s, e) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Apply transformations
        g.TranslateTransform(Width / 2f + _panOffset.X, Height / 2f + _panOffset.Y);
        g.ScaleTransform(_zoom, _zoom);

        DrawGrid(g);
        DrawOrigin(g);
        DrawObjects(g);
    }

    private void DrawGrid(Graphics g)
    {
        var pen = new Pen(Color.LightGray, 1.0f / _zoom);
        
        int lines = 100;
        float step = GridInPixels;

        for (int i = -lines; i <= lines; i++)
        {
            g.DrawLine(pen, i * step, -lines * step, i * step, lines * step);
            g.DrawLine(pen, -lines * step, i * step, lines * step, i * step);
        }
    }

    private void DrawOrigin(Graphics g)
    {
        var pen = new Pen(Color.Red, 2.0f / _zoom);
        float len = 10.0f;
        g.DrawLine(pen, -len, 0, len, 0); // X axis
        g.DrawLine(pen, 0, -len, 0, len); // Y axis
    }

    private void DrawObjects(Graphics g)
    {
        foreach (var obj in ProjectState.Instance.Objects)
        {
            if (!obj.IsEnabled) continue;
            
            var state = g.Save();
            obj.Draw(g, _zoom);
            
            // Draw selection highlight
            if (ProjectState.Instance.SelectedObjects.Contains(obj))
            {
                using var selPen = new Pen(Color.Cyan, 2.0f / _zoom);
                selPen.DashStyle = DashStyle.Dash;
                if (obj is LaserRectangle rect)
                {
                    g.DrawRectangle(selPen, rect.Position.X, rect.Position.Y, rect.Size.Width, rect.Size.Height);
                }
                else if (obj is LaserPath path && path.Points.Count > 1)
                {
                    g.DrawLines(selPen, path.Points.ToArray());
                }
            }
            
            g.Restore(state);
        }

        // Draw selection box interaction
        if (_isSelecting && ToolManager.Instance.CurrentTool == ToolType.Select)
        {
             // Normalizing rect for drawing
             float x = Math.Min(_dragStartPos.X, _currentMouseWorld.X);
             float y = Math.Min(_dragStartPos.Y, _currentMouseWorld.Y);
             float w = Math.Abs(_currentMouseWorld.X - _dragStartPos.X);
             float h = Math.Abs(_currentMouseWorld.Y - _dragStartPos.Y);
             
             using var boxBrush = new SolidBrush(Color.FromArgb(50, Color.Cyan));
             using var boxPen = new Pen(Color.Cyan, 1.0f / _zoom);
             g.FillRectangle(boxBrush, x, y, w, h);
             g.DrawRectangle(boxPen, x, y, w, h);
        }
        
        // Draw Resize Handles
        if (ToolManager.Instance.CurrentTool == ToolType.Select && ProjectState.Instance.SelectedObjects.Count > 0)
        {
            var bounds = GetSelectionBounds();
            if (bounds != null)
            {
                using var boundaryPen = new Pen(Color.Cyan, 1.0f / _zoom);
                boundaryPen.DashStyle = DashStyle.Solid;
                g.DrawRectangle(boundaryPen, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height);
                DrawResizeHandles(g, bounds.Value);
            }
        }
    }

    private PointF _currentMouseWorld;
    private bool _isSelecting = false;

    private PointF _moveStartPos; // To calculate total delta for Command

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        
        // Safety reset to ensure no lingering state from previous interactions
        if (e.Button == MouseButtons.Left && !_isPanning)
        {
             ResetInteractionState();
        }

        // Transform mouse coordinates to world coordinates
        PointF worldPos = ScreenToWorld(e.Location);
        _currentMouseWorld = worldPos;

        if (e.Button == MouseButtons.Right)
        {
            _isPanning = true;
            _lastMousePos = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            switch (ToolManager.Instance.CurrentTool)
            {
                case ToolType.Select:
                    // Check handles first
                    int handle = HitTestHandles(worldPos);
                    if (handle != -1)
                    {
                        _dragHandleIndex = handle;
                        _isResizing = true;
                        _dragStartPos = worldPos;
                        _initialGroupBounds = GetSelectionBounds();
                        // Snapshot object states
                        SnapshotSelection();
                        Invalidate(); // Ensure UI feedback immediately
                        return;
                    }
                
                    // Hit test logic
                    bool hit = false;
                    foreach (var obj in ProjectState.Instance.Objects.Reverse())
                    {
                        if (obj.HitTest(worldPos))
                        {
                            hit = true;
                            
                            // Interaction Fixes:
                            if (Control.ModifierKeys == Keys.Control)
                            {
                                // Toggle Selection
                                var currentSel = new List<LaserObject>(ProjectState.Instance.SelectedObjects);
                                if (currentSel.Contains(obj))
                                {
                                    currentSel.Remove(obj);
                                    ProjectState.Instance.SelectedObjects = currentSel;
                                    // Can't move/drag if we just deselected it
                                }
                                else
                                {
                                    currentSel.Add(obj);
                                    ProjectState.Instance.SelectedObjects = currentSel;
                                    
                                    // Make this the interaction object for potential drag
                                    _interactionObject = obj;
                                    _isMoving = true;
                                    _dragStartPos = worldPos;
                                    _moveStartPos = worldPos;
                                }
                            }
                            else
                            {
                                // Normal Click
                                if (ProjectState.Instance.SelectedObjects.Contains(obj))
                                {
                                    // Allows moving existing selection (group move)
                                    // NOTE: We do NOT deselect others yet, in case the user wants to drag the group.
                                    // We handle "Select Only This" in OnMouseUp if no drag occurred.
                                    _interactionObject = obj;
                                    _isMoving = true;
                                    _dragStartPos = worldPos;
                                    _moveStartPos = worldPos; 
                                }
                                else
                                {
                                    // Select ONLY this (Clear others)
                                    ProjectState.Instance.SelectedObjects = new List<LaserObject> { obj };
                                    
                                    _interactionObject = obj;
                                    _isMoving = true;
                                    _dragStartPos = worldPos;
                                    _moveStartPos = worldPos;
                                }
                            }
                            break;
                        }
                    }
                    
                    if (!hit)
                    {
                        // Clicked empty space
                        // Start Selection Box
                        //ProjectState.Instance.SelectedObjects = new List<LaserObject>(); // Clear selection immediately
                        _isSelecting = true;
                        _dragStartPos = worldPos;
                    }
                    
                    ProjectState.Instance.Objects.ResetBindings(); 
                    Invalidate(); 
                    break;

                case ToolType.DrawBox:
                    // Start drawing box
                    var box = new LaserRectangle
                    {
                        Name = "Rectangle",
                        Position = worldPos,
                        Size = new SizeF(0, 0)
                    };
                    ProjectState.Instance.AddObject(box);
                    _interactionObject = box;
                    _isDragging = true;
                    _dragStartPos = worldPos;
                    break;
                
                case ToolType.DrawLine:
                    var line = new LaserPath
                    {
                        Name = "Line",
                        Position = worldPos, 
                    };
                    line.Points.Add(worldPos);
                    line.Points.Add(worldPos); 
                    
                    ProjectState.Instance.AddObject(line);
                    _interactionObject = line;
                    _isDragging = true;
                    break;
            }
        }
    }

    private LaserObject? _interactionObject;
    private bool _isDragging = false; // For creation
    private bool _isMoving = false; // For moving existing objects
    private PointF _dragStartPos; // Used as "Last Mouse Pos" for moving

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointF worldPos = ScreenToWorld(e.Location);
        _currentMouseWorld = worldPos;

        if (_isPanning)
        {
            float dx = e.X - _lastMousePos.X;
            float dy = e.Y - _lastMousePos.Y;
            _panOffset.X += dx;
            _panOffset.Y += dy;
            _lastMousePos = e.Location;
            Invalidate();
            return;
        }
        
        // Cursor Update Logic
        if (!_isSelecting && !_isDragging && !_isMoving && !_isResizing && !_isPanning)
        {
            if (ToolManager.Instance.CurrentTool == ToolType.Select)
            {
                // Check handles
                if (HitTestHandles(worldPos) != -1)
                {
                    // Basic cursor for handles, could be specific (SizeNWSE etc) but Default or Cross is fine for now, 
                    // or let's use SizeAll for now as requested for "moving", but for handles "SizeAll" is ambiguous.
                    // User asked for "move" cursor when in position to START MOVING an object.
                    // Handles are for resizing.
                    // Let's check object hit.
                    Cursor = Cursors.SizeAll; // Simplify for handles too for now? Or keep Default. 
                    // Actually handles should probably be SizeNESW etc.
                    // Let's stick to the request: "changes to the 'move' when mouse is in position where you can start moving an object"
                }
                else
                {
                    bool hitObject = false;
                    foreach (var obj in ProjectState.Instance.Objects.Reverse())
                    {
                        if (obj.HitTest(worldPos))
                        {
                            hitObject = true;
                            break;
                        }
                    }
                    
                    if (hitObject)
                    {
                        Cursor = Cursors.SizeAll;
                    }
                    else
                    {
                        Cursor = Cursors.Default;
                    }
                }
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }

        if (_isSelecting)
        {
            Invalidate(); // Draw selection box
        }
        
        if (_isResizing)
        {
            UpdateResize(worldPos);
            return;
        }

        if (_isMoving && _interactionObject != null)
        {
             Cursor = Cursors.SizeAll; // Ensure we keep the move cursor while moving
             float dx = worldPos.X - _dragStartPos.X;
             float dy = worldPos.Y - _dragStartPos.Y;
             
             // Move all Selected Objects
             foreach(var obj in ProjectState.Instance.SelectedObjects)
             {
                 MoveObject(obj, dx, dy);
             }
             
             _dragStartPos = worldPos; // Update for next delta
             Invalidate();
             return;
        }


        if (_isDragging && _interactionObject != null)
        {
            if (ToolManager.Instance.CurrentTool == ToolType.DrawBox)
            {
                float x = Math.Min(_dragStartPos.X, worldPos.X);
                float y = Math.Min(_dragStartPos.Y, worldPos.Y);
                float w = Math.Abs(worldPos.X - _dragStartPos.X);
                float h = Math.Abs(worldPos.Y - _dragStartPos.Y);
                _interactionObject.Position = new PointF(x, y);
                _interactionObject.Size = new SizeF(w, h);
                Invalidate();
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.DrawLine)
            {
                if (_interactionObject is LaserPath path && path.Points.Count >= 2)
                {
                    path.Points[1] = worldPos;
                    Invalidate();
                }
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        
        _currentMouseWorld = ScreenToWorld(e.Location);
        
        if (e.Button == MouseButtons.Right)
        {
            _isPanning = false;
            Cursor = Cursors.Default;
        }

        if (_isSelecting)
        {
            _isSelecting = false;
            // Find objects in rect
            float x = Math.Min(_dragStartPos.X, _currentMouseWorld.X);
            float y = Math.Min(_dragStartPos.Y, _currentMouseWorld.Y);
            float w = Math.Abs(_currentMouseWorld.X - _dragStartPos.X);
            float h = Math.Abs(_currentMouseWorld.Y - _dragStartPos.Y);
            var rect = new RectangleF(x, y, w, h);
            
            var list = new List<LaserObject>();
            foreach(var obj in ProjectState.Instance.Objects)
            {
                // Robust bounds intersection
                var objRect = obj.GetBounds();
                if (!objRect.IsEmpty && rect.IntersectsWith(objRect))
                {
                    list.Add(obj);
                }
            }
            ProjectState.Instance.SelectedObjects = list;

            // Popup window listing the selected objects            
            if(false && list.Count > 0)
            {
                var selectedObjectsForm = new SelectedObjectsForm(list);
                selectedObjectsForm.ShowDialog();
            }

            // Update MainForm
            MainForm.Instance.UpdateSelectedObjects();
            
            //ProjectState.Instance.Objects.ResetBindings(); 
            Invalidate();
            return;
        }

        if (_isDragging)
        {
            _isDragging = false;
            if (_interactionObject != null)
            {
               ProjectState.Instance.RemoveObject(_interactionObject);
               var cmd = new AddObjectCommand(_interactionObject);
               CommandManager.Instance.Execute(cmd);
            }
            _interactionObject = null;
        }

        if (_isMoving)
        {
            _isMoving = false;
            
            // Calculate Total Move Delta
            float dx = _currentMouseWorld.X - _moveStartPos.X;
            float dy = _currentMouseWorld.Y - _moveStartPos.Y;
            bool movedSignificantly = Math.Abs(dx) > 0.001 || Math.Abs(dy) > 0.001;

            if (movedSignificantly)
            {
                // Revert the interactive move to prevent double application by MoveCommand.Execute()
                foreach(var obj in ProjectState.Instance.SelectedObjects)
                {
                     MoveObject(obj, -dx, -dy);
                }

                var cmd = new MoveCommand(ProjectState.Instance.SelectedObjects, dx, dy);
                CommandManager.Instance.Execute(cmd);
            }
            else
            {
                // It was a Click (not a drag)
                // If we clicked a selected object without dragging, and Ctrl wasn't held,
                // we now reduce the selection to just that object.
                if (ToolManager.Instance.CurrentTool == ToolType.Select && 
                    Control.ModifierKeys != Keys.Control && 
                    _interactionObject != null)
                {
                    ProjectState.Instance.SelectedObjects = new List<LaserObject> { _interactionObject };
                    ProjectState.Instance.Objects.ResetBindings();
                    Invalidate();
                }
            }
            _interactionObject = null;
        }
        
        if (_isResizing)
        {
            _isResizing = false;
            _dragHandleIndex = -1;
            _initialGroupBounds = null;
            
            var newStates = new Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points)>();
            foreach (var obj in ProjectState.Instance.SelectedObjects)
            {
                List<PointF>? pts = null;
                if (obj is LaserPath p) pts = new List<PointF>(p.Points);
                newStates[obj] = (obj.Position, obj.Size, pts);
            }
            
            var cmd = new ResizeCommand(_initialStates, newStates);
            CommandManager.Instance.Execute(cmd);
            
            _initialStates.Clear();
        }
        
        // Final safety reset
        ResetInteractionState();
    }

    private void ResetInteractionState()
    {
        _isSelecting = false;
        _isDragging = false;
        _isMoving = false;
        _isResizing = false;
        _interactionObject = null;
        _dragHandleIndex = -1;
        _initialGroupBounds = null;
        _initialStates.Clear();
    }
    
    // Resize Handles
    private RectangleF? GetSelectionBounds()
    {
        if (ProjectState.Instance.SelectedObjects.Count == 0) return null;
        
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool hasBounds = false;
        
        foreach (var obj in ProjectState.Instance.SelectedObjects)
        {
            var b = obj.GetBounds();
            if (b.IsEmpty) continue;
            
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
            hasBounds = true;
        }
        
        if (!hasBounds) return null;
        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
    
    private void DrawResizeHandles(Graphics g, RectangleF bounds)
    {
        float size = 8.0f / _zoom; // Constant screen size handles
        using var brush = new SolidBrush(Color.White);
        using var pen = new Pen(Color.Black, 1.0f / _zoom);

        // 8 handles
        PointF[] handles = GetHandlePositions(bounds);
        
        foreach (var h in handles)
        {
            g.FillRectangle(brush, h.X - size/2, h.Y - size/2, size, size);
            g.DrawRectangle(pen, h.X - size/2, h.Y - size/2, size, size);
        }
    }
    
    private PointF[] GetHandlePositions(RectangleF b)
    {
        return new PointF[] {
            new(b.Left, b.Top), // TL
            new(b.Left + b.Width/2, b.Top), // T
            new(b.Right, b.Top), // TR
            new(b.Right, b.Top + b.Height/2), // R
            new(b.Right, b.Bottom), // BR
            new(b.Left + b.Width/2, b.Bottom), // B
            new(b.Left, b.Bottom), // BL
            new(b.Left, b.Top + b.Height/2) // L
        };
    }
    
    private int _dragHandleIndex = -1; // -1 none, 0-7 handles
    
    private bool _isResizing = false;
    private RectangleF? _initialGroupBounds;
    private Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points)> _initialStates = new();

    private void SnapshotSelection()
    {
        _initialStates.Clear();
        foreach (var obj in ProjectState.Instance.SelectedObjects)
        {
            List<PointF>? pts = null;
            if (obj is LaserPath p) pts = new List<PointF>(p.Points);
            _initialStates[obj] = (obj.Position, obj.Size, pts);
        }
    }

    private void UpdateResize(PointF currentPos)
    {
        if (_initialGroupBounds == null) return;
        var b = _initialGroupBounds.Value;
        float l = b.Left, t = b.Top, r = b.Right, bm = b.Bottom;
        
        // Update bounds based on handle
        // 0:TL, 1:T, 2:TR, 3:R, 4:BR, 5:B, 6:BL, 7:L
        
        float newL = l, newT = t, newR = r, newB = bm;
        
        if (_dragHandleIndex == 0 || _dragHandleIndex == 6 || _dragHandleIndex == 7) newL = currentPos.X;
        if (_dragHandleIndex == 0 || _dragHandleIndex == 1 || _dragHandleIndex == 2) newT = currentPos.Y;
        if (_dragHandleIndex == 2 || _dragHandleIndex == 3 || _dragHandleIndex == 4) newR = currentPos.X;
        if (_dragHandleIndex == 4 || _dragHandleIndex == 5 || _dragHandleIndex == 6) newB = currentPos.Y;
        
        // Validate inverted bounds (flip if needed, but for now simple clamp or allow flip)
        float newW = newR - newL;
        float newH = newB - newT;
        
        // Determine Scale Factors
        float scaleX = (b.Width == 0) ? 1 : newW / b.Width;
        float scaleY = (b.Height == 0) ? 1 : newH / b.Height;
        
        // Apply to objects
        foreach (var kvp in _initialStates)
        {
            var obj = kvp.Key;
            var init = kvp.Value;
            
            // Relative position to group origin (TopLeft of group)
            float relX = init.Pos.X - b.Left;
            float relY = init.Pos.Y - b.Top;
            
            obj.Position = new PointF(newL + relX * scaleX, newT + relY * scaleY);
            obj.Size = new SizeF(init.Size.Width * scaleX, init.Size.Height * scaleY);
            
            if (obj is LaserPath p && init.Points != null)
            {
                for(int i=0; i<p.Points.Count; i++)
                {
                    float px = init.Points[i].X - b.Left;
                    float py = init.Points[i].Y - b.Top;
                    p.Points[i] = new PointF(newL + px * scaleX, newT + py * scaleY);
                }
            }
        }
        Invalidate();
    }
    
    private void MoveObject(LaserObject obj, float dx, float dy)
    {
         if (obj is LaserPath path)
         {
             for(int i=0; i<path.Points.Count; i++)
             {
                 path.Points[i] = new PointF(path.Points[i].X + dx, path.Points[i].Y + dy);
             }
             path.Position = new PointF(path.Position.X + dx, path.Position.Y + dy);
         }
         else if (obj is LaserGroup group)
         {
             foreach(var child in group.Children)
             {
                 MoveObject(child, dx, dy);
             }
         }
         else
         {
             obj.Position = new PointF(obj.Position.X + dx, obj.Position.Y + dy);
         }
    }

    private int HitTestHandles(PointF pos)
    {
         var bounds = GetSelectionBounds();
         if (bounds == null) return -1;
         
         var handles = GetHandlePositions(bounds.Value);
         float size = 8.0f / _zoom;
         
         for(int i=0; i<handles.Length; i++)
         {
             var r = new RectangleF(handles[i].X - size/2, handles[i].Y - size/2, size, size);
             if (r.Contains(pos)) return i;
         }
         return -1;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float zoomFactor = 1.1f;
        if (e.Delta > 0)
            _zoom *= zoomFactor;
        else
            _zoom /= zoomFactor;
        
        // Clamp zoom
        if (_zoom < 0.1f) _zoom = 0.1f;
        if (_zoom > 50.0f) _zoom = 50.0f;

        Invalidate();
    }
    private PointF ScreenToWorld(Point screenPoint)
    {
        // Inverse transform
        // Screen = (World * Scale) + Offset + Center
        // World = (Screen - Center - Offset) / Scale
        
        float x = (screenPoint.X - Width / 2f - _panOffset.X) / _zoom;
        float y = (screenPoint.Y - Height / 2f - _panOffset.Y) / _zoom;
        return new PointF(x, y);
    }
}
