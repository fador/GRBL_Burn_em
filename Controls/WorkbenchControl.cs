using System.Drawing.Drawing2D;
using laser_gui_test.Data;
using laser_gui_test.Tools;

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
        // Calculate visible area to optimize drawing
        // This is a simplified infinite grid for now
        // A robust version would inverse transform clip bounds
        
        var pen = new Pen(Color.LightGray, 1.0f / _zoom);
        
        // Draw some grid lines around origin (temporary simple approach)
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
            
            // Get layer color
            /*
            var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId);
            Color color = layer?.Color ?? Color.Black;
            */
            // For now let the object draw itself, or we can override the pen here.
            // Pushing the graphics state would be safe.
            
            var state = g.Save();
            obj.Draw(g, _zoom);
            
            // Draw selection highlight
            if (obj == ProjectState.Instance.SelectedObject)
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
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        
        // Transform mouse coordinates to world coordinates
        PointF worldPos = ScreenToWorld(e.Location);

        if (e.Button == MouseButtons.Middle || ToolManager.Instance.CurrentTool == ToolType.Pan)
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
                    // Hit test logic
                    bool found = false;
                    if (ProjectState.Instance.Objects.Count > 0)
                    {
                        foreach (var obj in ProjectState.Instance.Objects.Reverse())
                        {
                            if (obj.HitTest(worldPos))
                            {
                                ProjectState.Instance.SelectedObject = obj;
                                found = true;
                                
                                // Start Move
                                _interactionObject = obj;
                                _isMoving = true;
                                _dragStartPos = worldPos; // Use this as "Last Pos" for delta calc
                                break;
                            }
                        }
                    }
                    if (!found) ProjectState.Instance.SelectedObject = null;
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
                        Position = worldPos, // Not really used for Path but good for reference
                    };
                    line.Points.Add(worldPos);
                    line.Points.Add(worldPos); // End point starts at start
                    
                    ProjectState.Instance.AddObject(line);
                    _interactionObject = line;
                    _isDragging = true;
                    // _dragStartPos is technically first point
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

        if (_isMoving && _interactionObject != null)
        {
             float dx = worldPos.X - _dragStartPos.X;
             float dy = worldPos.Y - _dragStartPos.Y;
             
             if (_interactionObject is LaserPath path)
             {
                 for(int i=0; i<path.Points.Count; i++)
                 {
                     path.Points[i] = new PointF(path.Points[i].X + dx, path.Points[i].Y + dy);
                 }
                 // Update visual position for center (optional)
                 path.Position = new PointF(path.Position.X + dx, path.Position.Y + dy);
             }
             else
             {
                 _interactionObject.Position = new PointF(_interactionObject.Position.X + dx, _interactionObject.Position.Y + dy);
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
        
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = Cursors.Default;
        }

        if (_isDragging)
        {
            _isDragging = false;
            _interactionObject = null;
        }

        if (_isMoving)
        {
            _isMoving = false;
            _interactionObject = null;
        }
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
}
