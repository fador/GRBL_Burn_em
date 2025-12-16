using System;
using System.Drawing;
using System.Windows.Forms;
using laser_gui_test.Data;

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
        }

        private void InitializeComponent()
        {
            this.Size = new Size(300, 400);
            
            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(10),
                AutoScroll = true
            };

            // Device Selection
            layout.Controls.Add(new Label { Text = "Camera Device:", AutoSize = true });
            _cmbDevices = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            layout.Controls.Add(_cmbDevices);
            
            var btnRefresh = new Button { Text = "Refresh List", Width = 100 };
            btnRefresh.Click += (s, e) => RefreshDevices();
            layout.Controls.Add(btnRefresh);

            // Start/Stop
            _btnStartStop = new Button { Text = "Start Camera", Width = 250, Height = 40, BackColor = Color.LightGreen };
            _btnStartStop.Click += OnStartStopClick;
            layout.Controls.Add(_btnStartStop);

            layout.Controls.Add(new Label { Text = "", Height = 10 }); // Spacer

            // Overlay Settings
            var grpOverlay = new GroupBox { Text = "Overlay Settings", Width = 260, Height = 200 };
            var flowOverlay = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            
            _chkOverlay = new CheckBox { Text = "Enable Overlay", Checked = false };
            _chkOverlay.CheckedChanged += (s, e) => 
            {
                UpdateConfigFromUI(); // Save state
                if (MainForm.Instance != null) // Access existing instance
                {
                    // Logic to enable/disable rendering handled in Workbench via property?
                    // Or we just push Image=null if disabled.
                    UpdateOverlay();
                }
            };
            flowOverlay.Controls.Add(_chkOverlay);
            
            flowOverlay.Controls.Add(new Label { Text = "Opacity:", AutoSize = true });
            _trkOpacity = new TrackBar { Minimum = 0, Maximum = 100, Value = 50, Width = 240 };
            _trkOpacity.Scroll += (s, e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            flowOverlay.Controls.Add(_trkOpacity);

            // Manual Transforms
            // Position X, Y
            var pnlPos = new FlowLayoutPanel { AutoSize = true };
            _nudX = new NumericUpDown { DecimalPlaces = 2, Minimum = -1000, Maximum = 1000, Width = 60 };
            _nudY = new NumericUpDown { DecimalPlaces = 2, Minimum = -1000, Maximum = 1000, Width = 60 };
            pnlPos.Controls.AddRange(new Control[] { new Label { Text = "X:" }, _nudX, new Label { Text = "Y:" }, _nudY });
            flowOverlay.Controls.Add(pnlPos);

            // Size W, H
            var pnlSize = new FlowLayoutPanel { AutoSize = true };
            _nudWidth = new NumericUpDown { DecimalPlaces = 2, Minimum = 1, Maximum = 5000, Width = 60, Value = 100 };
            _nudHeight = new NumericUpDown { DecimalPlaces = 2, Minimum = 1, Maximum = 5000, Width = 60, Value = 100 };
            pnlSize.Controls.AddRange(new Control[] { new Label { Text = "W:" }, _nudWidth, new Label { Text = "H:" }, _nudHeight });
            flowOverlay.Controls.Add(pnlSize);
            
            // Events for NUDs
            EventHandler updateVal = (s, e) => { UpdateConfigFromUI(); UpdateOverlay(); };
            _nudX.ValueChanged += updateVal;
            _nudY.ValueChanged += updateVal;
            _nudWidth.ValueChanged += updateVal;
            _nudHeight.ValueChanged += updateVal;

            grpOverlay.Controls.Add(flowOverlay);
            layout.Controls.Add(grpOverlay);
            
            layout.Controls.Add(new Label { Text = "", Height = 10 }); // Spacer

            // Calibration & Mounting
            var pnlMount = new FlowLayoutPanel { AutoSize = true };
            _chkMounted = new CheckBox { Text = "Head Mounted Camera", Checked = AppConfiguration.Instance.CameraIsMounted, Width = 250 };
            _chkMounted.CheckedChanged += (s, e) => { AppConfiguration.Instance.CameraIsMounted = _chkMounted.Checked; AppConfiguration.Instance.Save(); };
            pnlMount.Controls.Add(_chkMounted);
            layout.Controls.Add(pnlMount);

            _btnCalibrate = new Button { Text = "Calibrate (Manual)", Width = 250 };
            _btnCalibrate.Click += (s, e) => StartManualCalibration(); // TODO
            layout.Controls.Add(_btnCalibrate);

            this.Controls.Add(layout);
            
            // Load Initial Values
            LoadSettings();
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
                _btnStartStop.Text = "Start Camera";
                _btnStartStop.BackColor = Color.LightGreen;
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
             // Update logic if parameters change but frame doesn't (static image?)
             // Or just wait for next frame.
             // If camera is stopped, we might want to clear overlay.
             if (!CameraManager.Instance.IsRunning && !AppConfiguration.Instance.ShowCameraOverlay)
             {
                 var wb = GetWorkbench();
                 if (wb != null)
                 {
                     var old = wb.OverlayImage;
                     wb.OverlayImage = null;
                     old?.Dispose();
                     wb.Invalidate();
                 }
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
