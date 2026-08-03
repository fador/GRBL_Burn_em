using grbl_burn_em.Data;
using grbl_burn_em.Data.Commands;
using grbl_burn_em.Forms;
using grbl_burn_em.Controls;
using System.Reflection;

namespace grbl_burn_em;

public partial class MainForm
{

    private void SetupCustomLayout()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            this.Text = $"GRBL Burn'Em Laser Control Software v{version?.Major}.{version?.Minor}";
            
            try
            {
               var assembly = Assembly.GetExecutingAssembly();
               using (var stream = assembly.GetManifestResourceStream("grbl_burn_em.icon.png"))
               {
                   if (stream != null)
                   {
                       using (var bmp = new Bitmap(stream))
                       {
                           this.Icon = Icon.FromHandle(bmp.GetHicon());
                       }
                   }
               }
            }
            catch { }
            
            _workbench = new WorkbenchControl
            {
                Dock = DockStyle.Fill
            };
            
            var cfg = AppConfiguration.Instance;
            if (cfg.WindowX != -1 && cfg.WindowWidth > 0 && cfg.WindowHeight > 0)
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(cfg.WindowX, cfg.WindowY);
                this.Size = new Size(cfg.WindowWidth, cfg.WindowHeight);
                
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
                    try
                    {
                        ProjectSerializer.Load(ofd.FileName);
                        CommandManager.Instance.Clear();
                        InitializeLayers(); 
                        _layerList.Refresh();
                        _workbench.Invalidate();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open project: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
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
            fileMenu.DropDownItems.Add("Export SVG...", null, (s, e) => ExportSvg());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("Options", null, (s, e) => 
            {
                using var dlg = new OptionsForm(GetRegisteredGeneratorNames());
                dlg.ShowDialog();
            });
            menuStrip.Items.Add(fileMenu);

            var editMenu = new ToolStripMenuItem("Edit");
            ((ToolStripMenuItem)editMenu.DropDownItems.Add("Undo", null, (s, e) => CommandManager.Instance.Undo())).ShortcutKeys = Keys.Control | Keys.Z;
            ((ToolStripMenuItem)editMenu.DropDownItems.Add("Redo", null, (s, e) => CommandManager.Instance.Redo())).ShortcutKeys = Keys.Control | Keys.Y;
            editMenu.DropDownItems.Add(new ToolStripSeparator());
            editMenu.DropDownItems.Add("Copy", null, (s, e) => CopySelection());
            editMenu.DropDownItems.Add("Paste", null, (s, e) => PasteSelection());
            editMenu.DropDownItems.Add("Delete", null, (s, e) => DeleteSelection());
            
            menuStrip.Items.Add(editMenu);

            var layersMenu = new ToolStripMenuItem("Layers");
            layersMenu.DropDownItems.Add("Scale Output...", null, (s, e) => ShowScaleLayerDialog());
            menuStrip.Items.Add(layersMenu);

            var insertMenu = new ToolStripMenuItem("Insert");
            insertMenu.DropDownItems.Add("Mathematical Shape...", null, (s, e) => ShowMathShapeDialog());
            menuStrip.Items.Add(insertMenu);

            var toolMenu = new ToolStripMenuItem("Tool");
            toolMenu.DropDownItems.Add("Edit text", null, (s, e) => EditText());
            toolMenu.DropDownItems.Add(new ToolStripSeparator());
            toolMenu.DropDownItems.Add("Mask Image with Shape", null, (s, e) => MaskSelectedImage());
            toolMenu.DropDownItems.Add("Unmask Image", null, (s, e) => UnmaskSelectedImage());
            toolMenu.DropDownItems.Add(new ToolStripSeparator());
            toolMenu.DropDownItems.Add("Camera Settings", null, (s, e) => 
            {
                var frm = new CameraSettingsForm();
                frm.Show(this);
            });
            
            toolMenu.DropDownItems.Add("Stop Camera", null, async (s, e) => 
            {
                await CameraManager.Instance.StopCameraAsync();
            });
            
            toolMenu.DropDownItems.Add(new ToolStripSeparator());
            toolMenu.DropDownItems.Add("Group", null, (s, e) => GroupSelection());
            toolMenu.DropDownItems.Add("Ungroup", null, (s, e) => UngroupSelection());
            toolMenu.DropDownItems.Add("Array Modifier", null, (s, e) => ShowArrayModifierDialog());
            toolMenu.DropDownItems.Add("Nesting / Packing...", null, (s, e) => ShowNestingDialog());
            toolMenu.DropDownItems.Add(new ToolStripSeparator());
            toolMenu.DropDownItems.Add("Attach to Path", null, (s, e) => AttachSelectedTextToPath());
            toolMenu.DropDownItems.Add("Detach from Path", null, (s, e) => DetachSelectedTextFromPath());
            toolMenu.DropDownItems.Add(new ToolStripSeparator());
            toolMenu.DropDownItems.Add("Power/Speed Calibration", null, (s, e) => ShowPowerSpeedCalibrationDialog());

            menuStrip.Items.Add(toolMenu);

            var aboutMenu = new ToolStripMenuItem("About");
            aboutMenu.DropDownItems.Add("About", null, (s, e) => 
            {
                using var dlg = new OptionsForm();
                dlg.SelectTab("About");
                dlg.ShowDialog();
            });
            menuStrip.Items.Add(aboutMenu);

            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            InitializeTopToolbar();

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

            _statusStrip = new StatusStrip();
            _lblStatusConnection = new ToolStripStatusLabel("Disconnected") { ForeColor = Color.Red };
            _lblStatusState = new ToolStripStatusLabel("State: Unknown");
            _lblStatusPos = new ToolStripStatusLabel("Pos: 0,0");
            _progressBar = new ToolStripProgressBar { Width = 100, Visible = false };
            
            _statusStrip.Items.AddRange(new ToolStripItem[] { _lblStatusConnection, new ToolStripSeparator(), _lblStatusState, new ToolStripSeparator(), _lblStatusPos, new ToolStripSeparator(), _progressBar });
            this.Controls.Add(_statusStrip);

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

            var rightSplit = new SplitContainer
            {
                Dock = DockStyle.Right,
                Width = 300,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 150
            };

            _rightTabControl = new TabControl { Dock = DockStyle.Fill };
            
            var tabObjects = new TabPage("Objects");
            _objectList = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                DataSource = ProjectState.Instance.Objects,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                MultiSelect = true,
                AllowDrop = true
            };

