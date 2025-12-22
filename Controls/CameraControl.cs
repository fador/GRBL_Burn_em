using System;
using System.Drawing;
using System.Windows.Forms;
using grbl_burn_em.Data;
using grbl_burn_em.Forms;

namespace grbl_burn_em.Controls
{
    public class CameraControl : UserControl
    {
        private ComboBox _cmbDevices = null!;
        private Button _btnStartStop = null!;
        private CheckBox _chkOverlay = null!;
        private TrackBar _trkOpacity = null!;
        private Button _btnCalibrate = null!;
        
        // Manual Adjustments
        private NumericUpDown _nudX = null!;
        private NumericUpDown _nudY = null!;
        private NumericUpDown _nudWidth = null!;
        private NumericUpDown _nudHeight = null!;
        
        private Button _btnRefresh = null!;
        private CheckBox _chkMounted = null!;

        public CameraControl()
        {
            InitializeComponent();
            RefreshDevices();
            CameraManager.Instance.CameraStopped += OnCameraStopped;
        }

        private void InitializeComponent()
        {
            this.Size = new Size(300, 400);
            
            // Use TableLayoutPanel for better resizing
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(10),
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Full width column

            // 1. Device Selection
            var pnlDevice = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            pnlDevice.Controls.Add(new Label { Text = "Camera Device:", AutoSize = true });
            _cmbDevices = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            pnlDevice.Controls.Add(_cmbDevices);
            var btnRefresh = new Button { Text = "Refresh List", Width = 100 };
            btnRefresh.Click += (s, e) => RefreshDevices();
            pnlDevice.Controls.Add(btnRefresh);
            layout.Controls.Add(pnlDevice);

            // 2. Start/Stop
            _btnStartStop = new Button { Text = "Start Camera", Height = 40, BackColor = Color.LightGreen, Dock = DockStyle.Top };
            _btnStartStop.Click += OnStartStopClick;
            layout.Controls.Add(_btnStartStop);

            layout.Controls.Add(new Label { Text = "", Height = 10 }); // Spacer

            // 3. Overlay Settings
            var grpOverlay = new GroupBox { Text = "Overlay Settings", Dock = DockStyle.Top, AutoSize = true };
            var flowOverlay = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            flowOverlay.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            flowOverlay.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            
            _chkOverlay = new CheckBox { Text = "Enable Overlay", Checked = false, AutoSize = true };
            _chkOverlay.CheckedChanged += (s, e) => 
            {
                UpdateConfigFromUI(); // Save state
                if (MainForm.Instance != null && !_chkOverlay.Checked)
                {
                    // Explicitly clear overlay if unchecked
                     UpdateOverlay();
                }
            };
            flowOverlay.Controls.Add(_chkOverlay, 0, 0);
            flowOverlay.SetColumnSpan(_chkOverlay, 2);
            
            flowOverlay.Controls.Add(new Label { Text = "Opacity:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            _trkOpacity = new TrackBar { Minimum = 0, Maximum = 100, Value = 50, Dock = DockStyle.Fill };
            _trkOpacity.Scroll += (s, e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            flowOverlay.Controls.Add(_trkOpacity, 1, 1);

            // Manual Transforms
            // X
            flowOverlay.Controls.Add(new Label { Text = "X:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            _nudX = new NumericUpDown { DecimalPlaces = 2, Minimum = -5000, Maximum = 5000, Dock = DockStyle.Fill };
            _nudX.ValueChanged += (s,e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            flowOverlay.Controls.Add(_nudX, 1, 2);
            
            // Y
            flowOverlay.Controls.Add(new Label { Text = "Y:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            _nudY = new NumericUpDown { DecimalPlaces = 2, Minimum = -5000, Maximum = 5000, Dock = DockStyle.Fill };
            _nudY.ValueChanged += (s,e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            flowOverlay.Controls.Add(_nudY, 1, 3);
            
            // W
            flowOverlay.Controls.Add(new Label { Text = "W:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            _nudWidth = new NumericUpDown { DecimalPlaces = 2, Minimum = 1, Maximum = 10000, Dock = DockStyle.Fill, Value = 100 };
            _nudWidth.ValueChanged += (s,e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            flowOverlay.Controls.Add(_nudWidth, 1, 4);

            // H
            flowOverlay.Controls.Add(new Label { Text = "H:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            _nudHeight = new NumericUpDown { DecimalPlaces = 2, Minimum = 1, Maximum = 10000, Dock = DockStyle.Fill, Value = 100 };
            _nudHeight.ValueChanged += (s,e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            flowOverlay.Controls.Add(_nudHeight, 1, 5);
            
            grpOverlay.Controls.Add(flowOverlay);
            layout.Controls.Add(grpOverlay);
            
            layout.Controls.Add(new Label { Text = "", Height = 10 }); // Spacer

            // 4. Calibration & Mounting
            var pnlMount = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown };
            _chkMounted = new CheckBox { Text = "Head Mounted Camera", Checked = AppConfiguration.Instance.CameraIsMounted, Width = 250 };
            _chkMounted.CheckedChanged += (s, e) => 
            { 
                AppConfiguration.Instance.CameraIsMounted = _chkMounted.Checked; 
                AppConfiguration.Instance.Save();
                UpdateUIState();
            };
            pnlMount.Controls.Add(_chkMounted);
            layout.Controls.Add(pnlMount);

            _btnCalibrate = new Button { Text = "Calibrate Alignment", Dock = DockStyle.Top, Height = 30 };
            _btnCalibrate.Click += OnCalibrateClick;
            layout.Controls.Add(_btnCalibrate);

            var btnLensCalib = new Button { Text = "Calibrate Lens (ArUco)", Dock = DockStyle.Top, Height = 30 };
            btnLensCalib.Click += (s, e) => 
            {
                 using var form = new ArucoCalibrationForm();
                 form.ShowDialog();
            };
            layout.Controls.Add(btnLensCalib);

            _btnRefresh = new Button { Text = "Scan Workspace", Dock = DockStyle.Top, Height = 30, BackColor = Color.LightSkyBlue };
            _btnRefresh.Click += OnRefreshClick;
            layout.Controls.Add(_btnRefresh);

            this.Controls.Add(layout);
            
            // Load Initial Values
            LoadSettings();
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            var isMounted = _chkMounted.Checked;
            _btnRefresh.Visible = isMounted;
            // Also might want to hide manual transforms if using calibration?
            // For now keep them available for manual tweak.
        }

        private void OnCalibrateClick(object? sender, EventArgs e)
        {
             var menu = new ContextMenuStrip();
             
             menu.Items.Add("Lens Calibration (Distortion)", null, (s, args) => 
             {
                 using var form = new LensCalibrationForm();
                 form.ShowDialog();
             });
             
             menu.Items.Add("-");

             menu.Items.Add("Alignment: Head Mounted (Offset)", null, (s, args) => 
             {
                 using var form = new OffsetCalibrationForm();
                 if (form.ShowDialog() == DialogResult.OK)
                 {
                     UpdateUIState();
                     UpdateOverlay();
                 }
             });

             menu.Items.Add("Alignment: Stationary (Homography)", null, (s, args) => 
             {
                // Stationary Calibration
                using var form = new CalibrationForm();
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var imgPoints = form.SelectedPoints;
                    // Now Ask for World Points.
                    MessageBox.Show("Now click the 4 corresponding points on the Workbench Grid.\nUse the 'Measure' tool or simply Click-to-Select logic (Not implemented yet). \n\nFor now, we will simulate 4 corners of the work area: \n(0,0), (AreaW,0), (AreaW, AreaH), (0, AreaH).", "Step 2");
                    
                    var config = AppConfiguration.Instance;
                    // World Points (Bed Corners)
                    PointF[] worldPoints = {
                        new PointF(0, config.WorkAreaHeight), // Top-Left (Y-Up: Height)
                        new PointF(config.WorkAreaWidth, config.WorkAreaHeight), // Top-Right
                        new PointF(config.WorkAreaWidth, 0), // Bottom-Right
                        new PointF(0, 0) // Bottom-Left
                    };
                    
                    CameraManager.Instance.ComputeHomography(imgPoints, worldPoints);
                    MessageBox.Show("Homography Computed and Applied.");
                }
             });
             
             menu.Show(_btnCalibrate, new Point(0, _btnCalibrate.Height));
        }
        
        private void OnRefreshClick(object? sender, EventArgs e)
        {
             // Trigger Grid Scan
             if (MessageBox.Show("Start Workspace Scan? The machine will move to cover the work area.", "Scan", MessageBoxButtons.YesNo) == DialogResult.Yes)
             {
                 // Start Scan Job
                 CameraManager.Instance.StartScan();
             }
        }



        private void LoadSettings()
        {
            var config = AppConfiguration.Instance;
            _chkOverlay.Checked = config.ShowCameraOverlay;
            _trkOpacity.Value = (int)(config.CameraOverlayOpacity * 100);
            _nudX.Value = (decimal)config.CameraOverlayX;
            _nudY.Value = (decimal)config.CameraOverlayY;
            _nudWidth.Value = (decimal)config.CameraOverlayWidth;
            _nudHeight.Value = (decimal)config.CameraOverlayHeight;
            _chkMounted.Checked = config.CameraIsMounted;
            
            // Select Last Device if available
            if (!string.IsNullOrEmpty(config.LastCameraDevice))
            {
                if (_cmbDevices.Items.Contains(config.LastCameraDevice))
                {
                    _cmbDevices.SelectedItem = config.LastCameraDevice;
                }
            }
        }

        private void UpdateConfigFromUI()
        {
            var config = AppConfiguration.Instance;
            config.ShowCameraOverlay = _chkOverlay.Checked;
            config.CameraOverlayOpacity = _trkOpacity.Value / 100f;
            config.CameraOverlayX = (float)_nudX.Value;
            config.CameraOverlayY = (float)_nudY.Value;
            config.CameraOverlayWidth = (float)_nudWidth.Value;
            config.CameraOverlayHeight = (float)_nudHeight.Value;
            config.Save();
        }

        private void RefreshDevices()
        {
            _cmbDevices.Items.Clear();
            var devices = CameraManager.Instance.GetAvailableDevices();
            if (devices.Count > 0)
            {
                _cmbDevices.Items.AddRange(devices.ToArray());
                _cmbDevices.SelectedIndex = 0;
            }
        }

        private void OnStartStopClick(object? sender, EventArgs e)
        {
            if (CameraManager.Instance.IsRunning)
            {
                CameraManager.Instance.StopCamera();
                // Button update handled by OnCameraStopped event
            }
            else
            {
                int index = _cmbDevices.SelectedIndex;
                if (index >= 0 && _cmbDevices.SelectedItem != null)
                {
                    var deviceName = _cmbDevices.SelectedItem.ToString() ?? "";
                    AppConfiguration.Instance.LastCameraDevice = deviceName;
                    AppConfiguration.Instance.Save();
                    
                    CameraManager.Instance.StartCamera(index);
                    CameraManager.Instance.FrameReceived += OnFrameReceived;
                    _btnStartStop.Text = "Stop Camera";
                    _btnStartStop.BackColor = Color.Salmon;
                }
            }
        }

        private void StartManualCalibration()
        {
             // TODO: Open Calibration Dialog
             MessageBox.Show("Manual Calibration coming soon.\nUse the X, Y, W, H controls to align the overlay manually for now.", "Calibration", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnFrameReceived(Bitmap frame)
        {
            // This comes from Camera Thread. Invoke UI.
            // But we don't want to flood the UI message pump.
            // WorkbenchControl needs the bitmap.
            // We should push the bitmap to WorkbenchControl.BackgroundImage?
            
            // We can invoke an update on WorkbenchControl periodically or as fast as possible.
            // Updating BackgroundImage triggers Invalidate() only if we set it.
            
            if (_chkOverlay.Checked)
            {
                // We need to pass this bitmap to Main UI.
                // Warning: Bitmap ownership. WorkbenchControl DrawImage needs a valid bitmap.
                // If we replace it, we must dispose the old one.
                
                try
                {
                    this.Invoke(new Action(() => 
                    {
                        var wb = GetWorkbench();
                        if (wb != null)
                        {
                            var old = wb.OverlayImage;
                            wb.OverlayImage = frame; // Workbench uses this for drawing
                            
                            // Transform
                            var config = AppConfiguration.Instance;
                            wb.OverlayImageOpacity = config.CameraOverlayOpacity;
                            wb.OverlayImagePosition = new PointF(config.CameraOverlayX, config.CameraOverlayY);
                            wb.OverlayImageSize = new SizeF(config.CameraOverlayWidth, config.CameraOverlayHeight);
                            
                            wb.Invalidate();
                            
                            old?.Dispose(); // Dispose old frame
                        }
                        else
                        {
                            frame.Dispose();
                        }
                    }));
                }
                catch 
                {
                    // UI Disposed or Closing
                    frame.Dispose(); 
                }
            }
            else
            {
                frame.Dispose();
            }
        }
        
        private void UpdateOverlay()
        {
             if (!AppConfiguration.Instance.ShowCameraOverlay)
             {
                 var wb = GetWorkbench();
                 if (wb != null)
                 {
                     var old = wb.OverlayImage;
                     wb.OverlayImage = null;
                     old?.Dispose();
                     wb.Invalidate();
                 }
                 return;
             }
             
             // If config matches UI, do we need to do anything? 
             // FrameReceived handles the image update.
             // This is mostly for opacity/position update which is done inside FrameReceived too?
             // Or if we want to repaint the Last Frame with new params?
             
             // If we have a background image, we can just invalidate.
             var wbc = GetWorkbench();
             if (wbc != null)
             {
                 var config = AppConfiguration.Instance;
                 wbc.OverlayImageOpacity = config.CameraOverlayOpacity;
                 wbc.OverlayImagePosition = new PointF(config.CameraOverlayX, config.CameraOverlayY);
                 wbc.OverlayImageSize = new SizeF(config.CameraOverlayWidth, config.CameraOverlayHeight);
                 wbc.Invalidate();
             }
        }

        private void OnCameraStopped()
        {
             // Clear overlay when camera stops
             if (this.InvokeRequired)
             {
                 this.Invoke(new Action(OnCameraStopped));
                 return;
             }
             
             _btnStartStop.Text = "Start Camera";
             _btnStartStop.BackColor = Color.LightGreen;
             
             var wb = GetWorkbench();
             if (wb != null)
             {
                 var old = wb.OverlayImage;
                 wb.OverlayImage = null;
                 old?.Dispose();
                 wb.Invalidate();
             }
        }

        private WorkbenchControl? GetWorkbench()
        {
            // Hacky way to find WorkbenchControl if we are not injected with it.
            // We can access MainForm.Instance
            // But MainForm doesn't expose Workbench directly as public? 
            // It is private _workbench.
            // We need to modify MainForm to expose it or pass it.
            
            // Let's assume we can get it via Controls search or modify MainForm.
            // For now, let's look at MainForm again.
            // It has `private WorkbenchControl _workbench`.
            // Modifying MainForm to expose it is cleaner.
            
            if (MainForm.Instance.Controls.Find("WorkbenchControl", true).FirstOrDefault() is WorkbenchControl wb)
            {
                return wb;
            }
            // Fallback: iterate controls of main form
             foreach(Control c in MainForm.Instance.Controls)
             {
                 if (c is WorkbenchControl w) return w;
                 // It might be inside a Container?
                 // Workbench is Dock Style Fill in Form? Or inside a panel?
                 // Setup layout shows `this.Controls.Add(_workbench)`. Wait.
                 // Verify SetupCustomLayout in MainForm.
             }
             return null;
        }
    }
}
