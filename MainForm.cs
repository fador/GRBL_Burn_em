using laser_gui_test.Controls;
using laser_gui_test.Data;
using laser_gui_test.Tools;
using System.ComponentModel;

namespace laser_gui_test;

public partial class MainForm : Form
{
    private WorkbenchControl _workbench;
    private DataGridView _objectList;
    private FlowLayoutPanel _layerPanel;
    private FlowLayoutPanel _toolsPanel;
    private GroupBox _controlPanel;

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
            _workbench.Invalidate();
        });

        fileMenu.DropDownItems.Add("Open", null, (s, e) => 
        {
            using var ofd = new OpenFileDialog { Filter = "Laser Project|*.json" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                 ProjectSerializer.Load(ofd.FileName);
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
            SplitterDistance = 400
        };

        // Object List (Top of Right)
        _objectList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = ProjectState.Instance.Objects,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            MultiSelect = false
        };
        _objectList.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "IsEnabled", HeaderText = "On", Width = 30 });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Power", HeaderText = "Pwr%", Width = 50 });
        _objectList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Speed", HeaderText = "Spd", Width = 50 });
        
        // Selection Sync
        _objectList.SelectionChanged += (s, e) => 
        {
            if (_objectList.SelectedRows.Count > 0)
            {
                var obj = _objectList.SelectedRows[0].DataBoundItem as LaserObject;
                if (ProjectState.Instance.SelectedObject != obj)
                {
                     ProjectState.Instance.SelectedObject = obj;
                     _workbench.Invalidate();
                }
            }
            else
            {
                ProjectState.Instance.SelectedObject = null;
                _workbench.Invalidate();
            }
        };

        ProjectState.Instance.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(ProjectState.SelectedObject))
            {
                var sel = ProjectState.Instance.SelectedObject;
                if (sel == null)
                {
                    _objectList.ClearSelection();
                }
                else
                {
                    foreach (DataGridViewRow row in _objectList.Rows)
                    {
                        if (row.DataBoundItem == sel)
                        {
                            row.Selected = true;
                            break;
                        }
                    }
                }
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
            { "Box", ToolType.DrawBox }
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
        var btnStart = new Button { Text = "Start", Width = 200, BackColor = Color.LightGreen };
        var btnStop = new Button { Text = "STOP", Width = 200, BackColor = Color.Red, ForeColor = Color.White };
        var btnPause = new Button { Text = "Pause", Width = 200, BackColor = Color.Yellow };

        flow.Controls.Add(btnConnect);
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); // Spacer
        flow.Controls.Add(btnStart);
        flow.Controls.Add(btnPause);
        flow.Controls.Add(btnStop);

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
                    foreach(var obj in objects)
                    {
                        // Assign active layer
                        if (ProjectState.Instance.ActiveLayer != null)
                        {
                            obj.LayerId = ProjectState.Instance.ActiveLayer.Id;
                        }
                        ProjectState.Instance.AddObject(obj);
                    }
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
                    
                    // Helper to load image properly (ProjectSerializer has logic, maybe reuse or plain Load)
                    // For now plain load
                    using var stream = new FileStream(ofd.FileName, FileMode.Open, FileAccess.Read);
                    lImg.Image = new Bitmap(stream);
                    
                    lImg.Position = new PointF(0, 0);
                    // Default scale: 1 pixel = 0.1 mm ? Or 1 pixel = 1 pixel (0.26mm)?
                    // Laser cutters often map 1px = X mm.
                    // Let's keep 1px = 1 unit (approx 0.26mm at 96dpi) if using 96dpi grid.
                    // Actually let's just use pixel dimensions.
                    lImg.Size = new SizeF(lImg.Image.Width, lImg.Image.Height);

                    if (ProjectState.Instance.ActiveLayer != null)
                        lImg.LayerId = ProjectState.Instance.ActiveLayer.Id;

                    ProjectState.Instance.AddObject(lImg);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