            _contextMenu = new ContextMenuStrip();
            var itemCopy = new ToolStripMenuItem("Copy");
            var itemPaste = new ToolStripMenuItem("Paste");
            var itemArray = new ToolStripMenuItem("Array Modifier");
            var itemEditText = new ToolStripMenuItem("Edit text");
            var itemMask = new ToolStripMenuItem("Mask Image with Shape");
            var itemUnmask = new ToolStripMenuItem("Unmask Image");
            var itemGroup = new ToolStripMenuItem("Group");
            var itemUngroup = new ToolStripMenuItem("Ungroup");
            var itemAttach = new ToolStripMenuItem("Attach to Path");
            var itemDetach = new ToolStripMenuItem("Detach from Path");

            itemCopy.Click += (s, e) => CopySelection();
            itemPaste.Click += (s, e) => PasteSelection();
            itemArray.Click += (s, e) => ShowArrayModifierDialog();

            itemEditText.Click += (s, e) => EditText();
            itemMask.Click += (s, e) => MaskSelectedImage();
            itemUnmask.Click += (s, e) => UnmaskSelectedImage();
            itemGroup.Click += (s, e) => GroupSelection();
            itemUngroup.Click += (s, e) => UngroupSelection();
            itemAttach.Click += (s, e) => AttachSelectedTextToPath();
            itemDetach.Click += (s, e) => DetachSelectedTextFromPath();

            _contextMenu.Items.AddRange(new ToolStripItem[] { 
                itemCopy, itemPaste, new ToolStripSeparator(), 
                itemEditText, new ToolStripSeparator(), 
                itemArray, new ToolStripSeparator(),
                itemMask, itemUnmask, new ToolStripSeparator(), 
                itemGroup, itemUngroup, new ToolStripSeparator(), 
                itemAttach, itemDetach 
            });

