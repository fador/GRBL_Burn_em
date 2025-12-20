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

public partial class WorkbenchControl : Control
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
    
    // Fields for Interaction (needed by partials)
    private PointF _currentMouseWorld;
    private bool _isSelecting = false;
    private PointF _moveStartPos; // To calculate total delta for Command
    private LaserObject? _interactionObject;
    private LaserBezier? _currentBezier; // For multi-step creation
    private bool _isDragging = false; // For creation
    private bool _isMoving = false; // For moving existing objects
    private PointF _dragStartPos; // Used as "Last Mouse Pos" for moving
    
    // Ruler
    private bool _isMeasuring = false;
    private PointF _measureStart;
    private PointF _measureEnd;
    
    private long _lastUpdateTicks = 0;
    
    private int _dragHandleIndex = -1; // -1 none, 0-7 handles
    
    private bool _isResizing = false;
    private bool _isRotating = false;
    private PointF _rotateCenter;
    private float _rotateStartAngle;

    private RectangleF? _initialGroupBounds;
    private Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)> _initialStates = new();


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
