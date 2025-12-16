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

using System.ComponentModel;

namespace laser_gui_test.Controls;

public class WorkbenchControl : Control
{
    private float _zoom = 1.0f;
    private PointF _panOffset = new PointF(0, 0);
    private Point _lastMousePos;
    private bool _isPanning = false;

    // Grid settings
    private const float GridSizeMm = 10.0f; // 10 mm
    private float GridStep => GridSizeMm;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsSnappingEnabled { get; set; } = false;
    public float SnapInterval => AppConfiguration.Instance.SnapGridSize;

    private PointF _laserPosition = new PointF(0,0);
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PointF LaserPosition
    {
        get => _laserPosition;
        set
        {
            _laserPosition = value;
            Invalidate(); // Redraw when position changes
        }
    }

    // Background Image Support
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Bitmap? OverlayImage { get; set; }
    
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PointF OverlayImagePosition { get; set; } = new PointF(0, 0); // World Coords (Top-Left?)
    
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SizeF OverlayImageSize { get; set; } = new SizeF(100, 100); // World Size
    
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float OverlayImageOpacity { get; set; } = 0.5f;


    private PointF Snap(PointF p)
    {
        if (!IsSnappingEnabled) return p;
        float interval = SnapInterval;
        float x = (float)Math.Round(p.X / interval) * interval;
        float y = (float)Math.Round(p.Y / interval) * interval;
        return new PointF(x, y);
    }

    public float Zoom => _zoom;
    public PointF PanOffset => _panOffset;

    public event Action<PointF>? MousePositionChanged;

    public WorkbenchControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.WhiteSmoke;
        
        // Load View Settings
        if (AppConfiguration.Instance.LastZoom > 0.01f) // Basic sanity check
        {
            _zoom = AppConfiguration.Instance.LastZoom;
            _panOffset = new PointF(AppConfiguration.Instance.LastPanX, AppConfiguration.Instance.LastPanY);
        }

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
        g.ScaleTransform(_zoom, -_zoom); // Y-Up coordinate system