            _contextMenu.Opening += (s, e) => 
            {
                var selRows = _objectList.SelectedRows;
                var selObjects = ProjectState.Instance.SelectedObjects;
                
                itemPaste.Enabled = Clipboard.ContainsText();
                itemEditText.Enabled = selObjects.Any(o => o is LaserText);
                itemMask.Enabled = false;
                itemUnmask.Enabled = selObjects.OfType<LaserImage>().Any(i => i.MaskId != Guid.Empty);

                if (selRows.Count == 2)
                {
                    var obj1 = ProjectState.Instance.Objects[selRows[0].Index];
                    var obj2 = ProjectState.Instance.Objects[selRows[1].Index];
                    bool hasImage = obj1 is LaserImage || obj2 is LaserImage;
                    bool hasShape = obj1 is LaserCircle || obj1 is LaserRectangle || obj2 is LaserCircle || obj2 is LaserRectangle;
                    if (hasImage && hasShape) itemMask.Enabled = true;
                }

                for (int i = _contextMenu.Items.Count - 1; i >= 0; i--)
                {
                    if (_contextMenu.Items[i].Tag is string mTag && mTag == "Plugin")
                        _contextMenu.Items.RemoveAt(i);
                }

                if (_pluginContextActions.Any())
                {
                    _contextMenu.Items.Add(new ToolStripSeparator { Tag = "Plugin" });
                    foreach(var pa in _pluginContextActions)
                    {
                        var pItem = new ToolStripMenuItem(pa.Name);
                        pItem.Tag = "Plugin";
                        pItem.Click += (sender, args) => 
                        {
                            var target = ProjectState.Instance.SelectedObject;
                            if(target != null) pa.Action(target);
                        };
                        _contextMenu.Items.Add(pItem);
                    }
                }
            };
            _objectList.ContextMenuStrip = _contextMenu;
            
            Rectangle dragBoxFromMouseDown = Rectangle.Empty;
            int rowIndexFromMouseDown = -1;
            int rowIndexOfItemUnderMouseToDrop = -1;

            _objectList.MouseMove += (s, e) => 
            {
                if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
                {
                    if (dragBoxFromMouseDown != Rectangle.Empty && !dragBoxFromMouseDown.Contains(e.X, e.Y))
                    {
                        DragDropEffects dropEffect = _objectList.DoDragDrop(_objectList.Rows[rowIndexFromMouseDown], DragDropEffects.Move);
                    }
                }
            };

