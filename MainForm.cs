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

    private NumericUpDown _numPosX = null!;
    private NumericUpDown _numPosY = null!;
    private NumericUpDown _numSizeW = null!;
    private NumericUpDown _numSizeH = null!;
    private bool _isUpdatingUI = false;

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
        this.Text = "Laser Control Software";
        
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
        var menuStrip = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("File");
        
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
        this.MainMenuStrip = menuStrip;
        this.Controls.Add(menuStrip);

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
            SplitterDistance = 100
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
            MultiSelect = true
        };
        _objectList.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "IsEnabled", HeaderText = "On", Width = 30 });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { Name = "LayerName", HeaderText = "Layer", Width = 80, ReadOnly = true });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { Name = "LayerPower", HeaderText = "Pwr%", Width = 40, ReadOnly = true });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { Name = "LayerSpeed", HeaderText = "Spd", Width = 40, ReadOnly = true });
        
        tabObjects.Controls.Add(_objectList);
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

        // Center: Workbench
        _workbench = new WorkbenchControl
        {
            Dock = DockStyle.Fill
        };
        this.Controls.Add(_workbench);
        
        // Z-Order correction (Docking happens in reverse add order usually, but let's be safe)
        // Bring Menu to front usually needed if it was added first but docked Top? 
        // WinForms docking needs Last Added -> First in Dock Order for Fill. 
        // So Fill should be added first? No.
        // Actually: Controls.Add adds to the beginning of the collection (index 0).
        // Dock layout engine lays out children in REVERSE index order (last control in collection laid out first).
        
        // We added Menu (Top) -> Layers (Bottom) -> Tools (Left) -> RightSplit (Right) -> Workbench (Fill).
        // If we assume Standard order:
        // Workbench (Fill) should be "top" of z-order (Index 0) to fill remaining space?
        // Let's just create them and trust the process, if it looks wrong we fix z-order.
        // Controls.Add adds to index 0.
        // So Workbench is Index 0.
        // RightSplit is Index 1.
        // Tools is Index 2.
        // Layers is Index 3.
        // Menu is Index 4.
        
        // Layout:
        // Menu (Top) takes space.
        // Layers (Bottom) takes space.
        // Tools (Left) takes space.
        // RightSplit (Right) takes space.
        // Workbench (Fill) takes remaining.
        
        // This actually works perfectly with Controls.Add order if done in reverse order of dependency?
        // Wait, standard WinForms:
        // this.Controls.Add(fillControl);
        // this.Controls.Add(dockLeftControl);
        // ...
        
        // Let's force Z-order just in case.
        _workbench.BringToFront(); // Fill last
        rightSplit.BringToFront();
        _toolsPanel.BringToFront();
        _layerPanel.BringToFront();
        menuStrip.BringToFront(); // Menu always top
        
        _workbench.SendToBack(); // Fill needs to be at the bottom of the z-order to be docked "last" in space calculation?
        // Actually, the control at index 0 is docked FIRST.
        // If I dock TOP, it takes top.
        // If I dock FILL, it takes whatever is left.
        // So FILL must be at the END of the list (Index Count-1) OR added FIRST?
        // "The control with the lowest Z-order (highest index) is docked first." -> Microsoft docs usually say this but it's confusing.
        // Correct rule: Controls are docked in the order of the Controls collection (0 to Count-1) or reverse?
        // "The z-order of the controls determines the docking priority. The control at the top of the z-order (index 0) has the HIGHEST priority and gets docked FIRST."
        // So:
        // 1. Menu (Top)
        // 2. Tools (Left)
        // 3. Layers (Bottom)
        // 4. Right Panel (Right)
        // 5. Workbench (Fill)
        
        // So we need to add them in that order (last added becomes index 0) -> No, Add() inserts at 0.
        // So we should add Workbench FIRST, then Right, then Layers, then Tools, then Menu.
        // My code added Menu, then Layers, then Tools, then Right, then Workbench.
        // So Workbench is at 0. Right is at 1...
        // So Workbench (Fill) gets docked FIRST? That would cover everything.
        // We need Workbench to be docked LAST (Lowest Priority).
        // So Workbench needs to be at the BOTTOM of Z-Order (Highest Index).
        
        // So:
        // menuStrip.BringToFront(); (Index 0)
        // _toolsPanel.BringToFront();
        // _layerPanel.BringToFront();
        // rightSplit.BringToFront();
        // _workbench.SendToBack();
        
        menuStrip.BringToFront();
        _toolsPanel.BringToFront();
        _layerPanel.BringToFront(); // or Bottom
        rightSplit.BringToFront();
        
        // Check order
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
                }
                else
                {
                    btnConnect.Text = "Connect";
                    btnConnect.BackColor = Color.FromName("Control");
                }
            });
        };

        var btnStart = new Button { Text = "Start", Width = 200, BackColor = Color.LightGreen };
        var btnStop = new Button { Text = "STOP", Width = 200, BackColor = Color.Red, ForeColor = Color.White };
        var btnPause = new Button { Text = "Pause", Width = 200, BackColor = Color.Yellow };

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
                _workbench.Invalidate();
            }
        };
        
        flowGroup.Controls.Add(btnGroup);
        flowGroup.Controls.Add(btnUngroup);
        flowGroup.Controls.Add(btnArray);
        
        flow.Controls.Add(flowGroup);
        
        // --- Transform / Properties ---
        var grpProps = new GroupBox { Text = "Transform", Width = 200, Height = 140 };
        var pnlProps = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        
        pnlProps.Controls.Add(new Label { Text = "X (mm):", AutoSize = true }, 0, 0);
        _numPosX = new NumericUpDown { DecimalPlaces = 2, Minimum = -1000, Maximum = 1000, Width = 80 };
        pnlProps.Controls.Add(_numPosX, 1, 0);

        pnlProps.Controls.Add(new Label { Text = "Y (mm):", AutoSize = true }, 0, 1);
        _numPosY = new NumericUpDown { DecimalPlaces = 2, Minimum = -1000, Maximum = 1000, Width = 80 };
        pnlProps.Controls.Add(_numPosY, 1, 1);

        pnlProps.Controls.Add(new Label { Text = "Width:", AutoSize = true }, 0, 2);
        _numSizeW = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 1000, Width = 80 };
        pnlProps.Controls.Add(_numSizeW, 1, 2);

        pnlProps.Controls.Add(new Label { Text = "Height:", AutoSize = true }, 0, 3);
        _numSizeH = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 1000, Width = 80 };
        pnlProps.Controls.Add(_numSizeH, 1, 3);
        
        // Add Layer/Speed/Power Info in Properties
        pnlProps.Controls.Add(new Label { Text = "Layer:", AutoSize = true }, 0, 4);
        var lblLayerInfo = new Label { Text = "-", AutoSize = true };
        lblLayerInfo.Name = "lblLayerInfo";
        pnlProps.Controls.Add(lblLayerInfo, 1, 4);
        
        grpProps.Controls.Add(pnlProps);
        flow.Controls.Add(grpProps);
        
        // Logic for Properties
        EventHandler valChanged = (s, e) =>
        {
            if (_isUpdatingUI) return;
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count == 1)
            {
                var obj = sel[0];
                float nx = (float)_numPosX.Value;
                float ny = (float)_numPosY.Value;
                float nw = (float)_numSizeW.Value;
                float nh = (float)_numSizeH.Value;
                
                // Only create command if changed
                if(Math.Abs(obj.Position.X - nx) > 0.01 || Math.Abs(obj.Position.Y - ny) > 0.01)
                {
                     // Move Absolute? MoveCommand is Relative.
                     float dx = nx - obj.Position.X;
                     float dy = ny - obj.Position.Y;
                     CommandManager.Instance.Execute(new MoveCommand(sel, dx, dy));
                }
                
                if(Math.Abs(obj.Size.Width - nw) > 0.01 || Math.Abs(obj.Size.Height - nh) > 0.01)
                {
                     // Resize? ResizeCommand expects Dictionary of States.
                     var oldState = new Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points)>();
                     var newState = new Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points)>();
                     
                     oldState[obj] = (obj.Position, obj.Size, (obj as LaserPath)?.Points?.ToList());
                     
                     // We need to set the new size directly on a temp object or calculate expected state?
                     // Actually ResizeCommand takes "New States".
                     // So we construct what we WANT.
                     var newSz = new SizeF(nw, nh);
                     newState[obj] = (obj.Position, newSz, (obj as LaserPath)?.Points?.ToList()); // Points scaling is tricky here without UpdateResize logic.
                     // For simple Width/Height update, we might need a dedicated command or careful scaling.
                     // IMPORTANT: Setting Size directly might not scale points for Paths.
                     // Checking LaserObject.cs...
                }
            }
        };
        
        _numPosX.ValueChanged += valChanged;
        _numPosY.ValueChanged += valChanged;
        // Size updates are tricky for Paths, limiting to Position for robust MVP or carefully implementing.
        // Let's allow Position editing fully. Size editing... maybe just disables for Paths?
        // Or we implement a "SetBounds" method that scales?
        // Let's wire Position first.
        
        // Snapping Toggle
        var chkSnap = new CheckBox { Text = "Snap to Grid", AutoSize = true };
        chkSnap.CheckedChanged += (s, e) => { _workbench.IsSnappingEnabled = chkSnap.Checked; };
        flow.Controls.Add(chkSnap);

        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 

        // Drawing Framing
        var grpFraming = new GroupBox { Text = "Framing", Width = 200, Height = 140 };
        var flowFraming = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        
        var lblPwr = new Label { Text = "Power (%):", AutoSize = true };
        var numFramePower = new NumericUpDown { Minimum = 0, Maximum = 100, Value = (decimal)AppConfiguration.Instance.FramingPower };
        
        var lblSpd = new Label { Text = "Speed:", AutoSize = true };
        var numFrameSpeed = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = (decimal)AppConfiguration.Instance.FramingSpeed, Increment = 100 };

        var btnFrame = new Button { Text = "Frame Bounds", Width = 180, BackColor = Color.LightYellow };
        
        btnFrame.Click += (s, e) => 
        {
            AppConfiguration.Instance.FramingPower = (float)numFramePower.Value;
            AppConfiguration.Instance.FramingSpeed = (float)numFrameSpeed.Value;
            AppConfiguration.Instance.Save();
            
            var gen = new GrblGenerator();
            var lines = gen.GenerateFraming(ProjectState.Instance.Objects, AppConfiguration.Instance.FramingPower, AppConfiguration.Instance.FramingSpeed);
            
            var gcode = string.Join(Environment.NewLine, lines);
            using var dlg = new DebugCodeForm(gcode);
            dlg.ShowDialog();
        };

        flowFraming.Controls.Add(lblPwr);
        flowFraming.Controls.Add(numFramePower);
        flowFraming.Controls.Add(lblSpd);
        flowFraming.Controls.Add(numFrameSpeed);
        flowFraming.Controls.Add(btnFrame);
        grpFraming.Controls.Add(flowFraming);
        flow.Controls.Add(grpFraming);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 
        flow.Controls.Add(new Label { Text = "History:", AutoSize = true });
        
        var lbHistory = new ListBox { Width = 200, Height = 200 };
        flow.Controls.Add(lbHistory);
        
        CommandManager.Instance.StateChanged += (s, e) => 
        {
             lbHistory.Items.Clear();
             foreach(var desc in CommandManager.Instance.GetHistory())
             {
                 lbHistory.Items.Add(desc);
             }
             // Add current stack indicator logic if needed, but simple list for now
             _workbench.Invalidate();
        };

        _controlPanel.Controls.Add(flow);
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

    public bool UpdateSelectedObjects()
    {
        _workbench.Invalidate();
        var sel = ProjectState.Instance.SelectedObjects;
        
        _isUpdatingUI = true;
        if (sel.Count == 1)
        {
            var obj = sel[0];
            _numPosX.Enabled = true;
            _numPosY.Enabled = true;
            _numSizeW.Enabled = true;
            _numSizeH.Enabled = true;
            
            _numPosX.Value = (decimal)obj.Position.X;
            _numPosY.Value = (decimal)obj.Position.Y;
            _numSizeW.Value = (decimal)obj.Size.Width;
            _numSizeH.Value = (decimal)obj.Size.Height;
            
            // Update Layer Info Label
            var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId);
            var lblInfo = _controlPanel.Controls.Find("lblLayerInfo", true).FirstOrDefault() as Label;
            if (lblInfo != null)
            {
                if (layer != null)
                {
                    lblInfo.Text = $"{layer.Name}\nS: {layer.Speed}\nP: {layer.Power}%\n{layer.Mode}";
                    // Add Mode info if asked
                }
                else
                {
                    lblInfo.Text = "No Layer";
                }
            }
            
            // Switch tab to "Objects" if needed? No, let user stay on Layers if they want.
            
            // Update selection in list?
            // If Single selection
        }
        else
        {
            _numPosX.Enabled = false;
            _numPosY.Enabled = false;
            _numSizeW.Enabled = false;
            _numSizeH.Enabled = false;
            
            _numPosX.Value = 0;
            _numPosY.Value = 0;
            _numSizeW.Value = 0;
            _numSizeH.Value = 0;
            
            // Clear label
            var lblInfo = _controlPanel.Controls.Find("lblLayerInfo", true).FirstOrDefault() as Label;
            if (lblInfo != null) lblInfo.Text = "-";
        }
        _isUpdatingUI = false;
        
        // Update layer buttons based on selection?
        
        _isUpdatingSelection = true;
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