        DrawBackgroundImage(g);
        DrawGrid(g);
        DrawWorkArea(g); // Draw Work Area before Origin
        DrawOrigin(g);
        DrawObjects(g);
        DrawLaserPosition(g);
        DrawRulerOverlay(g);
    }

    private void DrawWorkArea(Graphics g)
    {
        float w = AppConfiguration.Instance.WorkAreaWidth;
        float h = AppConfiguration.Instance.WorkAreaHeight;
        string origin = AppConfiguration.Instance.WorkOrigin;
        
        float x = 0;
        float y = 0;
        
        // Coordinate system: 
        // We assume World Coordinates: X Right, Y Down (standard GDI+).
        // BUT for Laser/CNC, Y is usually Up.
        // If we want "Bottom Left" to be 0,0.
        // If our view assumes Y+ is Down...
        // Let's stick to the visual representation.
        // User sends G-code. Only GrblGenerator cares about coordinate flipping if needed.
        // In this GUI, let's assume standard Cartesian for the user interaction if possible?
        // Or standard Screen.
        // If "Bottom Left" is 0,0, and Y+ is Down (Screen).
        // Then "Up" is negative Y.
        
        // Let's interpret "Work Area Height" as extending in the "Height" direction.
        
        if (origin == "BottomLeft")
        {
             // 0,0 is Bottom Left. Box extends Right (+X) and Up (+Y).
             x = 0;
             y = 0; 
        }
        else if (origin == "TopLeft")
        {
             // 0,0 is Top Left. Box extends Right (+X) and Down (-Y).
             x = 0;
             y = -h;
        }
        else if (origin == "Center")
        {
             x = -w / 2;
             y = -h / 2;
        }
        
        using var pen = new Pen(Color.Black, 3.0f / _zoom);
        g.DrawRectangle(pen, x, y, w, h);
    }

    private void DrawBackgroundImage(Graphics g)
    {
        if (OverlayImage != null)
        {
             // Draw Image in World Coordinates
             // We need to handle Opacity
             
             // Create ColorMatrix
             float opacity = OverlayImageOpacity;
             if (opacity < 0) opacity = 0;
             if (opacity > 1) opacity = 1;
             
             System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix();
             cm.Matrix33 = opacity;
             
             using var ia = new System.Drawing.Imaging.ImageAttributes();
             ia.SetColorMatrix(cm);
             
             // Get Config
             var config = AppConfiguration.Instance;
             
             // Dest Rect
             float x = config.CameraOverlayX;
             float y = config.CameraOverlayY;
             float w = config.CameraOverlayWidth;
             float h = config.CameraOverlayHeight;
             
             // Note: In our current View Transform (Scale Y = -Zoom), +Y is Down on screen (if global Y is Up).
             // Wait, OnPaint: g.ScaleTransform(_zoom, -_zoom);
             // If World Y is Up. (0,0) is origin. (0, 100) is Up.
             // Screen Y is Down. (0,0) is Top-Left.
             // (0, 100) World -> (0, -100) Screen. (Above top edge).
             
             // Image Drawing:
             // If we draw image at (x,y) with height h.
             // We want Top of Image to be at Y+H? Or Y?
             // Usually "Position" means Top-Left of the entity.
             // In Y-Up world, Top-Left is (x, y_max).
             // If User sets Y=0 (Bottom), and H=100. Top is 100.
             // So if OverlayY is "Bottom Edge", then we draw from Y+H down to Y.
             // If OverlayY is "Top Edge", we draw from Y down to Y-H.
             
             // Let's assume standard UI convention: X,Y is Top-Left.
             // So Top Edge = Y. Bottom Edge = Y - H.
             // Right Edge = X + W.
             
             // However, CameraControl NUDs allow negative values.
             
             // Let's try to map the Image (0,0) [Top-Left] to World (x,y).
             // And Image (0,h) [Bottom-Left] to World (x, y-h).
             // This assumes Y is Top-Left coordinate.
             
             PointF[] destPoints = {
                 new PointF(x, y),         // UL (Upper Left of source maps here) -> World Top-Left
                 new PointF(x + w, y),     // UR -> World Top-Right
                 new PointF(x, y - h)      // DL -> World Bottom-Left
             };
             
             g.DrawImage(OverlayImage, destPoints, 
                 new RectangleF(0, 0, OverlayImage.Width, OverlayImage.Height),
                 GraphicsUnit.Pixel, ia);
        }
    }

    private void DrawGrid(Graphics g)
    {
        var pen = new Pen(Color.LightGray, 1.0f / _zoom);
        
        int lines = 100;
        float step = GridStep; // 10.0 world units

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
        if (ToolManager.Instance.CurrentTool == ToolType.Select && ProjectState.Instance.SelectedObjects.Count == 1)
        {
             var obj = ProjectState.Instance.SelectedObjects[0];
             if (obj is LaserBezier bezier)
             {
                 DrawNodeHandles(g, bezier);
             }

             var bounds = GetSelectionBounds();
             if (bounds != null)
             {
                 using var boundaryPen = new Pen(Color.Cyan, 1.0f / _zoom);
                 boundaryPen.DashStyle = DashStyle.Solid;
                 g.DrawRectangle(boundaryPen, bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height);
                 DrawResizeHandles(g, bounds.Value);
             }
        }
        else if (ProjectState.Instance.SelectedObjects.Count > 0)
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

    private void DrawLaserPosition(Graphics g)
    {
        // Draw a crosshair at _laserPosition
        // _laserPosition is in Machine Coordinates (MPos).
        // If Work Area Origin matches logic, we assume MPos matches World Pos for now.
        
        float x = _laserPosition.X;
        float y = _laserPosition.Y;
        
        // Size in pixels
        float size = 10.0f / _zoom;
        
        using var pen = new Pen(Color.Red, 2.0f / _zoom);
        // X
        g.DrawLine(pen, x - size, y - size, x + size, y + size);
        g.DrawLine(pen, x - size, y + size, x + size, y - size);
        
        // Circle
        g.DrawEllipse(pen, x - size, y - size, size * 2, size * 2);
    }

    private void DrawRulerOverlay(Graphics g)
    {
        if (ToolManager.Instance.CurrentTool == ToolType.Ruler && _isMeasuring)
        {
             using var pen = new Pen(Color.Red, 2.0f / _zoom);
             pen.DashStyle = DashStyle.Solid;
             pen.EndCap = LineCap.ArrowAnchor;
             pen.StartCap = LineCap.DiamondAnchor;

             g.DrawLine(pen, _measureStart, _measureEnd);
             
             // Draw Text
             float dist = (float)Math.Sqrt(Math.Pow(_measureEnd.X - _measureStart.X, 2) + Math.Pow(_measureEnd.Y - _measureStart.Y, 2));
             PointF mid = new PointF((_measureStart.X + _measureEnd.X)/2, (_measureStart.Y + _measureEnd.Y)/2);
             
             string text = $"{dist:F1} mm";
             
             // Invert zoom for font size to keep it readable
             // And we must unflip the Y axis for text drawing
             float fontSize = 12.0f / _zoom;
             if (fontSize < 0.1f) fontSize = 0.1f; // Safety
             
             using var font = new Font("Arial", fontSize);
             using var bgBrush = new SolidBrush(Color.FromArgb(180, Color.White));
             using var textBrush = new SolidBrush(Color.DarkBlue);
             
             // Calculate size and position
             var size = g.MeasureString(text, font);
             float tx = mid.X - size.Width / 2;
             float ty = mid.Y - size.Height / 2;

             // Save state to flip back for text
             var state = g.Save();
             g.TranslateTransform(tx, ty);
             g.ScaleTransform(1, -1); // Flip Y back for text
             
             // Draw at 0,0 (relative to Translate)
             g.FillRectangle(bgBrush, 0, 0, size.Width, size.Height);
             g.DrawString(text, font, textBrush, 0, 0);
             
             g.Restore(state);
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
        _currentMouseWorld = worldPos; // Keep raw for selection hit testing
        
        PointF snappedPos = Snap(worldPos);

        // 2. Panning (Right Click)
        if (e.Button == MouseButtons.Right)
        {
            if (ToolManager.Instance.CurrentTool == ToolType.DrawBezier && _currentBezier != null)
            {
                FinalizeBezier();
                return;
            }
            
            _isPanning = true;
            _lastMousePos = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button != MouseButtons.Left) return;

    
    // 3. Tool Creation
    if (ToolManager.Instance.CurrentTool == ToolType.DrawLine)
    {
         var line = new LaserPath { Name = "Line", Position = snappedPos };
         line.Points.Add(snappedPos);
         line.Points.Add(snappedPos);
         ProjectState.Instance.AddObject(line);
         _interactionObject = line;
         _isDragging = true;
         _dragStartPos = snappedPos;
         return;
    }
    else if (ToolType.DrawCircle == ToolManager.Instance.CurrentTool)
    {
         var circle = new LaserCircle { Name = "Circle", Position = snappedPos, Size = new SizeF(0, 0) };
         ProjectState.Instance.AddObject(circle);
         _interactionObject = circle;
         _isDragging = true;
         _dragStartPos = snappedPos;
         return;
    }
    else if (ToolManager.Instance.CurrentTool == ToolType.DrawBox)
    {
         var box = new LaserRectangle { Name = "Rectangle", Position = snappedPos, Size = new SizeF(0, 0) };
         ProjectState.Instance.AddObject(box);
         _interactionObject = box;
         _isDragging = true;
         _dragStartPos = snappedPos;
         return;
    }
    else if (ToolManager.Instance.CurrentTool == ToolType.DrawBezier)
    {
         if (_currentBezier == null)
         {
              _currentBezier = new LaserBezier { Name = "Bezier" };
              _currentBezier.Points.Add(snappedPos);
              ProjectState.Instance.AddObject(_currentBezier);
         }
         else
         {
              // Add new segment (C1, C2, End)
              
              // Snap to Start if close (Closing the loop)
              if (_currentBezier.Points.Count > 0)
              {
                  var start = _currentBezier.Points[0];
                  float distSq = (snappedPos.X - start.X) * (snappedPos.X - start.X) + (snappedPos.Y - start.Y) * (snappedPos.Y - start.Y);
                  float snapThresh = 15.0f / _zoom; 
                  if (distSq < snapThresh * snapThresh)
                  {
                      snappedPos = start;
                  }
              }

              var last = _currentBezier.Points.Last();
              float dx = snappedPos.X - last.X;
              float dy = snappedPos.Y - last.Y;
              // Auto-calculated smooth control points
              _currentBezier.Points.Add(new PointF(last.X + dx * 0.33f, last.Y + dy * 0.33f)); // C1
              _currentBezier.Points.Add(new PointF(last.X + dx * 0.66f, last.Y + dy * 0.66f)); // C2
              _currentBezier.Points.Add(snappedPos); // End
              
              _currentBezier.UpdateBounds();
              Invalidate();
         }
         return;
    }
    else if (ToolManager.Instance.CurrentTool == ToolType.Text)
    {
        // Use TextEditorForm
        string val = "Text";
        string fontName = "Arial";
        float fontSize = 20f;

        using (var form = new TextEditorForm(val, fontName, fontSize))
        {
             if (form.ShowDialog() == DialogResult.OK)
             {
                 val = form.TextValue;
                 fontName = form.FontName;
                 fontSize = form.FontSize;
             }
             else
             {
                 return; // Cancelled
             }
        }

        if (string.IsNullOrWhiteSpace(val)) return;

        var t = new LaserText();
        t.Text = val;
        t.FontName = fontName;
        t.FontSize = fontSize;
        t.Position = snappedPos;
        // Size will be calculated on Draw or we set a default?
        // Let's force a measure? Or let Draw handle it.
        // Draw calculates Size if it changes? 
        // LaserText.Draw updates .Size property.
        // Issue: Selection Box needs Size immediately for HitTest/Draw Highlight.
        // We can create a temporary bitmap to measure.
        using (var tmpBmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(tmpBmp))
        using (var f = new Font(t.FontName, t.FontSize))
        {
             t.Size = g.MeasureString(t.Text, f);
        }

        ProjectState.Instance.AddObject(t);
        
        // Auto-select
        ProjectState.Instance.SelectedObjects = new List<LaserObject> { t };
        
        // Switch back to select for convenience
        ToolManager.Instance.SetTool(ToolType.Select);
        
        Invalidate();
        return;
    }    if (ToolManager.Instance.CurrentTool == ToolType.Ruler)
        {
             _isMeasuring = true;
             _measureStart = worldPos;
             _measureEnd = worldPos;
             Invalidate();
             return;
        }

        // 3b. Click To Move
        if (ToolManager.Instance.CurrentTool == ToolType.ClickToMove)
        {
             // Send Jog Command
             // G0 or $J? 
             // Use $J for jogging logic if supported, or direct G0 if state allows.
             // Usually Click-To-Move implies "Jog Here".
             // $J=G90 X... Y... F...
             
             // Check if machine is Idle or Jogging
             if (SerialInterface.Instance.IsConnected)
             {
                 // We need Feed Rate. Let's use a default or fetch from UI?
                 // For now hardcode or use safe default.
                 int feed = 1000; // mm/min
                 string cmd = $"$J=G90 X{snappedPos.X:F3} Y{snappedPos.Y:F3} F{feed}";
                 SerialInterface.Instance.Write(cmd + "\n");
             }
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
                _dragStartPos = snappedPos; // Snap start of resize too? Maybe or maybe raw? Resizing logic snaps Current.
                                            // Ideally we want Consistent Logic. 
                                            // If we snap Current, we should snap Start to avoid jumps?
                                            // But Handle might not be on grid.
                                            // Let's use Raw for Start? No, UpdateResize uses Absolute position.
                                            // So _dragStartPos is not used for Resize delta.
                                            // It is used for hit test though.
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
                        newSelection.Add(hitObj);
                        ProjectState.Instance.SelectedObjects = newSelection;
                        // Prepare for move
                        _interactionObject = hitObj;
                        _isMoving = true;
                        _dragStartPos = snappedPos;
                        _moveStartPos = snappedPos;
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
                        _dragStartPos = snappedPos;
                        _moveStartPos = snappedPos;
                    }
                    else
                    {
                        // Select ONLY this object
                        ProjectState.Instance.SelectedObjects = new List<LaserObject> { hitObj };
                        _interactionObject = hitObj;
                        _isMoving = true;
                        _dragStartPos = snappedPos;
                        _moveStartPos = snappedPos;
                    }
                }
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.Rotate)
            {
                 if (ProjectState.Instance.SelectedObjects.Count > 0)
                 {
                     var b = GetSelectionBounds();
                     if (b != null)
                     {
                         _rotateCenter = new PointF(b.Value.Left + b.Value.Width/2, b.Value.Top + b.Value.Height/2);
                         _rotateStartAngle = (float)(Math.Atan2(worldPos.Y - _rotateCenter.Y, worldPos.X - _rotateCenter.X) * 180.0 / Math.PI);
                         SnapshotSelection();
                         _isRotating = true;
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
    private LaserBezier? _currentBezier; // For multi-step creation

    private void FinalizeBezier()
    {
        if (_currentBezier != null)
        {
            if (_currentBezier.Points.Count < 4) // Minimum 1 segment
            {
                ProjectState.Instance.RemoveObject(_currentBezier);
            }
            else
            {
                 // Done
            }
            _currentBezier = null;
            Invalidate();
        }
    }
    private bool _isDragging = false; // For creation
    private bool _isMoving = false; // For moving existing objects
    private PointF _dragStartPos; // Used as "Last Mouse Pos" for moving
    
    // Ruler
    private bool _isMeasuring = false;
    private PointF _measureStart;
    private PointF _measureEnd;
    
    private long _lastUpdateTicks = 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointF worldPos = ScreenToWorld(e.Location);
        _currentMouseWorld = worldPos; // Raw for some interactions? 
                                       // Actually, effective mouse world should be Snapped for creation/move.
                                       // But for selection hit testing it should be raw.
                                       
        PointF effectivePos = Snap(worldPos);

        if (_isPanning)
        {
            float dx = e.X - _lastMousePos.X;
            float dy = e.Y - _lastMousePos.Y;
            _panOffset.X += dx;
            _panOffset.Y += dy;
            _lastMousePos = e.Location;
            Invalidate();
            // Fire event for raw mouse move too if needed?
            // Usually we want World Coordinates.
            MousePositionChanged?.Invoke(ScreenToWorld(e.Location));
            return;
        }

        // Fire Mouse Position Event (Throttled)
        long now = DateTime.Now.Ticks;
        if (now - _lastUpdateTicks > 500000) // 50ms
        {
             MousePositionChanged?.Invoke(worldPos);
        }

        // 2. Resizing
        if (_isResizing)
        {
            UpdateResize(effectivePos); // Snap resize handle
            return;
        }

        if (_isRotating)
        {
            float currentAngle = (float)(Math.Atan2(worldPos.Y - _rotateCenter.Y, worldPos.X - _rotateCenter.X) * 180.0 / Math.PI);
            float deltaAngle = currentAngle - _rotateStartAngle;
            
            foreach(var kvp in _initialStates)
            {
                var obj = kvp.Key;
                var init = kvp.Value;
                
                // 1. Rotate Orientation
                obj.Rotation = init.Rotation + deltaAngle;
                
                // 2. Orbit Position
                float rx = init.Pos.X - _rotateCenter.X;
                float ry = init.Pos.Y - _rotateCenter.Y;
                
                float rad = deltaAngle * (float)Math.PI / 180f;
                float c = (float)Math.Cos(rad);
                float s = (float)Math.Sin(rad);
                
                float nx = rx * c - ry * s;
                float ny = rx * s + ry * c;
                
                obj.Position = new PointF(_rotateCenter.X + nx, _rotateCenter.Y + ny);
                
                if (obj is LaserPath p && init.Points != null)
                {
                    for(int i=0; i<p.Points.Count; i++)
                    {
                         float px = init.Points[i].X - _rotateCenter.X;
                         float py = init.Points[i].Y - _rotateCenter.Y;
                         p.Points[i] = new PointF(_rotateCenter.X + px * c - py * s, _rotateCenter.Y + px * s + py * c);
                    }
                }
                else if (obj is LaserBezier b && init.Points != null)
                {
                    for(int i=0; i<b.Points.Count; i++)
                    {
                         float px = init.Points[i].X - _rotateCenter.X;
                         float py = init.Points[i].Y - _rotateCenter.Y;
                         b.Points[i] = new PointF(_rotateCenter.X + px * c - py * s, _rotateCenter.Y + px * s + py * c);
                    }
                    b.UpdateBounds();
                }
            }
            Invalidate();
            
            // Fire Mouse Position Event (Throttled)
            long nowMove = DateTime.Now.Ticks;
            if (nowMove - _lastUpdateTicks > 500000) // 50ms
            {
                 // Update property grid live? Maybe too heavy logic 
                 // MainForm.Instance.UpdateSelectedObjects(false);
                 _lastUpdateTicks = nowMove;
            }
            return;
        }

        // 3. Moving Objects
        if (_isMoving && _interactionObject != null)
        {
            Cursor = Cursors.SizeAll;
            // Calculate delta based on Effective Positions to ensure we move in steps
            float dx = effectivePos.X - Snap(_dragStartPos).X; // Delta from Snapped Start to Snapped Current
            float dy = effectivePos.Y - Snap(_dragStartPos).Y;
            
            // If we didn't snap _dragStartPos at global level, we might get offsets.
            // Best practice: When starting drag, we record the "Anchor" relative to Object?
            // Or just Delta.
            // If I click at 10.5 and move to 11.5 (Snap=1.0). Snap(10.5)=11. Snap(11.5)=12. dx=1.
            // If I click at 10.1 and move to 10.9. Snap(10)=10. Snap(11)=11. dx=1. Correct.
            
            float incDx = effectivePos.X - _dragStartPos.X; // _dragStartPos here acts as "Last Pos"
            float incDy = effectivePos.Y - _dragStartPos.Y;
            
            if (Math.Abs(incDx) > 0.0001 || Math.Abs(incDy) > 0.0001)
            {
                foreach(var obj in ProjectState.Instance.SelectedObjects)
                {
                    MoveObject(obj, incDx, incDy);
                }
                _dragStartPos = effectivePos; // Update to the position we moved TO
            }
            
            Invalidate();
            // Fire Mouse Position Event (Throttled)
            long nowMove = DateTime.Now.Ticks;
            if (nowMove - _lastUpdateTicks > 500000) // 50ms
            {
                 MainForm.Instance.UpdateSelectedObjects(false);
                 _lastUpdateTicks = nowMove;
            }

            return;
        }

        // 4. Creating Objects (Dragging)
        if (_isDragging && _interactionObject != null)
        {
            PointF start = Snap(_dragStartPos); // This might have been raw on MouseDown?
            // If we want start to be snapped, we should have snapped it on MouseDown or here.
            // Let's assume DragStartPos IS snapped if we handled it in MouseDown? 
            // Or we just map it here.
            
            if (ToolManager.Instance.CurrentTool == ToolType.DrawBox || ToolManager.Instance.CurrentTool == ToolType.DrawCircle)
            {
                float w = Math.Abs(effectivePos.X - start.X);
                float h = Math.Abs(effectivePos.Y - start.Y);

                if (Control.ModifierKeys == Keys.Control)
                {
                    float max = Math.Max(w, h);
                    w = max;
                    h = max;
                }

                // Determine Top-Left based on drag direction
                float x = (effectivePos.X < start.X) ? start.X - w : start.X;
                float y = (effectivePos.Y < start.Y) ? start.Y - h : start.Y;

                _interactionObject.Position = new PointF(x, y);
                _interactionObject.Size = new SizeF(w, h);
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.DrawLine)
            {
                if (_interactionObject is LaserPath path && path.Points.Count >= 2)
                {
                    path.Points[0] = start; // Ensure start point is snapped too if we want
                    path.Points[1] = effectivePos;
                }
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.DrawBezier)
            {
                 // No drag for new click-style bezier yet
                 // Or we could preview the line to the mouse?
            }
            Invalidate();
            return;
        }
        
        if (_isMeasuring)
        {
             _measureEnd = worldPos;
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
            // If we were panning, we are done.
            return;
        }

        if (e.Button == MouseButtons.Left && ToolManager.Instance.CurrentTool == ToolType.Text)
        {
            // Cancel? No, just finish.
            // Actually, preventing text creation on drag end.
            // For now, text creation is on MouseDown, so this might be redundant unless we want drag-to-size.
            // Let's assume click-only for now.
        }

        if (_isRotating)
        {
            _isRotating = false;
            
             var newStates = new Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)>();
             foreach(var obj in ProjectState.Instance.SelectedObjects)
             {
                List<PointF>? pts = null;
                float fSize = 0;
                if (obj is LaserPath p) pts = new List<PointF>(p.Points);
                else if (obj is LaserBezier b) pts = new List<PointF>(b.Points);
                
                if (obj is LaserText t) fSize = t.FontSize;
                
                newStates[obj] = (obj.Position, obj.Size, pts, fSize, obj.Rotation);
             }
             
             // Reuse ResizeCommand (renamed conceptually to TransformCommand) 
             // as it handles all state we care about.
             CommandManager.Instance.Execute(new ResizeCommand(_initialStates, newStates));
             
             MainForm.Instance.UpdateSelectedObjects();
             _initialStates.Clear();
             ResetInteractionState();
             return;
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

        if (_isMeasuring)
        {
             _isMeasuring = false;
             Invalidate();
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
                
                MainForm.Instance.UpdateSelectedObjects(); // Final update
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
            
            var newStates = new Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)>();
            foreach (var obj in ProjectState.Instance.SelectedObjects)
            {
                List<PointF>? pts = null;
                float fSize = 0;
                if (obj is LaserPath p) pts = new List<PointF>(p.Points);
                else if (obj is LaserBezier b) pts = new List<PointF>(b.Points);
                
                if (obj is LaserText t) fSize = t.FontSize;
                
                newStates[obj] = (obj.Position, obj.Size, pts, fSize, obj.Rotation);
            }
            
            var cmd = new ResizeCommand(_initialStates, newStates);
            CommandManager.Instance.Execute(cmd);
            MainForm.Instance.UpdateSelectedObjects(); // Final update
            
            _initialStates.Clear();
        }
        
        // Final safety reset
        ResetInteractionState();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (ToolManager.Instance.CurrentTool != ToolType.Select) return;

        PointF worldPos = ScreenToWorld(e.Location);
        float hitTolerance = 8.0f / _zoom;

        var hitObj = ProjectState.Instance.Objects.Reverse().FirstOrDefault(o => o.HitTest(worldPos, hitTolerance));

        if (hitObj is LaserText textObj)
        {
            using (var form = new TextEditorForm(textObj.Text, textObj.FontName, textObj.FontSize))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    textObj.Text = form.TextValue;
                    textObj.FontName = form.FontName;
                    textObj.FontSize = form.FontSize;

                    using (var tmpBmp = new Bitmap(1, 1))
                    using (var g = Graphics.FromImage(tmpBmp))
                    using (var f = new Font(textObj.FontName, textObj.FontSize))
                    {
                         textObj.Size = g.MeasureString(textObj.Text, f);
                    }

                    Invalidate();
                }
            }
        }
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

    private void DrawNodeHandles(Graphics g, LaserBezier b)
    {
        float size = 6.0f / _zoom;
        using var brushAnchor = new SolidBrush(Color.Blue);
        using var brushControl = new SolidBrush(Color.LightBlue);
        using var penLine = new Pen(Color.Gray, 1.0f / _zoom);
        
        for (int i = 0; i < b.Points.Count; i++)
        {
            var p = b.Points[i];
            bool isAnchor = (i % 3 == 0);
            
            // Draw connection lines
            if (!isAnchor)
            {
                // Connect to anchor
                // if i%3 == 1, anchor is i-1
                // if i%3 == 2, anchor is i+1
                int anchorIdx = (i % 3 == 1) ? i - 1 : i + 1;
                if (anchorIdx >= 0 && anchorIdx < b.Points.Count)
                {
                    g.DrawLine(penLine, p, b.Points[anchorIdx]);
                }
            }

            var brush = isAnchor ? brushAnchor : brushControl;
            g.FillEllipse(brush, p.X - size/2, p.Y - size/2, size, size);
        }
    }
    
    private int _dragHandleIndex = -1; // -1 none, 0-7 handles
    
    private bool _isResizing = false;
    private bool _isRotating = false;
    private PointF _rotateCenter;
    private float _rotateStartAngle;

    private RectangleF? _initialGroupBounds;
    private Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)> _initialStates = new();

    private void SnapshotSelection()
    {
        _initialStates.Clear();
        foreach (var obj in ProjectState.Instance.SelectedObjects)
        {
            List<PointF>? pts = null;
            float fSize = 0;
            if (obj is LaserPath p) pts = new List<PointF>(p.Points);
            else if (obj is LaserBezier b) pts = new List<PointF>(b.Points);
            
            if (obj is LaserText t) fSize = t.FontSize;
            
            _initialStates[obj] = (obj.Position, obj.Size, pts, fSize, obj.Rotation);
        }
    }

    private void UpdateResize(PointF currentPos)
    {
        // 1. Node Editing
        if (_dragHandleIndex >= 100)
        {
             if (ProjectState.Instance.SelectedObjects.Count == 1 && ProjectState.Instance.SelectedObjects[0] is LaserBezier bezier)
             {
                 int idx = _dragHandleIndex - 100;
                 if (idx >= 0 && idx < bezier.Points.Count)
                 {
                     // Snap Ends together
                     if (idx == 0 || idx == bezier.Points.Count - 1)
                     {
                         int otherIdx = (idx == 0) ? bezier.Points.Count - 1 : 0;
                         var other = bezier.Points[otherIdx];
                         float distSq = (currentPos.X - other.X) * (currentPos.X - other.X) + (currentPos.Y - other.Y) * (currentPos.Y - other.Y);
                         float snapThresh = 15.0f / _zoom;
                         if (distSq < snapThresh * snapThresh)
                         {
                             currentPos = other;
                         }
                     }
                     
                     bezier.Points[idx] = currentPos;
                     bezier.UpdateBounds();
                     Invalidate();
                 }
             }
             return;
        }

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
        
            // Validate inverted bounds
        float newW = newR - newL;
        float newH = newB - newT;
        
        // Size Snapping
        if (IsSnappingEnabled)
        {
            float interval = SnapInterval;
            if (Math.Abs(interval) > 0.001f)
            {
                newW = (float)Math.Round(newW / interval) * interval;
                newH = (float)Math.Round(newH / interval) * interval;
                if (newW < interval) newW = interval; // Min size?
                if (newH < interval) newH = interval;
            }
        }

        // Determine Scale Factors
        float scaleX = (b.Width == 0) ? 1 : newW / b.Width;
        float scaleY = (b.Height == 0) ? 1 : newH / b.Height;

        // Aspect Ratio Lock (Ctrl)
        if (Control.ModifierKeys == Keys.Control)
        {
            // Determine if corner or side
            bool isCorner = (_dragHandleIndex == 0 || _dragHandleIndex == 2 || _dragHandleIndex == 4 || _dragHandleIndex == 6);
            bool isTopBottom = (_dragHandleIndex == 1 || _dragHandleIndex == 5);
            bool isLeftRight = (_dragHandleIndex == 3 || _dragHandleIndex == 7);

            float lockedScale = 1.0f;

            if (isCorner)
            {
                // Take the larger scale change magnitude
                lockedScale = (Math.Abs(scaleX) > Math.Abs(scaleY)) ? scaleX : scaleY; 
                float mag = Math.Max(Math.Abs(scaleX), Math.Abs(scaleY));
                scaleX = Math.Sign(scaleX) * mag;
                scaleY = Math.Sign(scaleY) * mag;
            }
            else if (isTopBottom)
            {
                scaleX = Math.Abs(scaleY); // Maintain X sign but take Y magnitude? No, just match Y scale.
            }
            else if (isLeftRight)
            {
                scaleY = Math.Abs(scaleX);
            }
            
            // Recalculate Bounds
            float finalW = b.Width * scaleX;
            float finalH = b.Height * scaleY;
            
            // Re-apply to L/T/R/B based on Anchor
            if (_dragHandleIndex == 0) { newL = b.Right - finalW; newT = b.Bottom - finalH; }
            
            // Handle 1 (T): Bottom fixed. Center X.
            if (_dragHandleIndex == 1) 
            { 
                 newT = b.Bottom - finalH; 
                 float cx = (b.Left + b.Right) / 2;
                 newL = cx - finalW / 2;
                 newR = cx + finalW / 2;
            }
            
            // Handle 2 (TR): Left/Bottom fixed.
            if (_dragHandleIndex == 2) { newL = b.Left; newT = b.Bottom - finalH; }
            
            // Handle 3 (R): Left fixed. Center Y.
            if (_dragHandleIndex == 3)
            {
                newL = b.Left;
                float cy = (b.Top + b.Bottom) / 2;
                newT = cy - finalH / 2;
            }
            
            // Handle 4 (BR): Left/Top fixed.
            if (_dragHandleIndex == 4) { newL = b.Left; newT = b.Top; }
            
            // Handle 5 (B): Top fixed. Center X.
            if (_dragHandleIndex == 5)
            {
                newT = b.Top;
                float cx = (b.Left + b.Right) / 2;
                newL = cx - finalW / 2;
            }

            // Handle 6 (BL): Right/Top fixed.
            if (_dragHandleIndex == 6) { newL = b.Right - finalW; newT = b.Top; }
            
            // Handle 7 (L): Right fixed. Center Y.
            if (_dragHandleIndex == 7)
            {
                newL = b.Right - finalW;
                float cy = (b.Top + b.Bottom) / 2;
                newT = cy - finalH / 2;
            }
        }
        
        // Apply to objects
        foreach (var kvp in _initialStates)
        {
            var obj = kvp.Key;
            var init = kvp.Value;
            
            // Relative position to group origin (TopLeft of group)
            float relX = init.Pos.X - b.Left;
            float relY = init.Pos.Y - b.Top;
            
            float objX = newL + relX * scaleX;
            float objY = newT + relY * scaleY;
            float objW = init.Size.Width * scaleX;
            float objH = init.Size.Height * scaleY;

            // Normalize negative size for Objects that require positive size (Rect, Image)
            // Path points handle themselves via transform usually, but LaserObject.Size should be positive.
            if (objW < 0) { objX += objW; objW = -objW; }
            if (objH < 0) { objY += objH; objH = -objH; }

            obj.Position = new PointF(objX, objY);
            obj.Size = new SizeF(objW, objH);
            
            if (obj is LaserPath p && init.Points != null)
            {
                for(int i=0; i<p.Points.Count; i++)
                {
                    float px = init.Points[i].X - b.Left;
                    float py = init.Points[i].Y - b.Top;
                    p.Points[i] = new PointF(newL + px * scaleX, newT + py * scaleY);
                }
            }
            else if (obj is LaserBezier bez && init.Points != null)
            {
                // Resize all points
                for(int i=0; i<bez.Points.Count && i<init.Points.Count; i++)
                {
                    float px = init.Points[i].X - b.Left;
                    float py = init.Points[i].Y - b.Top;
                    bez.Points[i] = new PointF(newL + px * scaleX, newT + py * scaleY);
                }
                bez.UpdateBounds();
            }
            else if (obj is LaserText txt)
            {
                 // Update Font Size based on scaleY (Height controls font size)
                 // Ensure we don't flip font size if scaleY is negative (flipped)
                 float newFs = init.FontSize * Math.Abs(scaleY);
                 if (newFs < 1f) newFs = 1f;
                 txt.FontSize = newFs;
            }
        }
        Invalidate();
        
        // Throttled UI Update
        long nowResize = DateTime.Now.Ticks;
        if (nowResize - _lastUpdateTicks > 500000)
        {
            MainForm.Instance.UpdateSelectedObjects(false); 
            _lastUpdateTicks = nowResize;
        }
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
         else if (obj is LaserBezier bezier)
         {
             for(int i=0; i<bezier.Points.Count; i++)
             {
                 bezier.Points[i] = new PointF(bezier.Points[i].X + dx, bezier.Points[i].Y + dy);
             }
             // Ensure Position is also updated (usually Move logic expects Obj Position to be updated)
             // But LaserBezier.Position is derived. 
             // Wait, LaserObject.Position is a property.
             // If we move points, Position (Bounds) should change.
             bezier.Position = new PointF(bezier.Position.X + dx, bezier.Position.Y + dy);
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
         // 1. Node Handles (Index >= 100)
         if (ProjectState.Instance.SelectedObjects.Count == 1 && ProjectState.Instance.SelectedObjects[0] is LaserBezier bezier)
         {
             float size = 8.0f / _zoom;
             for(int i=0; i<bezier.Points.Count; i++)
             {
                 var p = bezier.Points[i];
                 if (new RectangleF(p.X - size/2, p.Y - size/2, size, size).Contains(pos))
                 {
                     return 100 + i;
                 }
             }
         }

         // 2. Resize Handles
         var bounds = GetSelectionBounds();
         if (bounds == null) return -1;
         
         var handles = GetHandlePositions(bounds.Value);
         float handleSize = 8.0f / _zoom;
         
         for(int i=0; i<handles.Length; i++)
         {
             var r = new RectangleF(handles[i].X - handleSize/2, handles[i].Y - handleSize/2, handleSize, handleSize);
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
        float y = (screenPoint.Y - Height / 2f - _panOffset.Y) / -_zoom; // Note negative zoom
        return new PointF(x, y);
    }
}