            _objectList.MouseDown += (s, e) => 
            {
                 rowIndexFromMouseDown = _objectList.HitTest(e.X, e.Y).RowIndex;

                 if (rowIndexFromMouseDown != -1)
                 {
                     Size dragSize = SystemInformation.DragSize;
                     dragBoxFromMouseDown = new Rectangle(new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)), dragSize);
                 }
                 else
                 {
                     dragBoxFromMouseDown = Rectangle.Empty;
                 }
            };
            
            _objectList.DragOver += (s, e) => e.Effect = DragDropEffects.Move;

            _objectList.DragDrop += (s, e) => 
            {
                 Point clientPoint = _objectList.PointToClient(new Point(e.X, e.Y));
                 rowIndexOfItemUnderMouseToDrop = _objectList.HitTest(clientPoint.X, clientPoint.Y).RowIndex;

                 if (e.Effect == DragDropEffects.Move)
                 {
                     if (rowIndexOfItemUnderMouseToDrop < 0) rowIndexOfItemUnderMouseToDrop = _objectList.Rows.Count - 1;
                     
                     var objects = ProjectState.Instance.Objects;
                     if (rowIndexFromMouseDown >= 0 && rowIndexFromMouseDown < objects.Count)
                     {
                         var item = objects[rowIndexFromMouseDown];
                         
                         if (rowIndexOfItemUnderMouseToDrop != rowIndexFromMouseDown)
                         {
                             objects.RemoveAt(rowIndexFromMouseDown);
                             objects.Insert(rowIndexOfItemUnderMouseToDrop, item);
                             
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

            tabObjects.Controls.Add(_objectList);
            tabObjects.Controls.Add(tsOrder);
            
            _rightTabControl.TabPages.Add(tabObjects);

            var tabLayers = new TabPage("Layers");
            _layerList = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                DataSource = ProjectState.Instance.Layers,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                MultiSelect = false
            };
            
            _layerList.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "IsVisible", HeaderText = "Vis", Width = 30 });
            _layerList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            var colColor = new DataGridViewTextBoxColumn { DataPropertyName = "Color", HeaderText = "Color", Width = 40, ReadOnly = true };
            _layerList.Columns.Add(colColor);
            _layerList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Speed", HeaderText = "Spd", Width = 50 });
            _layerList.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Power", HeaderText = "Pwr%", Width = 50 });
            
            var colMode = new DataGridViewComboBoxColumn 
            { 
                DataPropertyName = "Mode", 
                HeaderText = "Mode", 
                Width = 60,
                DataSource = Enum.GetValues(typeof(LayerMode))
            };
            _layerList.Columns.Add(colMode);

            _layerList.CellDoubleClick += (s, e) => 
            {
                if (e.RowIndex < 0) return;
                var layer = ProjectState.Instance.Layers[e.RowIndex];
                if (e.ColumnIndex == _layerList.Columns[2].Index)
                {
                    using var cd = new ColorDialog { Color = layer.Color };
                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        layer.Color = cd.Color;
                        _layerList.Refresh();
                        InitializeLayers();
                        _workbench.Invalidate();
                    }
                }
            };

            _layerList.CellFormatting += (s, e) => 
            {
                if (e.RowIndex < 0 || e.RowIndex >= ProjectState.Instance.Layers.Count) return;
                var layer = ProjectState.Instance.Layers[e.RowIndex];
                 if (e.ColumnIndex == _layerList.Columns[2].Index)
                 {
                     e.CellStyle.BackColor = layer.Color;
                     e.CellStyle.SelectionBackColor = layer.Color;
                     e.Value = ""; 
                     e.FormattingApplied = true;
                 }
            };
            
            _layerList.CellValueChanged += (s, e) => 
            {
                 _workbench.Invalidate();
                 InitializeLayers();
                 _objectList.Refresh();
                 UpdateSelectedObjects();
            };

            _layerList.DataError += (s, e) => { e.Cancel = false; };

            tabLayers.Controls.Add(_layerList);
            _rightTabControl.TabPages.Add(tabLayers);

            var tabControl = new TabPage("Control");
            var pnlControl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true, Padding = new Padding(10) };
            
            var grpJog = new GroupBox { Text = "Jog", Width = 250, Height = 120 };
            var gridJog = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            
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
            
            var pnlStep = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            pnlStep.Controls.Add(new Label { Text = "Step (mm):", AutoSize = true, Padding = new Padding(0,5,0,0) });
            var cmbStep = new ComboBox { Width = 60 };
            cmbStep.Items.AddRange(new object[] { "0.1", "1", "10", "100" });
            cmbStep.SelectedIndex = 2;
            pnlStep.Controls.Add(cmbStep);
            pnlControl.Controls.Add(pnlStep);
            
            var pnlFeed = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            pnlFeed.Controls.Add(new Label { Text = "Feed (mm/min):", AutoSize = true, Padding = new Padding(0,5,0,0) });
            var numFeed = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = 1000, Width = 60 };
            pnlFeed.Controls.Add(numFeed);
            pnlControl.Controls.Add(pnlFeed);

            Action<string, string> sendJog = (axis, dir) => 
            {
                 if (!SerialInterface.Instance.IsConnected) return;
                 if (!double.TryParse(cmbStep.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double step)) step = 10;
                 double dist = (dir == "-") ? -step : step;
                 string cmd = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"$J=G91 {axis}{dist} F{numFeed.Value}");
                 SerialInterface.Instance.Write(cmd + "\n");
            };
            
            btnYPlus.Click += (s, e) => sendJog("Y", "+");
            btnYMinus.Click += (s, e) => sendJog("Y", "-");
            btnXPlus.Click += (s, e) => sendJog("X", "+");
            btnXMinus.Click += (s, e) => sendJog("X", "-");
            btnHome.Click += (s, e) => SerialInterface.Instance.Write("$H\n");

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
                    SerialInterface.Instance.Write("M3 S10\n");
                    btnFire.Text = "STOP LASER";
                    btnFire.BackColor = Color.Red;
                    isFiring = true;
                }
            };
            grpFire.Controls.Add(btnFire);
            pnlControl.Controls.Add(grpFire);
            
            tabControl.Controls.Add(pnlControl);
            _rightTabControl.TabPages.Add(tabControl);

            var tabConsole = new TabPage("G-code");
            var pnlConsole = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            pnlConsole.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pnlConsole.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            
            var txtLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.Lime, Font = new Font("Consolas", 9) };
            pnlConsole.Controls.Add(txtLog, 0, 0);
            
            var pnlInput = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            pnlInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pnlInput.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));

            PluginManager.Instance.Initialize(this);
            
            var txtInput = new TextBox { Dock = DockStyle.Fill };
            var btnSend = new Button { Text = "Send", Dock = DockStyle.Fill };
            
            Action sendCommand = () => 
            {
                 string cmd = txtInput.Text.Trim();
                 if (!string.IsNullOrEmpty(cmd))
                 {
                     SerialInterface.Instance.Write(cmd + "\n");
                     txtInput.Text = "";
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
            
            _logTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _logTimer.Tick += (s, e) => 
            {
                if (_logBuffer.IsEmpty || txtLog.IsDisposed) return;
                
                var sb = new System.Text.StringBuilder();
                int count = 0;
                while (_logBuffer.TryDequeue(out string? result) && count < 500)
                {
                    sb.Append(result);
                    count++;
                }
                
                if (sb.Length > 0)
                {
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.SelectionLength = 0;
                    txtLog.AppendText(sb.ToString());
                    txtLog.ScrollToCaret();  
                }
            };
            _logTimer.Start();

            SerialInterface.Instance.LineReceived += (line) => 
            {
                if (txtLog.IsDisposed) return;
                string logMsg = line.Contains("error:") ? $"< {line} (Error)\n" : $"< {line}\n";
                _logBuffer.Enqueue(logMsg);
            };

            SerialInterface.Instance.LineSent += (line) =>
            {
                if (txtLog.IsDisposed) return;
                _logBuffer.Enqueue($">> {line}\n");
            };

            SerialInterface.Instance.BufferLimitsReceived += (planner, rx) =>
            {
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

            rightSplit.Panel1.Controls.Add(_rightTabControl);
            
            _objectList.CellFormatting += (s, e) => 
            {
                if (e.RowIndex < 0 || e.RowIndex >= _objectList.Rows.Count) return;
                var row = _objectList.Rows[e.RowIndex];
                if (row.DataBoundItem is not LaserObject obj) return;
                var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId);
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

            _objectList.DataError += (s, e) => { e.Cancel = false; };
            
            _objectList.SelectionChanged += (s, e) => 
            {
                if(_isUpdatingSelection) return;
                if (_objectList.SelectedRows.Count > 0)
                {
                    var list = new List<LaserObject>();
                    foreach (DataGridViewRow row in _objectList.SelectedRows)
                    {
                        if (row.DataBoundItem is LaserObject obj)
                            list.Add(obj);
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
                     UpdateSelectedObjects();
            };

            _controlPanel = new GroupBox
            {
                Text = "Laser Control",
                Dock = DockStyle.Fill
            };
            InitializeControlPanel();
            rightSplit.Panel2.Controls.Add(_controlPanel);

            this.Load += (s, e) => { rightSplit.SplitterDistance = 150; };
            this.Controls.Add(rightSplit);
            this.Controls.Add(_workbench);
            
            _workbench.BringToFront();
            _layerPanel.BringToFront(); 
            _toolsPanel.BringToFront();
            rightSplit.BringToFront();

            if (_controlPanel != null) _controlPanel.BringToFront();
            
            _statusStrip.SendToBack();
            _topToolbarPanel.SendToBack(); 
            menuStrip.SendToBack();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup Error: {ex.Message}\n{ex.StackTrace ?? "No Stack Trace"}", "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
