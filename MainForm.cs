using laser_gui_test.Controls;
using laser_gui_test.Data;
using laser_gui_test.Tools;
using System.ComponentModel;
using laser_gui_test.Data.Commands;
using System.Linq;
using laser_gui_test.Forms;
using laser_gui_test.Data.Generators;

namespace laser_gui_test;

public partial class MainForm : Form
{
    private static MainForm? _instance;
    public static MainForm Instance => _instance ??= new MainForm();

    private WorkbenchControl _workbench = null!;
    private TabControl _rightTabControl = null!;
    private DataGridView _objectList = null!;
    private DataGridView _layerList = null!;
    private FlowLayoutPanel _layerPanel = null!;
    private FlowLayoutPanel _toolsPanel = null!;
    private GroupBox _controlPanel = null!;

    private bool _isUpdatingSelection = false;



    private bool _isUpdatingUI = false;

    private JobRunner _jobRunner = new JobRunner();
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _lblStatusConnection = null!;
    private ToolStripStatusLabel _lblStatusState = null!;
    private ToolStripStatusLabel _lblStatusPos = null!;
    private ToolStripProgressBar _progressBar = null!;
    
    // Top Toolbar Controls
    private ToolStripLabel _lblMousePos = null!;
    private NumericUpDown _nudPosX = null!;
    private NumericUpDown _nudPosY = null!;
    private NumericUpDown _nudSizeW = null!;
    private NumericUpDown _nudSizeH = null!;
    private ToolStripLabel _lblLayerInfo = null!;
    
    // Text Toolbar Controls
    private ToolStripTextBox _txtContent = null!;
    private ToolStripComboBox _cmbFont = null!;
    private NumericUpDown _nudFontSize = null!;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
             // Save View Settings
             AppConfiguration.Instance.LastPanX = _workbench.PanOffset.X;
             AppConfiguration.Instance.LastPanY = _workbench.PanOffset.Y;
             
             // Save Window State
             if (WindowState == FormWindowState.Normal)
             {
                 AppConfiguration.Instance.WindowX = Location.X;
                 AppConfiguration.Instance.WindowY = Location.Y;
                 AppConfiguration.Instance.WindowWidth = Size.Width;
                 AppConfiguration.Instance.WindowHeight = Size.Height;
                 AppConfiguration.Instance.WindowState = (int)FormWindowState.Normal;
             }
             else
             {
                 AppConfiguration.Instance.WindowState = (int)WindowState;
                 if (WindowState == FormWindowState.Maximized)
                 {
                     AppConfiguration.Instance.WindowX = RestoreBounds.X;
                     AppConfiguration.Instance.WindowY = RestoreBounds.Y;
                     AppConfiguration.Instance.WindowWidth = RestoreBounds.Width;
                     AppConfiguration.Instance.WindowHeight = RestoreBounds.Height;
                 }
             }
             
