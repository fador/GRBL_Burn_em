using System;
using System.Drawing;
using System.Windows.Forms;
using laser_gui_test.Data;
using laser_gui_test.Forms;

namespace laser_gui_test.Controls
{
    public class CameraControl : UserControl
    {
        private ComboBox _cmbDevices;
        private Button _btnStartStop;
        private CheckBox _chkOverlay;
        private TrackBar _trkOpacity;
        private Button _btnCalibrate;
        
        // Manual Adjustments
        private NumericUpDown _nudX;
        private NumericUpDown _nudY;
        private NumericUpDown _nudWidth;
        private NumericUpDown _nudHeight;

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
            flowOverlay.Controls.Add(_nudX, 1, 2);
            
            // Y
            flowOverlay.Controls.Add(new Label { Text = "Y:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            _nudY = new NumericUpDown { DecimalPlaces = 2, Minimum = -5000, Maximum = 5000, Dock = DockStyle.Fill };
            flowOverlay.Controls.Add(_nudY, 1, 3);
            
            // W
            flowOverlay.Controls.Add(new Label { Text = "W:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            _nudWidth = new NumericUpDown { DecimalPlaces = 2, Minimum = 1, Maximum = 10000, Dock = DockStyle.Fill, Value = 100 };
            flowOverlay.Controls.Add(_nudWidth, 1, 4);

            // H
            flowOverlay.Controls.Add(new Label { Text = "H:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            _nudHeight = new NumericUpDown { DecimalPlaces = 2, Minimum = 1, Maximum = 10000, Dock = DockStyle.Fill, Value = 100 };
            flowOverlay.Controls.Add(_nudHeight, 1, 5);
            
            grpOverlay.Controls.Add(flowOverlay);
            layout.Controls.Add(grpOverlay);
            
            // Events for NUDs
            EventHandler updateVal = (s, e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            _nudX.ValueChanged += updateVal;
            _nudY.ValueChanged += updateVal;
            _nudWidth.ValueChanged += updateVal;
            _nudHeight.ValueChanged += updateVal;

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

        private Button _btnRefresh;

        private void UpdateUIState()
        {
            var isMounted = _chkMounted.Checked;
            _btnRefresh.Visible = isMounted;
            // Also might want to hide manual transforms if using calibration?
            // For now keep them available for manual tweak.
        }

        private void OnCalibrateClick(object sender, EventArgs e)
        {
            if (_chkMounted.Checked)
            {
                // Head Mounted Calibration
                // Workflow:
                // 1. User confirms they are over a target.
                // 2. We toggle overlay transparency? Or show Crosshair?
                // 3. User clicks "Confirm".
                // 4. We calculate offset assuming Target = Current Laser Position?
                // Wait, if Laser is at X,Y. Camera is at X_cam, Y_cam.
                // Camera sees Target (which is at X,Y).
                // Center of Camera Image = X_cam, Y_cam.
                // If Target is at Center of Image, then Camera Position = Target Position.
                // So Offset = CameraPos - HeadPos.
                // Usage: We know HeadPos (Machine Coords). We know Camera sees HeadPos. 
                // So Camera is Physically at HeadPos. Offset = 0.
                // BUT, usually Camera is offset from Head.
                // True Workflow:
                // 1. Burn Dot at (X0, Y0).
                // 2. Move Head to (X1, Y1) such that Camera Center is on Dot.
                // 3. Physical Camera is now at (X0, Y0).
                // 4. Head is at (X1, Y1).
                // 5. Offset = PhysicalCamera - Head = (X0 - X1, Y0 - Y1).
                
                var laserPos = SerialInterface.Instance.MachinePosition; // This assumes we track position
                var res = MessageBox.Show(
                    $"1. Pulse Laser to mark current spot (X:{laserPos.X}, Y:{laserPos.Y}).\n" +
                    "2. Release button and Jog machine until the Camera Crosshair is EXACTLY on that spot.\n" +
                    "3. Click OK here.",
                    "Head Mounted Calibration", MessageBoxButtons.OKCancel);
                
                if (res == DialogResult.OK)
                {
                     var newPos = SerialInterface.Instance.MachinePosition;
                     // Offset = OriginalSpot - NewSpot
                     // We need to know OriginalSpot.
                     // The User shouldn't move before Step 1? We don't know if they moved.
                     // Better: "Enter coordinates of the target you are looking at".
                     // Or assume they started at "Zero" relative to move?
                     
                     // Interactive:
                     // We can't know "OriginalSpot" unless we recorded it before they jogged.
                     // Let's assume they entered this mode AT the spot.
                     // So LaserPos IS the Spot.
                     
                     float spotX = laserPos.X;
                     float spotY = laserPos.Y;
                     
                     float currentHeadX = newPos.X;
                     float currentHeadY = newPos.Y;
                     
                     float offX = spotX - currentHeadX;
                     float offY = spotY - currentHeadY;
                     
                     AppConfiguration.Instance.CameraOverlayX = offX; // Reuse X/Y for Offset? 
                     // Or use CalibrationData
                     CameraManager.Instance.Calibration.OffsetX = offX;
                     CameraManager.Instance.Calibration.OffsetY = offY;
                     //AppConfiguration.Instance.Save(); // Save Calibration
                     
                     MessageBox.Show($"Calibrated! Offset: {offX}, {offY}");
                }
            }
            else
            {
                // Stationary Calibration
                using var form = new CalibrationForm();
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var imgPoints = form.SelectedPoints;
                    // Now Ask for World Points.
                    MessageBox.Show("Now click the 4 corresponding points on the Workbench Grid.\nUse the 'Measure' tool or simply Click-to-Select logic (Not implemented yet). \n\nFor now, we will simulate 4 corners of the work area: \n(0,0), (AreaW,0), (AreaW, AreaH), (0, AreaH).", "Step 2");
                    
                    // TODO: Interactive World Point Selection.
                    // For MVP/Demo: Assume they clicked corners of the image which correspond to corners of bed.
                    // If they fill the camera view with the bed...
                    
                    // Let's just create a dummy "World Points" logic for now or rely on user typing them?
                    // Better: Compute Homography with simulated Bed Corners.
                    
                    var config = AppConfiguration.Instance;
                    // World Points (Bed Corners)
                    PointF[] worldPoints = {
                        new PointF(0, config.WorkAreaHeight), // Top-Left (Y-Up: Height)
                        new PointF(config.WorkAreaWidth, config.WorkAreaHeight), // Top-Right
                        new PointF(config.WorkAreaWidth, 0), // Bottom-Right
                        new PointF(0, 0) // Bottom-Left
                    };
                    
                    // Order matters! CalibrationForm click order must match.
                    // Re-Sort? Or instruct user: TL, TR, BR, BL.
                    
                    CameraManager.Instance.ComputeHomography(imgPoints, worldPoints);
                    MessageBox.Show("Homography Computed and Applied.");
                }
            }
        }
        
        private void OnRefreshClick(object sender, EventArgs e)
        {
             // Trigger Grid Scan
             if (MessageBox.Show("Start Workspace Scan? The machine will move to cover the work area.", "Scan", MessageBoxButtons.YesNo) == DialogResult.Yes)
             {
                 // Start Scan Job
                 CameraManager.Instance.StartScan();
             }
        }

        private CheckBox _chkMounted;

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

        private void OnStartStopClick(object sender, EventArgs e)
        {
            if (CameraManager.Instance.IsRunning)
            {
                CameraManager.Instance.StopCamera();
                // Button update handled by OnCameraStopped event
            }
            else
            {
                int index = _cmbDevices.SelectedIndex;
                if (index >= 0)
                {
                    var deviceName = _cmbDevices.SelectedItem.ToString();
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
