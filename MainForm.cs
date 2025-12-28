/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using grbl_burn_em.Controls;
using grbl_burn_em.Data;
using grbl_burn_em.Tools;
using grbl_burn_em.Data.Commands;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em;

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
    private FlowLayoutPanel _topToolbarPanel = null!;

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
    private NumericUpDown _nudRotation = null!;
    private ToolStripLabel _lblLayerInfo = null!;
    
    // Logging Optimization
    private System.Collections.Concurrent.ConcurrentQueue<string> _logBuffer = new System.Collections.Concurrent.ConcurrentQueue<string>();
    private System.Windows.Forms.Timer _logTimer = null!;
    
    // Text Toolbar Controls
    private ToolStripTextBox _txtContent = null!;
    private ToolStripComboBox _cmbFont = null!;
    private NumericUpDown _nudFontSize = null!;
    private ToolStripButton _btnBold = null!;
    private ToolStripButton _btnItalic = null!;

    // Row 4 Controls
    private TrackBar _trkPathOffset = null!;
    private NumericUpDown _nudVerticalOffset = null!;
    private CheckBox _chkReversePath = null!;
    private CheckBox _chkUpsideDown = null!;
    private ToolStripComboBox _cmbWarpMethod = null!;
    private ToolTip _toolTip = new ToolTip();

    // Plugin Support
    private ContextMenuStrip _contextMenu = null!;
    private List<(string Name, Action<LaserObject> Action)> _pluginContextActions = new();
    private List<IGCodeGenerator> _gcodeGenerators = new();

    public MainForm()
    {
        InitializeComponent();
        SetupCustomLayout();
    }

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
}