             AppConfiguration.Instance.Save();
        }
        catch(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
        }

        base.OnFormClosing(e);
    }

    public MainForm()
    {
        InitializeComponent();
        SetupCustomLayout();
    }

    private void SetupCustomLayout()
    {
        try
        {
            this.Text = "Laser Control Software";
            
            // Initialize Workbench EARLY to prevent null references in event handlers
            _workbench = new WorkbenchControl
            {
                Dock = DockStyle.Fill
            };
            
            // Restore Window Settings
            var cfg = AppConfiguration.Instance;
            if (cfg.WindowX != -1 && cfg.WindowWidth > 0 && cfg.WindowHeight > 0)
            {
                this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(cfg.WindowX, cfg.WindowY);
            this.Size = new Size(cfg.WindowWidth, cfg.WindowHeight);
            
            // Validate Screen - ensure at least some part is visible
            bool visible = Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(this.DesktopBounds));
            if (!visible)
            {
                this.StartPosition = FormStartPosition.WindowsDefaultLocation;
                this.Size = new Size(1200, 800);
            }

            if (cfg.WindowState == (int)FormWindowState.Maximized)
            {
                this.WindowState = FormWindowState.Maximized;
            }
        }
        else
        {
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        // 1. Menu Strip
        // 1. Menu Strip
        var menuStrip = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("File");

        // Shared Actions
        Action applyMask = () => 
        {
            var sel = _objectList.SelectedRows;
            if (sel.Count != 2) 
            {
                MessageBox.Show("Please select exactly one Image and one Shape (Circle/Rectangle) to create a mask.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            
            var obj1 = ProjectState.Instance.Objects[sel[0].Index];
            var obj2 = ProjectState.Instance.Objects[sel[1].Index];
            
            LaserImage? img = obj1 as LaserImage ?? obj2 as LaserImage;
            LaserObject? shape = (obj1 is LaserCircle || obj1 is LaserRectangle) ? obj1 :
                                 (obj2 is LaserCircle || obj2 is LaserRectangle) ? obj2 : null;
                                 
            if (img != null && shape != null && img != shape)
            {
                 if (img.MaskId == shape.Id)
                 {
                     img.MaskId = Guid.Empty;
                 }
                 else
                 {
                     img.MaskId = shape.Id;
                 }
                 _workbench.Invalidate();
            }
            else
            {
                MessageBox.Show("Selection must include one Image and one Shape.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        
        fileMenu.DropDownItems.Add("New", null, (s, e) => 
        {
            ProjectState.Instance.Objects.Clear();
            CommandManager.Instance.Clear();
            _workbench.Invalidate();
        });

        fileMenu.DropDownItems.Add("Open", null, (s, e) => 
        {
            using var ofd = new OpenFileDialog { Filter = "Laser Project|*.json" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                 ProjectSerializer.Load(ofd.FileName);
                 CommandManager.Instance.Clear();
                 InitializeLayers(); 
                 _layerList.Refresh();
                 _workbench.Invalidate();
            }
        });

        fileMenu.DropDownItems.Add("Save", null, (s, e) => 
        {
            using var sfd = new SaveFileDialog { Filter = "Laser Project|*.json" };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                ProjectSerializer.Save(sfd.FileName);
            }
        });

        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Import File", null, (s, e) => ImportFile());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Options", null, (s, e) => 
        {
            using var dlg = new OptionsForm();
            dlg.ShowDialog();
        });
        menuStrip.Items.Add(fileMenu);

        var toolMenu = new ToolStripMenuItem("Tool");
        toolMenu.DropDownItems.Add("Mask Image with Shape", null, (s, e) => applyMask());
        menuStrip.Items.Add(toolMenu);

        this.MainMenuStrip = menuStrip;
        this.Controls.Add(menuStrip);

        // 1.5 Top Toolbar (Two Rows)
        InitializeTopToolbar();

        // 2. Main Container (Splits Left Tools and Rest)
        // Actually, let's use Docking properly.
        
        // Bottom: Layer Select
        _layerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = Color.LightGray,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(5)
        };
        InitializeLayers();
        this.Controls.Add(_layerPanel);

        // Status Strip
        _statusStrip = new StatusStrip();
        _lblStatusConnection = new ToolStripStatusLabel("Disconnected") { ForeColor = Color.Red };
        _lblStatusState = new ToolStripStatusLabel("State: Unknown");
        _lblStatusPos = new ToolStripStatusLabel("Pos: 0,0");
        _progressBar = new ToolStripProgressBar { Width = 100, Visible = false };
        
        _statusStrip.Items.AddRange(new ToolStripItem[] { _lblStatusConnection, new ToolStripSeparator(), _lblStatusState, new ToolStripSeparator(), _lblStatusPos, new ToolStripSeparator(), _progressBar });
        this.Controls.Add(_statusStrip);

        // Left: Tools
        _toolsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 60,
            BackColor = Color.Gray,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(5)
        };
        InitializeTools();
        this.Controls.Add(_toolsPanel);

        // Right: Object List & Controls
        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Right,
            Width = 300,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 100,
            //FixedPanel = FixedPanel.Panel2 // Keep Control Panel (Bottom) fixed size when resizing form
        };

        // Object List & Layers Tab Control
        _rightTabControl = new TabControl { Dock = DockStyle.Fill };
        
        // Tab 1: Objects
        var tabObjects = new TabPage("Objects");
        _objectList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = ProjectState.Instance.Objects,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            MultiSelect = true,
            AllowDrop = true // Enable Drag/Drop
        };

        // Context Menu
        var ctxMenu = new ContextMenuStrip();
        var itemMask = new ToolStripMenuItem("Mask Image with Shape");
        itemMask.Click += (s, e) => applyMask();
        ctxMenu.Opening += (s, e) => 
        {
            var sel = _objectList.SelectedRows;
            itemMask.Enabled = false;
            if (sel.Count == 2)
            {
                var obj1 = ProjectState.Instance.Objects[sel[0].Index];
                var obj2 = ProjectState.Instance.Objects[sel[1].Index];
                bool hasImage = obj1 is LaserImage || obj2 is LaserImage;
                bool hasShape = obj1 is LaserCircle || obj1 is LaserRectangle || obj2 is LaserCircle || obj2 is LaserRectangle;
                if (hasImage && hasShape) itemMask.Enabled = true;
            }
        };
        ctxMenu.Items.Add(itemMask);
        _objectList.ContextMenuStrip = ctxMenu;
        
        // Wire Drag/Drop Events
        Rectangle dragBoxFromMouseDown = Rectangle.Empty;
        int rowIndexFromMouseDown = -1;
        int rowIndexOfItemUnderMouseToDrop = -1;

        _objectList.MouseMove += (s, e) => 
        {
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                // If the mouse moves outside the rectangle, start the drag
                if (dragBoxFromMouseDown != Rectangle.Empty && !dragBoxFromMouseDown.Contains(e.X, e.Y))
                {
                    // Proceed with the drag and drop, passing in the list item
                    DragDropEffects dropEffect = _objectList.DoDragDrop(_objectList.Rows[rowIndexFromMouseDown], DragDropEffects.Move);
                }
            }
        };

        _objectList.MouseDown += (s, e) => 
        {
             // Get the index of the item the mouse is below
             rowIndexFromMouseDown = _objectList.HitTest(e.X, e.Y).RowIndex;

             if (rowIndexFromMouseDown != -1)
             {
                 // Remember the point where the mouse down occurred
                 // The DragSize indicates the size that the mouse can move before a drag event should be started
                 Size dragSize = SystemInformation.DragSize;

                 // Create a rectangle using the DragSize, with the MousePosition as the center of the rectangle
                 dragBoxFromMouseDown = new Rectangle(new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)), dragSize);
             }
             else
             {
                 // Reset the rectangle if the mouse is not over an item in the ListBox
                 dragBoxFromMouseDown = Rectangle.Empty;
             }
        };
        
        _objectList.DragOver += (s, e) => 
        {
            e.Effect = DragDropEffects.Move;
        };

        _objectList.DragDrop += (s, e) => 
        {
             // The mouse locations are relative to the screen, so they must be converted to client coordinates
             Point clientPoint = _objectList.PointToClient(new Point(e.X, e.Y));
                 
             // Get the row index of the item the mouse is below
             rowIndexOfItemUnderMouseToDrop = _objectList.HitTest(clientPoint.X, clientPoint.Y).RowIndex;

             // If the drag operation was a move then remove and insert the row
             if (e.Effect == DragDropEffects.Move)
             {
                 if (rowIndexOfItemUnderMouseToDrop < 0) rowIndexOfItemUnderMouseToDrop = _objectList.Rows.Count - 1; // Drop at end if missed
                 
                 // Perform reorder on Data Source
                 var objects = ProjectState.Instance.Objects;
                 if (rowIndexFromMouseDown >= 0 && rowIndexFromMouseDown < objects.Count)
                 {
                     var item = objects[rowIndexFromMouseDown];
                     
                     // Direct swap or move
                     // Remove and Insert
                     if (rowIndexOfItemUnderMouseToDrop != rowIndexFromMouseDown)
                     {
                         objects.RemoveAt(rowIndexFromMouseDown);
                         objects.Insert(rowIndexOfItemUnderMouseToDrop, item);
                         
                         // Select the dropped item
                         _objectList.ClearSelection();
                         _objectList.Rows[rowIndexOfItemUnderMouseToDrop].Selected = true;
                     }
                 }
             }
        };
        _objectList.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "IsEnabled", HeaderText = "On", Width = 30 });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { Name = "LayerName", HeaderText = "Layer", Width = 80, ReadOnly = true });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { Name = "LayerPower", HeaderText = "Pwr%", Width = 40, ReadOnly = true });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { Name = "LayerSpeed", HeaderText = "Spd", Width = 40, ReadOnly = true });
        
        // Order Buttons Toolbar
        var tsOrder = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        var btnUp = new ToolStripButton("▲") { ToolTipText = "Move Up" };
        var btnDown = new ToolStripButton("▼") { ToolTipText = "Move Down" };
        
        btnUp.Click += (s, e) => 
        {
            var sel = _objectList.SelectedRows;
            if (sel.Count == 1)
            {
                int idx = sel[0].Index;
                if (idx > 0)
                {
                    var objects = ProjectState.Instance.Objects;
                    var item = objects[idx];
                    objects.RemoveAt(idx);
                    objects.Insert(idx - 1, item);
                    _objectList.ClearSelection();
                    _objectList.Rows[idx - 1].Selected = true;
                }
            }
        };

        btnDown.Click += (s, e) => 
        {
            var sel = _objectList.SelectedRows;
            if (sel.Count == 1)
            {
                int idx = sel[0].Index;
                var objects = ProjectState.Instance.Objects;
                if (idx < objects.Count - 1)
                {
                    var item = objects[idx];
                    objects.RemoveAt(idx);
                    objects.Insert(idx + 1, item);
                    _objectList.ClearSelection();
                    _objectList.Rows[idx + 1].Selected = true;
                }
            }
        };
        
        tsOrder.Items.Add(btnUp);
        tsOrder.Items.Add(btnDown);

        tabObjects.Controls.Add(_objectList); // Add list first (Fill)
        tabObjects.Controls.Add(tsOrder); // Add toolbar (Top) - Docking order matters?
        // In WinForms, Control added last is at top of Z-order?
        // Docking precedence: The LAST added control with DockStyle.Top is at the TOP-MOST position? 
        // No, typically if Fill is verified, we add Top first, then Fill.
        // Or we add Fill first, but since it fills remaining, if Top is not there yet...
        // Actually: "Controls are docked in reverse Z-order." (Last added is closest to edge?)
        // Let's add ToolStrip FIRST if we want it at the top, or LAST?
        // "The last control added to the Controls collection is the first one docked." 
        // Wait, "The control at the beginning of the Controls collection is docked last." (Z-Order 0 is top).
        // Controls.Add adds to the END of the collection.
        // So `_objectList` is added. Then `tsOrder`.
        // If we add `tsOrder` (Top) second, it will be added to the collection.
        // If `tsOrder` is at generic Z-index 0 (top of stack).
        // Docking: The control with Z-order 0 is docked FIRST?
        // Let's rely on standard practice: Add Top controls, then Fill controls? No, Fill consumes remaining space.
        // To be safe: Add tsOrder (Top), THEN _objectList (Fill).
        
        // So I will change the logic to clear and re-add or just use BringToFront on tsOrder.
        // Actually, replacing content allows me to just add them in correct order.
        
        _rightTabControl.TabPages.Add(tabObjects);

        // Tab 2: Layers
        var tabLayers = new TabPage("Layers");
        _layerList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = ProjectState.Instance.Layers, // BindingList<Layer>
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            MultiSelect = false
        };
        
        _layerList.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "IsVisible", HeaderText = "Vis", Width = 30 });
        _layerList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        
        // Color Column (Owner Draw ideally, but simple button/text for now? Let's use ReadOnly text with BackColor)
        var colColor = new DataGridViewTextBoxColumn { DataPropertyName = "Color", HeaderText = "Color", Width = 40, ReadOnly = true };
        _layerList.Columns.Add(colColor);
        
        _layerList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Speed", HeaderText = "Spd", Width = 50 });
        _layerList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Power", HeaderText = "Pwr%", Width = 50 });
        
        // Mode ComboBox
        var colMode = new DataGridViewComboBoxColumn 
        { 
            DataPropertyName = "Mode", 
            HeaderText = "Mode", 
            Width = 60,
            DataSource = Enum.GetValues(typeof(LayerMode))
        };
        _layerList.Columns.Add(colMode);

        // Layer List Events
        _layerList.CellDoubleClick += (s, e) => 
        {
            if (e.RowIndex < 0) return;
            var layer = ProjectState.Instance.Layers[e.RowIndex];
            
            // Color Picking
            if (e.ColumnIndex == _layerList.Columns[2].Index) // Color
            {
                using var cd = new ColorDialog { Color = layer.Color };
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    layer.Color = cd.Color;
                    _layerList.Refresh();
                    InitializeLayers(); // Update bottom panel
                    _workbench.Invalidate();
                }
            }
        };

        _layerList.CellFormatting += (s, e) => 
        {
            if (e.RowIndex < 0 || e.RowIndex >= ProjectState.Instance.Layers.Count) return;
            var layer = ProjectState.Instance.Layers[e.RowIndex];
             
             // Color Column
             if (e.ColumnIndex == _layerList.Columns[2].Index) // Color defined above
             {
                 e.CellStyle.BackColor = layer.Color;
                 e.CellStyle.SelectionBackColor = layer.Color;
                 e.Value = ""; 
                 e.FormattingApplied = true;
             }
        };
        
        _layerList.CellValueChanged += (s, e) => 
        {
             // Trigger updates
             _workbench.Invalidate();
             InitializeLayers(); // Update bottom buttons
             _objectList.Refresh(); // Update object list layer info
             UpdateSelectedObjects();
        };

        // Suppress DataError for ComboBox binding issues if any
        _layerList.DataError += (s, e) => { e.Cancel = false; };

        tabLayers.Controls.Add(_layerList);
        _rightTabControl.TabPages.Add(tabLayers);

        // Tab 3: Laser Control
        var tabControl = new TabPage("Control");
        var pnlControl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, Padding = new Padding(10) };
        
        // Jogging
        var grpJog = new GroupBox { Text = "Jog", Width = 250, Height = 180 };
        var gridJog = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
        // 0,0  0,1(Up) 0,2
        // 1,0(L) 1,1   1,2(R)
        // 2,0  2,1(Dn) 2,2
        
        var btnYPlus = new Button { Text = "Y+", Dock = DockStyle.Fill };
        var btnYMinus = new Button { Text = "Y-", Dock = DockStyle.Fill };
        var btnXPlus = new Button { Text = "X+", Dock = DockStyle.Fill };
        var btnXMinus = new Button { Text = "X-", Dock = DockStyle.Fill };
        var btnHome = new Button { Text = "H", Dock = DockStyle.Fill, BackColor = Color.LightBlue };
        
        gridJog.Controls.Add(btnYPlus, 1, 0);
        gridJog.Controls.Add(btnXMinus, 0, 1);
        gridJog.Controls.Add(btnHome, 1, 1);
        gridJog.Controls.Add(btnXPlus, 2, 1);
        gridJog.Controls.Add(btnYMinus, 1, 2);
        
        grpJog.Controls.Add(gridJog);
        pnlControl.Controls.Add(grpJog);
        
        // Step Size
        var pnlStep = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        pnlStep.Controls.Add(new Label { Text = "Step (mm):", AutoSize = true, Padding = new Padding(0,5,0,0) });
        var cmbStep = new ComboBox { Width = 60 };
        cmbStep.Items.AddRange(new object[] { "0.1", "1", "10", "100" });
        cmbStep.SelectedIndex = 2; // 10mm
        pnlStep.Controls.Add(cmbStep);
        pnlControl.Controls.Add(pnlStep);
        
        // Feed Rate
        var pnlFeed = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        pnlFeed.Controls.Add(new Label { Text = "Feed (mm/min):", AutoSize = true, Padding = new Padding(0,5,0,0) });
        var numFeed = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = 1000, Width = 60 };
        pnlFeed.Controls.Add(numFeed);
        pnlControl.Controls.Add(pnlFeed);

        // Jog Logic
        Action<string, string> sendJog = (axis, dir) => 
        {
             if (!SerialInterface.Instance.IsConnected) return;
             if (!double.TryParse(cmbStep.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double step)) step = 10;
             double dist = (dir == "-") ? -step : step;
             // $J=G91 X10 F1000
             string cmd = $"$J=G91 {axis}{dist} F{numFeed.Value}";
             SerialInterface.Instance.Write(cmd + "\n");
        };
        
        btnYPlus.Click += (s, e) => sendJog("Y", "+");
        btnYMinus.Click += (s, e) => sendJog("Y", "-");
        btnXPlus.Click += (s, e) => sendJog("X", "+");
        btnXMinus.Click += (s, e) => sendJog("X", "-");
        btnHome.Click += (s, e) => SerialInterface.Instance.Write("$H\n");

        // Fire Laser
        var grpFire = new GroupBox { Text = "Testing", Width = 250, Height = 80 };
        var btnFire = new Button { Text = "FIRE (Low Power)", Dock = DockStyle.Fill, BackColor = Color.Salmon };
        bool isFiring = false;
        btnFire.Click += (s, e) => 
        {
            if (!SerialInterface.Instance.IsConnected) return;
            if (isFiring)
            {
                SerialInterface.Instance.Write("M5\n");
                btnFire.Text = "FIRE (Low Power)";
                btnFire.BackColor = Color.Salmon;
                isFiring = false;
            }
            else
            {
                // M3 S1 (Low power)
                SerialInterface.Instance.Write("M3 S10\n"); // S10 just to be sure it's visible but safe-ish
                btnFire.Text = "STOP LASER";
                btnFire.BackColor = Color.Red;
                isFiring = true;
            }
        };
        grpFire.Controls.Add(btnFire);
        pnlControl.Controls.Add(grpFire);
        
        tabControl.Controls.Add(pnlControl);
        _rightTabControl.TabPages.Add(tabControl);

        // Tab 4: G-code / Console
        var tabConsole = new TabPage("G-code");
        var pnlConsole = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        pnlConsole.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pnlConsole.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        
        var txtLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.Lime, Font = new Font("Consolas", 9) };
        pnlConsole.Controls.Add(txtLog, 0, 0);
        
        var pnlInput = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        pnlInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlInput.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        
        var txtInput = new TextBox { Dock = DockStyle.Fill };
        var btnSend = new Button { Text = "Send", Dock = DockStyle.Fill };
        
        Action sendCommand = () => 
        {
             string cmd = txtInput.Text.Trim();
             if (!string.IsNullOrEmpty(cmd))
             {
                 SerialInterface.Instance.Write(cmd + "\n");
                 txtInput.Text = "";
                 // Local echo done by LineReceived/DataReceived ideally, but we can echo here too
                 if (txtLog.IsDisposed) return;
                 txtLog.AppendText($"> {cmd}\n");
                 txtLog.ScrollToCaret();
             }
        };
        
        btnSend.Click += (s, e) => sendCommand();
        txtInput.KeyDown += (s, e) => { if(e.KeyCode == Keys.Enter) { sendCommand(); e.SuppressKeyPress=true; } };
        
        pnlInput.Controls.Add(txtInput, 0, 0);
        pnlInput.Controls.Add(btnSend, 1, 0);
        pnlConsole.Controls.Add(pnlInput, 0, 1);
        
        tabConsole.Controls.Add(pnlConsole);
        _rightTabControl.TabPages.Add(tabConsole);
        
        // Wire up Logging
        SerialInterface.Instance.LineReceived += (line) => 
        {
            if (txtLog.IsDisposed) return;
            // optimize: ignore 'ok' to prevent spam/lag
            //if (line == "ok") return; 
            if(line == "ok") line += $" ({_jobRunner.PendingCommandsCount} slots)";

            try {
                txtLog.BeginInvoke(() => 
                {
                    if (line.Trim().StartsWith("error:"))
                    {
                         string errCode = line.Trim().Substring(6);
                         string msg = GrblErrors.GetMessage(errCode);
                         
                         txtLog.SelectionStart = txtLog.TextLength;
                         txtLog.SelectionLength = 0;
                         txtLog.SelectionColor = Color.Red;
                         txtLog.AppendText($"< {line} ({msg})\n");
                         txtLog.SelectionColor = txtLog.ForeColor;
                    }
                    else
                    {
                        txtLog.AppendText($"< {line}\n");
                    }
                    txtLog.ScrollToCaret();
                });
            } catch { } 
        };

        // Log Outgoing Data
        SerialInterface.Instance.LineSent += (line) =>
        {
            if (txtLog.IsDisposed) return;
             try {
                txtLog.BeginInvoke(() => 
                {
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.SelectionLength = 0;
                    txtLog.SelectionColor = Color.Yellow; 
                    txtLog.AppendText($">> {line}\n");
                    txtLog.SelectionColor = txtLog.ForeColor;
                    txtLog.ScrollToCaret();
                });
             } catch {}
        };

        // Handle dynamically detected buffer size
        SerialInterface.Instance.BufferLimitsReceived += (planner, rx) =>
        {
             // planner = Available planner blocks
             // rx = Available RX bytes.
             // User requested to use Planner Block count for flow control.
             
             // Only update the capacity when the machine is IDLE (buffer is empty).
             // Updating during a job would treat "Available" as "Max", causing throttling/deadlock.
             if (SerialInterface.Instance.MachineState != "Idle") return;

             if (_jobRunner.MaxPlannerBlocks != planner)
             {
                 _jobRunner.MaxPlannerBlocks = planner;
                 if (!txtLog.IsDisposed)
                 {
                      txtLog.BeginInvoke(() => txtLog.AppendText($"[INFO] Flow Control: Planner Blocks = {planner}, Rx Bytes = {rx}\n"));
                 }
             }
        };

        // Add Tabs to Right Panel
        rightSplit.Panel1.Controls.Add(_rightTabControl);
        
        // ... (Binding Logic for ObjectList) ...
        _objectList.CellFormatting += (s, e) => 
        {
            if (e.RowIndex < 0 || e.RowIndex >= _objectList.Rows.Count) return;
            var row = _objectList.Rows[e.RowIndex];
            if (row.DataBoundItem is not LaserObject obj) return;

            // Find Layer
            var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId);
            
            // NOTE: We check column by NAME to be safe
            if (_objectList.Columns[e.ColumnIndex].Name == "LayerName")
            {
                e.Value = layer?.Name ?? "None";
                e.FormattingApplied = true;
            }
            else if (_objectList.Columns[e.ColumnIndex].Name == "LayerPower")
            {
                e.Value = layer?.Power.ToString("0") ?? "0";
                e.FormattingApplied = true;
            }
            else if (_objectList.Columns[e.ColumnIndex].Name == "LayerSpeed")
            {
                e.Value = layer?.Speed.ToString("0") ?? "0";
                e.FormattingApplied = true;
            }
        };

        // Handle DataError
        _objectList.DataError += (s, e) => { e.Cancel = false; };
        
        // Selection Logic (Keep existing)
        _objectList.SelectionChanged += (s, e) => 
        {
            if(_isUpdatingSelection) return;
            if (_objectList.SelectedRows.Count > 0)
            {
                var list = new List<LaserObject>();
                foreach (DataGridViewRow row in _objectList.SelectedRows)
                {
                    if (row.DataBoundItem is LaserObject obj)
                    {
                        list.Add(obj);
                    }
                }
                
                var current = ProjectState.Instance.SelectedObjects;
                if (!new HashSet<LaserObject>(current).SetEquals(list))
                {
                     ProjectState.Instance.SelectedObjects = list;
                     _workbench.Invalidate();
                }
            }
            else
            {
                if (ProjectState.Instance.SelectedObjects.Count > 0)
                {
                    ProjectState.Instance.SelectedObjects = new List<LaserObject>();
                    _workbench.Invalidate();
                }
            }
        };

        ProjectState.Instance.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(ProjectState.SelectedObject) || e.PropertyName == nameof(ProjectState.SelectedObjects))
            {
                 // Update visual selection in list
                 UpdateSelectedObjects();
            }
        };

        // Control Panel (Bottom of Right)
        _controlPanel = new GroupBox
        {
            Text = "Laser Control",
            Dock = DockStyle.Fill
        };
        InitializeControlPanel();
        rightSplit.Panel2.Controls.Add(_controlPanel);

        this.Controls.Add(rightSplit);

        // Center: Workbench (Already initialized, just add)
        this.Controls.Add(_workbench);
        
        
        // 1. Fill (Inner-most)
        _workbench.BringToFront();
        
        // 2. Side Panels
        _layerPanel.BringToFront(); 
        _toolsPanel.BringToFront();
        rightSplit.BringToFront();

        if (_controlPanel != null) _controlPanel.BringToFront(); // Inside Split, doesn't matter for Main Z
        
        // 3. Outer Bars
        _statusStrip.SendToBack();
        _topToolbarPanel.SendToBack(); 
        menuStrip.SendToBack();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup Error: {ex.Message}\n{ex.StackTrace ?? "No Stack Trace"}", "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private FlowLayoutPanel _topToolbarPanel = null!; 

    private void InitializeTopToolbar()
    {
        _topToolbarPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Color.FromName("Control")
        };
        
        // Row 1: Mouse Position
        var tsRow1 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _lblMousePos = new ToolStripLabel("Mouse: 0.00, 0.00");
        tsRow1.Items.Add(_lblMousePos);
        
        // Row 2: Properties
        var tsRow2 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        
        tsRow2.Items.Add(new ToolStripLabel("X:"));
        _nudPosX = new NumericUpDown { Width = 60, DecimalPlaces = 2, Minimum = -10000, Maximum = 10000, Increment = 10 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudPosX));
        
        tsRow2.Items.Add(new ToolStripLabel("Y:"));
        _nudPosY = new NumericUpDown { Width = 60, DecimalPlaces = 2, Minimum = -10000, Maximum = 10000, Increment = 10 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudPosY));
        
        tsRow2.Items.Add(new ToolStripLabel("W:"));
        _nudSizeW = new NumericUpDown { Width = 60, DecimalPlaces = 2, Minimum = 0, Maximum = 10000, Increment = 10 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudSizeW));
        
        tsRow2.Items.Add(new ToolStripLabel("H:"));
        _nudSizeH = new NumericUpDown { Width = 60, DecimalPlaces = 2, Minimum = 0, Maximum = 10000, Increment = 10 };
        tsRow2.Items.Add(new ToolStripControlHost(_nudSizeH));
        
        tsRow2.Items.Add(new ToolStripSeparator());
        _lblLayerInfo = new ToolStripLabel("-");
        tsRow2.Items.Add(_lblLayerInfo);
        
        _topToolbarPanel.Controls.Add(tsRow1);
        _topToolbarPanel.Controls.Add(tsRow2);
        
        // Row 3: Text Controls
        var tsRow3 = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        
        tsRow3.Items.Add(new ToolStripLabel("Text:"));
        _txtContent = new ToolStripTextBox { Width = 150 };
        tsRow3.Items.Add(_txtContent);
        
        tsRow3.Items.Add(new ToolStripLabel("Font:"));
        _cmbFont = new ToolStripComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var family in FontFamily.Families)
        {
            _cmbFont.Items.Add(family.Name);
        }
        tsRow3.Items.Add(_cmbFont);
        
        tsRow3.Items.Add(new ToolStripLabel("Size:"));
        _nudFontSize = new NumericUpDown { Width = 60, Minimum = 1, Maximum = 1000, DecimalPlaces = 1 };
        tsRow3.Items.Add(new ToolStripControlHost(_nudFontSize));
        
        _topToolbarPanel.Controls.Add(tsRow3);
        
        this.Controls.Add(_topToolbarPanel); 
        
        // Wire Properties Logic
        EventHandler valChanged = (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1)
            {
                var obj = sel[0];
                
                float nx = (float)_nudPosX.Value;
                float ny = (float)_nudPosY.Value;
                float nw = (float)_nudSizeW.Value;
                float nh = (float)_nudSizeH.Value;
                
                // Position Change
                if(Math.Abs(obj.Position.X - nx) > 0.01 || Math.Abs(obj.Position.Y - ny) > 0.01)
                {
                     float dx = nx - obj.Position.X;
                     float dy = ny - obj.Position.Y;
                     CommandManager.Instance.Execute(new MoveCommand(sel, dx, dy));
                     _workbench.Invalidate();
                }
                
                // Size Change
                if(Math.Abs(obj.Size.Width - nw) > 0.01 || Math.Abs(obj.Size.Height - nh) > 0.01)
                {
                    obj.Size = new SizeF(nw, nh);
                    _workbench.Invalidate();
                }
            }
        };
        
        _nudPosX.ValueChanged += valChanged;
        _nudPosY.ValueChanged += valChanged;
        _nudSizeW.ValueChanged += valChanged;
        _nudSizeH.ValueChanged += valChanged;

        // Wire Text Logic
        EventHandler textChanged = (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1 && sel[0] is LaserText txt)
            {
                txt.Text = _txtContent.Text;
                if (_cmbFont.SelectedItem != null) txt.FontName = _cmbFont.SelectedItem?.ToString() ?? "Arial";
                txt.FontSize = (float)_nudFontSize.Value;
                
                // Recalc Size
                 using (var tmpBmp = new Bitmap(1, 1))
                 using (var g = Graphics.FromImage(tmpBmp))
                 using (var f = new Font(txt.FontName, txt.FontSize))
                 {
                      txt.Size = g.MeasureString(txt.Text, f);
                 }
                
                _workbench.Invalidate();
            }
        };

        _txtContent.TextChanged += textChanged;
        _cmbFont.SelectedIndexChanged += textChanged;
        _nudFontSize.ValueChanged += textChanged;
    }

    private void InitializeLayers()
    {
        _layerPanel.Controls.Clear();
        
        // Add "New Layer" Button
        var btnAdd = new Button
        {
            Text = "+",
            Size = new Size(30, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White
        };
        btnAdd.Click += (s, e) => 
        {
             // Create new layer
             // We need a random color
             var rnd = new Random();
             var color = Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256));
             var newLayer = new Layer($"Layer {ProjectState.Instance.Layers.Count}", color);
             ProjectState.Instance.Layers.Add(newLayer);
             InitializeLayers(); // Refresh
        };
        _layerPanel.Controls.Add(btnAdd);

        foreach (var layer in ProjectState.Instance.Layers)
        {
            var btn = new Button
            {
                BackColor = layer.Color,
                Size = new Size(30, 30),
                FlatStyle = FlatStyle.Flat,
                Tag = layer
            };
            
            // Tooltip for Layer Info
            var tt = new ToolTip();
            tt.SetToolTip(btn, $"{layer.Name}\nS:{layer.Speed} P:{layer.Power}% ({layer.Mode})");

            btn.MouseUp += (s, e) => 
            {
                if (e.Button == MouseButtons.Left)
                {
                     // Assign to selected objects if any
                     var sel = ProjectState.Instance.SelectedObjects;
                     if (sel.Count > 0)
                     {
                         foreach(var obj in sel) obj.LayerId = layer.Id;
                         _objectList.Refresh();
                         _workbench.Invalidate();
                         UpdateSelectedObjects(); // Update props panel
                     }
                     
                     ProjectState.Instance.ActiveLayer = layer;
                     if (s is Button b) UpdateLayerButtons(b);
                }
            };
            
            btn.DoubleClick += (s, e) => 
            {
                 using var dlg = new LayerSettingsForm(layer);
                 if (dlg.ShowDialog() == DialogResult.OK)
                 {
                     layer.Name = dlg.LayerName;
                     layer.Color = dlg.LayerColor;
                     layer.Speed = dlg.LayerSpeed;
                     layer.Power = dlg.LayerPower;
                     layer.Mode = dlg.LayerMode;
                     
                     btn.BackColor = layer.Color;
                     tt.SetToolTip(btn, $"{layer.Name}\nS:{layer.Speed} P:{layer.Power}% ({layer.Mode})");
                     _workbench.Invalidate();
                     UpdateSelectedObjects();
                 }
            };

            _layerPanel.Controls.Add(btn);

            if (ProjectState.Instance.ActiveLayer == layer)
            {
                UpdateLayerButtons(btn);
            }
        }
    }

    private void UpdateLayerButtons(Button activeBtn)
    {
        foreach(Control c in _layerPanel.Controls)
        {
            if (c is Button b)
            {
                if (c == activeBtn)
                {
                    b.FlatAppearance.BorderColor = Color.White;
                    b.FlatAppearance.BorderSize = 3;
                }
                else
                {
                    b.FlatAppearance.BorderColor = Color.Black; // Default
                    b.FlatAppearance.BorderSize = 1;
                }
            }
        }
    }

    private void InitializeTools()
    {
        var toolMap = new Dictionary<string, ToolType>
        {
            { "Select", ToolType.Select },
            { "Line", ToolType.DrawLine },
            { "Box", ToolType.DrawBox },
            { "Circle", ToolType.DrawCircle },
            { "Text", ToolType.Text },
            { "Ruler", ToolType.Ruler }
        };

        foreach (var kvp in toolMap)
        {
            var btn = new Button
            {
                Text = kvp.Key,
                Size = new Size(50, 50),
                Margin = new Padding(2),
                Tag = kvp.Value
            };
            
            btn.Click += (s, e) => 
            {
                ToolManager.Instance.SetTool((ToolType)btn.Tag);
                // Visual feedback (simple)
                foreach(Control c in _toolsPanel.Controls) c.BackColor = Color.FromName("Control");
                btn.BackColor = Color.LightBlue;
            };

            _toolsPanel.Controls.Add(btn);
        }
    }

    private void InitializeControlPanel()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        
        var btnConnect = new Button { Text = "Connect", Width = 200 };
        
        // Connect Logic
        btnConnect.Click += (s, e) => 
        {
             if (SerialInterface.Instance.IsConnected)
             {
                 SerialInterface.Instance.Disconnect();
             }
             else
             {
                 string port = AppConfiguration.Instance.LastPortName;
                 int baud = AppConfiguration.Instance.BaudRate;
                 if (string.IsNullOrEmpty(port))
                 {
                     MessageBox.Show("Please select a COM port in Options.", "Configuration Missing");
                     return;
                 }
                 try
                 {
                     SerialInterface.Instance.Connect(port, baud);
                 }
                 catch (Exception ex)
                 {
                     MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 }
             }
        };

        // Status Update
        SerialInterface.Instance.ConnectionStatusChanged += (connected) => 
        {
            if (btnConnect.IsDisposed) return;
            btnConnect.Invoke(() => 
            {
                if (connected)
                {
                    btnConnect.Text = "Disconnect";
                    btnConnect.BackColor = Color.Salmon;
                    _lblStatusConnection.Text = "Connected";
                    _lblStatusConnection.ForeColor = Color.Green;
                    
                    // Request Settings to update Work Area
                    SerialInterface.Instance.Write("$$");
                }
                else
                {
                    btnConnect.Text = "Connect";
                    btnConnect.BackColor = Color.FromName("Control");
                    _lblStatusConnection.Text = "Disconnected";
                    _lblStatusConnection.ForeColor = Color.Red;
                }
            });
        };
        
        SerialInterface.Instance.LineReceived += (line) => 
        {
             // Parse $130=... (X Max) and $131=... (Y Max)
             // Format: $130=200.000
             if (line.StartsWith("$130="))
             {
                 if (float.TryParse(line.Substring(5), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float xMax))
                 {
                     if (xMax > 0) 
                     {
                         AppConfiguration.Instance.WorkAreaWidth = xMax;
                         _workbench.Invalidate();
                     }
                 }
             }
             else if (line.StartsWith("$131="))
             {
                 if (float.TryParse(line.Substring(5), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float yMax))
                 {
                     if (yMax > 0) 
                     {
                         AppConfiguration.Instance.WorkAreaHeight = yMax;
                         _workbench.Invalidate();
                     }
                 }
             }
        };

        SerialInterface.Instance.StatusReceived += (state, pos) => 
        {
            if (_statusStrip.IsDisposed) return;
            _statusStrip.BeginInvoke(() => 
            {
                _lblStatusState.Text = $"State: {state}";
                _lblStatusPos.Text = $"Pos: {pos.X:F3}, {pos.Y:F3}";
                
                // Update Workbench Laser Position
                _workbench.LaserPosition = pos;
            });
        };

        // Wire Workbench Mouse Event
        if (_workbench != null)
        {
            _workbench.MousePositionChanged += (pos) => 
            {
                 if (this.IsDisposed) return;
                 // Optimize?
                 this.Invoke(() => _lblMousePos.Text = $"Mouse: {pos.X:F2}, {pos.Y:F2}");
            };
        }
        
        /* Moved Logic to InitializeTopToolbar to have access to NUDs
        // Wire Properties Logic
        EventHandler valChanged = (s, e) =>
        {
           ...
        };
        */

        _jobRunner.ProgressChanged += (curr, total) => 
        {
             if (_statusStrip.IsDisposed) return;
             // Throttle? Or just BeginInvoke
             _statusStrip.BeginInvoke(() => 
             {
                 _progressBar.Visible = true;
                 _progressBar.Maximum = total;
                 _progressBar.Value = Math.Min(curr, total);
             });
        };
        
        _jobRunner.JobCompleted += () => 
        {
             if (_statusStrip.IsDisposed) return;
             _statusStrip.Invoke(() => 
             {
                 _progressBar.Visible = false;
                 MessageBox.Show("Job Completed!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
             });
        };

        var btnStart = new Button { Text = "Start", Width = 200, BackColor = Color.LightGreen };
        btnStart.Click += (s, e) => 
        {
            if (!SerialInterface.Instance.IsConnected)
            {
                MessageBox.Show("Not connected.", "Error");
                return;
            }
            
            // Generate GCode
            var generator = new Data.Generators.GrblGenerator();
            var lines = generator.Generate(ProjectState.Instance.Objects);
            _jobRunner.Start(lines);
        };

        var btnStop = new Button { Text = "STOP", Width = 200, BackColor = Color.Red, ForeColor = Color.White };
        btnStop.Click += (s, e) => 
        {
             _jobRunner.Stop();
        };

        var btnPause = new Button { Text = "Pause/Resume", Width = 200, BackColor = Color.Yellow };
        btnPause.Click += (s, e) => 
        {
            if (_jobRunner.IsPaused) _jobRunner.Resume();
            else _jobRunner.Pause();
        };

        flow.Controls.Add(btnConnect);
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); // Spacer
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        var flowGen = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnGenerate = new Button { Text = "G-Code", Width = 90, BackColor = Color.LightBlue };
        btnGenerate.Click += (s, e) => GenerateGCode();
        
        var btnPreview = new Button { Text = "Preview", Width = 90, BackColor = Color.LightYellow };
        btnPreview.Click += (s, e) => ShowPreview();
        
        flowGen.Controls.Add(btnGenerate);
        flowGen.Controls.Add(btnPreview);
        flow.Controls.Add(flowGen);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });

        flow.Controls.Add(btnStart);

        flow.Controls.Add(btnPause);
        flow.Controls.Add(btnStop);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        var flowGroup = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnGroup = new Button { Text = "Group", Width = 60 };
        var btnUngroup = new Button { Text = "Ungroup", Width = 60 };
        var btnArray = new Button { Text = "Array", Width = 60 };
        
        btnGroup.Click += (s, e) => 
        {
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count > 1) CommandManager.Instance.Execute(new GroupCommand(sel));
        };
        btnUngroup.Click += (s, e) => 
        {
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Any(o => o is LaserGroup)) CommandManager.Instance.Execute(new UngroupCommand(sel));
        };
        btnArray.Click += (s, e) =>
        {
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 0) return;
            
            using var dlg = new GridArrayForm();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var cmd = new CloneArrayCommand(sel, dlg.Rows, dlg.Cols, dlg.GapX, dlg.GapY);
                CommandManager.Instance.Execute(cmd);
                if (_workbench != null) _workbench.Invalidate();
            }
        };
        
        flowGroup.Controls.Add(btnGroup);
        flowGroup.Controls.Add(btnUngroup);
        flowGroup.Controls.Add(btnArray);
        
        flow.Controls.Add(flowGroup);
        
        // REMOVED HISTORY
        // lbHistory.Items.Clear();
        // CommandManager.Instance.StateChanged += ... 
        
        _controlPanel.Controls.Add(flow);

        // Size updates are tricky for Paths, limiting to Position for robust MVP or carefully implementing.
        // Let's allow Position editing fully. Size editing... maybe just disables for Paths?
        // Or we implement a "SetBounds" method that scales?
        // Let's wire Position first.
        
        // Snapping Toggle
        var chkSnap = new CheckBox { Text = "Snap to Grid", AutoSize = true };
        chkSnap.CheckedChanged += (s, e) => { if (_workbench != null) _workbench.IsSnappingEnabled = chkSnap.Checked; };
        flow.Controls.Add(chkSnap);

        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 

        // Drawing Framing
        // Drawing Framing
        var grpFraming = new GroupBox { Text = "Alignment / Marking", Width = 200, Height = 220 };
        var flowFraming = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        
        var lblPwr = new Label { Text = "Power (%):", AutoSize = true };
        var numFramePower = new NumericUpDown { Minimum = 0, Maximum = 100, Value = (decimal)AppConfiguration.Instance.FramingPower };
        
        var lblSpd = new Label { Text = "Speed:", AutoSize = true };
        var numFrameSpeed = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = (decimal)AppConfiguration.Instance.FramingSpeed, Increment = 100 };

        var btnFrame = new Button { Text = "Frame All Bound", Width = 180, BackColor = Color.LightYellow };
        var btnOutline = new Button { Text = "Outline Objects", Width = 180, BackColor = Color.LightCyan };
        var btnMark = new Button { Text = "Mark Centers (X)", Width = 180, BackColor = Color.LightCyan };
        
        btnFrame.Click += (s, e) => 
        {
            AppConfiguration.Instance.FramingPower = (float)numFramePower.Value;
            AppConfiguration.Instance.FramingSpeed = (float)numFrameSpeed.Value;
            AppConfiguration.Instance.Save();
            
            var gen = new GrblGenerator();
            var lines = gen.GenerateFraming(ProjectState.Instance.Objects, AppConfiguration.Instance.FramingPower, AppConfiguration.Instance.FramingSpeed);
            
            _jobRunner.Start(lines);
        };
        
        btnOutline.Click += (s, e) => 
        {
             AppConfiguration.Instance.FramingPower = (float)numFramePower.Value;
             AppConfiguration.Instance.FramingSpeed = (float)numFrameSpeed.Value;
             AppConfiguration.Instance.Save();

             var gen = new GrblGenerator();
             var objects = ProjectState.Instance.SelectedObjects.Any() ? ProjectState.Instance.SelectedObjects : ProjectState.Instance.Objects.ToList();
             var lines = gen.GenerateObjectOutlines(objects, AppConfiguration.Instance.FramingPower, AppConfiguration.Instance.FramingSpeed);
             // Make debug window
             //using var dlg = new DebugCodeForm(string.Join("\n", lines));
             //dlg.ShowDialog();
             _jobRunner.Start(lines);
        };

        btnMark.Click += (s, e) => 
        {
             AppConfiguration.Instance.FramingPower = (float)numFramePower.Value;
             AppConfiguration.Instance.FramingSpeed = (float)numFrameSpeed.Value;
             AppConfiguration.Instance.Save();

             var gen = new GrblGenerator();
             var objects = ProjectState.Instance.SelectedObjects.Any() ? ProjectState.Instance.SelectedObjects : ProjectState.Instance.Objects.ToList();
             var lines = gen.GenerateCenterMarks(objects, AppConfiguration.Instance.FramingPower, AppConfiguration.Instance.FramingSpeed);
             _jobRunner.Start(lines);
        };

        flowFraming.Controls.Add(lblPwr);
        flowFraming.Controls.Add(numFramePower);
        flowFraming.Controls.Add(lblSpd);
        flowFraming.Controls.Add(numFrameSpeed);
        flowFraming.Controls.Add(btnFrame);
        flowFraming.Controls.Add(btnOutline);
        flowFraming.Controls.Add(btnMark);
        grpFraming.Controls.Add(flowFraming);
        flow.Controls.Add(grpFraming);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 
        // lbHistory removed
        // CommandManager.Instance.StateChanged += ... // Removed from UI
        if (_workbench != null) _workbench.Invalidate();

        if (_controlPanel != null) _controlPanel.Controls.Add(flow);
    }

    private void ImportFile()
    {
        using var ofd = new OpenFileDialog();
        ofd.Filter = "Supported Files|*.bmp;*.jpg;*.jpeg;*.png;*.svg|Images|*.bmp;*.jpg;*.jpeg;*.png|Scalable Vector Graphics|*.svg|All Files|*.*";
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            string ext = Path.GetExtension(ofd.FileName).ToLower();
            
            if (ext == ".svg")
            {
                try 
                {
                    var objects = SvgImporter.Import(ofd.FileName);
                    var cmd = new AddObjectCommand(objects);
                    
                    foreach(var obj in objects)
                    {
                        if (ProjectState.Instance.ActiveLayer != null)
                             obj.LayerId = ProjectState.Instance.ActiveLayer.Id;
                    }
                    CommandManager.Instance.Execute(cmd);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import SVG: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                // Assume Image
                try
                {
                    // Load into LaserImage
                    var lImg = new LaserImage();
                    lImg.Name = Path.GetFileNameWithoutExtension(ofd.FileName);
                    lImg.ImagePath = ofd.FileName;
                    
                    using var stream = new FileStream(ofd.FileName, FileMode.Open, FileAccess.Read);
                    var lbmp = new Bitmap(stream);
                    lImg.Image = new Bitmap(lbmp); 
                    
                    lImg.Position = new PointF(0, 0);
                    
                    float dpiX = lImg.Image.HorizontalResolution;
                    float dpiY = lImg.Image.VerticalResolution;
                    if (dpiX <= 0) dpiX = 96;
                    if (dpiY <= 0) dpiY = 96;
                    
                    float width = lImg.Image.Width * (96.0f / dpiX);
                    float height = lImg.Image.Height * (96.0f / dpiY);
                    
                    lImg.Size = new SizeF(width, height);

                    if (ProjectState.Instance.ActiveLayer != null)
                        lImg.LayerId = ProjectState.Instance.ActiveLayer.Id;

                    // Command
                    var cmd = new AddObjectCommand(lImg);
                    CommandManager.Instance.Execute(cmd);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }


    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            CommandManager.Instance.Undo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y))
        {
            CommandManager.Instance.Redo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.G))
        {
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count > 1)
            {
                var cmd = new GroupCommand(sel);
                CommandManager.Instance.Execute(cmd);
            }
            return true;
        }
        if (keyData == (Keys.Control | Keys.U))
        {
            // Ungroup ALL selected groups
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Any(o => o is LaserGroup))
            {
                var cmd = new UngroupCommand(sel);
                CommandManager.Instance.Execute(cmd);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public bool UpdateSelectedObjects(bool updateListSelection = true)
    {
        var sel = ProjectState.Instance.SelectedObjects;
        
        _isUpdatingUI = true;
        if (sel.Count == 1)
        {
            var obj = sel[0];
            _nudPosX.Enabled = true;
            _nudPosY.Enabled = true;
            _nudSizeW.Enabled = true;
            _nudSizeH.Enabled = true;
            
            _nudPosX.Value = (decimal)obj.Position.X;
            _nudPosY.Value = (decimal)obj.Position.Y;
            _nudSizeW.Value = (decimal)obj.Size.Width;
            _nudSizeH.Value = (decimal)obj.Size.Height;
            
            // Text Toolbar
            if (obj is LaserText txt)
            {
                _txtContent.Enabled = true;
                _cmbFont.Enabled = true;
                _nudFontSize.Enabled = true;
                
                _txtContent.Text = txt.Text;
                if (_cmbFont.Items.Contains(txt.FontName))
                    _cmbFont.SelectedItem = txt.FontName;
                else if (_cmbFont.Items.Count > 0)
                     _cmbFont.SelectedIndex = 0; 
                    
                _nudFontSize.Value = (decimal)txt.FontSize;
            }
            else
            {
                _txtContent.Enabled = false;
                _cmbFont.Enabled = false;
                _nudFontSize.Enabled = false;
                _txtContent.Text = "";
            }

            // Update Layer Info Label
            var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId);
            if (layer != null)
            {
                _lblLayerInfo.Text = $"{layer.Name} (S: {layer.Speed})";
            }
            else
            {
                _lblLayerInfo.Text = "No Layer";
            }
        }
        else
        {
            _nudPosX.Enabled = false;
            _nudPosY.Enabled = false;
            _nudSizeW.Enabled = false;
            _nudSizeH.Enabled = false;
            
            _txtContent.Enabled = false;
            _cmbFont.Enabled = false;
            _nudFontSize.Enabled = false;
            
            // Clear or set to 0? NUDs don't support empty string.
            // Just leaving enabled=false is visible enough.
            // But value might be misleading.
            _nudPosX.Value = 0;
            _nudPosY.Value = 0;
            _nudSizeW.Value = 0;
            _nudSizeH.Value = 0;
            _txtContent.Text = "";

            _lblLayerInfo.Text = "-";
        }
        _isUpdatingUI = false;
        
        _isUpdatingSelection = true;
        
        if (updateListSelection)
        {
            var currentSet = new HashSet<LaserObject>(ProjectState.Instance.SelectedObjects);
            
            // Update Object List Selection
            foreach (DataGridViewRow row in _objectList.Rows)
            {
                if (row.DataBoundItem is LaserObject obj)
                {
                    bool shouldSelect = currentSet.Contains(obj);
                if (row.Selected != shouldSelect)
                {
                    row.Selected = shouldSelect;
                }
            }
        }
        }
        _isUpdatingSelection = false;

        return true;
    }

    private void GenerateGCode()
    {
        string generatorName = AppConfiguration.Instance.GCodeGenerator;
        IGCodeGenerator? generator = null;

        if (generatorName == "Grbl") generator = new GrblGenerator();
        // Add others here

        if (generator == null)
        {
             // Default
             generator = new GrblGenerator();
        }

        try
        {
            var lines = generator.Generate(ProjectState.Instance.Objects);
            var gcode = string.Join(Environment.NewLine, lines);
            
            using var dlg = new DebugCodeForm(gcode);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Generation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowPreview()
    {
        string generatorName = AppConfiguration.Instance.GCodeGenerator;
        IGCodeGenerator? generator = null;

        if (generatorName == "Grbl") generator = new GrblGenerator();
        // Add others here

        if (generator == null)
        {
             // Default
             generator = new GrblGenerator();
        }

        try
        {
            var lines = generator.Generate(ProjectState.Instance.Objects);
            var gcode = string.Join(Environment.NewLine, lines);
            
            using var dlg = new PreviewForm(gcode);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Preview generation failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
