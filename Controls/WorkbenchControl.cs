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
        
        // 1. Reset State
        if (e.Button == MouseButtons.Left && !_isPanning) ResetInteractionState();

        PointF worldPos = ScreenToWorld(e.Location);
        _currentMouseWorld = worldPos;

        // 2. Panning (Right Click)
        if (e.Button == MouseButtons.Right)
        {
            _isPanning = true;
            _lastMousePos = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // 3. Creation Tools
        if (ToolManager.Instance.CurrentTool == ToolType.DrawBox)
        {
            var box = new LaserRectangle { Name = "Rectangle", Position = worldPos, Size = new SizeF(0, 0) };
            ProjectState.Instance.AddObject(box);
            _interactionObject = box;
            _isDragging = true;
            _dragStartPos = worldPos;
            return;
        }
        
        if (ToolManager.Instance.CurrentTool == ToolType.DrawLine)
        {
            var line = new LaserPath { Name = "Line", Position = worldPos };
            line.Points.Add(worldPos);
            line.Points.Add(worldPos); 
            ProjectState.Instance.AddObject(line);
            _interactionObject = line;
            _isDragging = true;
            return;
        }

        // 4. Selection Tool
        if (ToolManager.Instance.CurrentTool == ToolType.Select)
        {
            // A. Check Resize Handles
            int handle = HitTestHandles(worldPos);
            if (handle != -1)
            {
                _dragHandleIndex = handle;
                _isResizing = true;
                _dragStartPos = worldPos;
                _initialGroupBounds = GetSelectionBounds();
                SnapshotSelection();
                Invalidate();
                return;
            }

            // B. Hit Test Objects
            float hitTolerance = 8.0f / _zoom; 
            var hitObj = ProjectState.Instance.Objects.Reverse().FirstOrDefault(o => o.HitTest(worldPos, hitTolerance));

            if (hitObj != null)
            {
                bool isCtrl = (Control.ModifierKeys == Keys.Control);
                bool isSelected = ProjectState.Instance.SelectedObjects.Contains(hitObj);

                if (isCtrl)
                {
                    // Toggle Selection
                    var newSelection = new List<LaserObject>(ProjectState.Instance.SelectedObjects);
                    if (isSelected)
                    {
                        newSelection.Remove(hitObj);
                        ProjectState.Instance.SelectedObjects = newSelection;
                    }
                    else
                    {
                        newSelection.Add(hitObj);
                        ProjectState.Instance.SelectedObjects = newSelection;
                        // Prepare for move
                        _interactionObject = hitObj;
                        _isMoving = true;
                        _dragStartPos = worldPos;
                        _moveStartPos = worldPos;
                    }
                }
                else
                {
                    // Normal Click
                    if (isSelected)
                    {
                        // Clicked on already selected object -> Prepare for move (Group move)
                        _interactionObject = hitObj;
                        _isMoving = true;
                        _dragStartPos = worldPos;
                        _moveStartPos = worldPos;
                    }
                    else
                    {
                        // Select ONLY this object
                        ProjectState.Instance.SelectedObjects = new List<LaserObject> { hitObj };
                        _interactionObject = hitObj;
                        _isMoving = true;
                        _dragStartPos = worldPos;
                        _moveStartPos = worldPos;
                    }
                }
            }
            else
            {
                // C. Clicked Empty Space -> Start Selection Box
                _isSelecting = true;
                _dragStartPos = worldPos;
            }

            Invalidate();
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

        // 1. Panning
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

        // 2. Resizing
        if (_isResizing)
        {
            UpdateResize(worldPos);
            return;
        }

        // 3. Moving Objects
        if (_isMoving && _interactionObject != null)
        {
            Cursor = Cursors.SizeAll;
            float dx = worldPos.X - _dragStartPos.X;
            float dy = worldPos.Y - _dragStartPos.Y;
            
            foreach(var obj in ProjectState.Instance.SelectedObjects)
            {
                MoveObject(obj, dx, dy);
            }
            
            _dragStartPos = worldPos;
            Invalidate();
            return;
        }

        // 4. Creating Objects (Dragging)
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
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.DrawLine)
            {
                if (_interactionObject is LaserPath path && path.Points.Count >= 2)
                {
                    path.Points[1] = worldPos;
                }
            }
            Invalidate();
            return;
        }

        // 5. Selection Box
        if (_isSelecting)
        {
            Invalidate();
            return;
        }

        // 6. Cursor Updates
        if (ToolManager.Instance.CurrentTool == ToolType.Select)
        {
            if (HitTestHandles(worldPos) != -1)
            {
                Cursor = Cursors.SizeAll;
            }
            else
            {
                float hitTolerance = 8.0f / _zoom;
                bool hit = false;
                foreach (var obj in ProjectState.Instance.Objects.Reverse())
                {
                    if (obj.HitTest(worldPos, hitTolerance))
                    {
                        hit = true;
                        break;
                    }
                }
                Cursor = hit ? Cursors.SizeAll : Cursors.Default;
            }
        }
        else
        {
            Cursor = Cursors.Default;
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
            if (Control.ModifierKeys == Keys.Control)
            {
                 var current = new List<LaserObject>(ProjectState.Instance.SelectedObjects);
                 // Merge and Distinct
                 foreach(var item in list)
                 {
                     if (!current.Contains(item)) current.Add(item);
                 }
                 ProjectState.Instance.SelectedObjects = current;
            }
            else
            {
                ProjectState.Instance.SelectedObjects = list;
            }

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
