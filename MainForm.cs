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
    private DataGridView _objectList = null!;
    private FlowLayoutPanel _layerPanel = null!;
    private FlowLayoutPanel _toolsPanel = null!;
    private GroupBox _controlPanel = null!;

    private bool _isUpdatingSelection = false;

    public MainForm()
    {
        InitializeComponent();
        SetupCustomLayout();
    }

    private void SetupCustomLayout()
    {
        this.Text = "Laser Control Software";
        this.Size = new Size(1200, 800);

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
            Width = 250,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 200
        };

        // Object List (Top of Right)
        _objectList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = ProjectState.Instance.Objects,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            MultiSelect = true,
            Height = 200
        };
        _objectList.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "IsEnabled", HeaderText = "On", Width = 30 });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Power", HeaderText = "Pwr%", Width = 50 });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Speed", HeaderText = "Spd", Width = 50 });
        
        // Selection Sync
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
                
                // Avoid infinite loop if ProjectState triggers this
                // We need equality check? Or just set it.
                // Setting SelectedObjects triggers PropertyChanged, which we listen to below.
                // We must ensure we don't re-select in list if list initiated it.
                // But typically it's fine if we handle re-entrancy or if checks pass.
                
                // Simple check: identify if list matches state
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
                 UpdateSelectedObjects();
            }
        };

        rightSplit.Panel1.Controls.Add(_objectList);

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
        foreach (var layer in ProjectState.Instance.Layers)
        {
            var btn = new Button
            {
                BackColor = layer.Color,
                Size = new Size(30, 30),
                FlatStyle = FlatStyle.Flat,
                Tag = layer
            };
            btn.Click += (s, e) => 
            {
                ProjectState.Instance.ActiveLayer = layer;
                if (s is Button b) UpdateLayerButtons(b);
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
        
        var btnGenerate = new Button { Text = "Generate G-Code", Width = 200, BackColor = Color.LightBlue };
        btnGenerate.Click += (s, e) => GenerateGCode();
        flow.Controls.Add(btnGenerate);
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });

        flow.Controls.Add(btnStart);

        flow.Controls.Add(btnPause);
        flow.Controls.Add(btnStop);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        var flowGroup = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnGroup = new Button { Text = "Group", Width = 95 };
        var btnUngroup = new Button { Text = "Ungroup", Width = 95 };
        
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
        
        flowGroup.Controls.Add(btnGroup);
        flowGroup.Controls.Add(btnUngroup);
        flow.Controls.Add(flowGroup);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 

        // Framing
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
                        // Don't add directly, let Execute do it
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
        _isUpdatingSelection = true;
        var current = new HashSet<LaserObject>(ProjectState.Instance.SelectedObjects);
        
        foreach (DataGridViewRow row in _objectList.Rows)
        {
            if (row.DataBoundItem is LaserObject obj)
            {
                bool shouldSelect = current.Contains(obj);
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
}
