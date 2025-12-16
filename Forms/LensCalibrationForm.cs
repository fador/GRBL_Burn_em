using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using laser_gui_test.Data;
using OpenCvSharp;
using OpenCvSharp.Extensions;

// Avoid ambiguity between System.Drawing.Size and OpenCvSharp.Size
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;
using Timer = System.Windows.Forms.Timer;

namespace laser_gui_test.Forms
{
    public class LensCalibrationForm : Form
    {
        private PictureBox _pbCam = null!;
        private ListBox _lstFrames = null!;
        private Label _lblStatus = null!;
        private Button _btnCapture = null!;
        private Button _btnCalibrate = null!;
        private Timer _uiTimer = null!;
        
        private List<Mat> _capturedFrames = new List<Mat>();
        private Mat? _currentMat;
        private object _lock = new object();
        
        // Config from Data
        private int _rows;
        private int _cols;
        private float _spacing;
        private CalibrationPatternType _type;

        public LensCalibrationForm()
        {
            InitializeComponent();
            
            var calib = CameraManager.Instance.Calibration;
            _rows = calib.PatternRows;
            _cols = calib.PatternCols;
            _spacing = calib.PatternSpacingMm;
            _type = calib.PatternType;
            
            CameraManager.Instance.FrameReceived += OnFrameReceived;
            
            _uiTimer = new Timer { Interval = 100 };
            _uiTimer.Tick += (s, e) => UpdateUI();
            _uiTimer.Start();
            
            // On Config Change (if we had a settings button), we would update vars.
        }

        private void InitializeComponent()
        {
            this.Size = new Size(900, 600);
            this.Text = "Lens Calibration (Circle Grid)";
            this.TopMost = true; // Keep form on top
            this.FormClosing += (s, e) => {
                 CameraManager.Instance.FrameReceived -= OnFrameReceived;
                 foreach(var m in _capturedFrames) m.Dispose();
                 _currentMat?.Dispose();
            };

            var split = new SplitContainer { Dock = DockStyle.Fill };
            this.Controls.Add(split);

            // Left: Camera
            _pbCam = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            split.Panel1.Controls.Add(_pbCam);

            // Right: Controls
            var pnlRight = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            split.Panel2.Controls.Add(pnlRight);
            
            pnlRight.Controls.Add(new Label { Text = "Instructions:", Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), AutoSize = true });
            pnlRight.Controls.Add(new Label { Text = "1. Print the calibration pattern (Asymmetric Circles).\n2. Hold it flat in front of camera.\n3. Capture 10-20 images from different angles.\n4. Ensure the colored grid is detected.", AutoSize = true, Width = 250, Height = 100 });
            
            _lblStatus = new Label { Text = "Status: Waiting...", AutoSize = true, ForeColor = Color.Blue };
            pnlRight.Controls.Add(_lblStatus);
            
            _btnCapture = new Button { Text = "Capture Frame", Width = 200, Height = 40, BackColor = Color.LightYellow };
            _btnCapture.Click += OnCaptureClick;
            pnlRight.Controls.Add(_btnCapture);
            
            _btnCalibrate = new Button { Text = "Calibrate Now", Width = 200, Height = 40, BackColor = Color.LightGreen, Enabled = false };
            _btnCalibrate.Click += OnCalibrateClick;
            pnlRight.Controls.Add(_btnCalibrate);

            var btnAuto = new Button { Text = "Start Auto Calibration", Width = 200, Height = 40, BackColor = Color.LightSkyBlue, Margin = new Padding(0, 10, 0, 0) };
            btnAuto.Click += OnAutoCalibrateClick;
            pnlRight.Controls.Add(btnAuto);
            
            pnlRight.Controls.Add(new Label { Text = "Captured Frames:", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
            _lstFrames = new ListBox { Width = 200, Height = 200 };
            pnlRight.Controls.Add(_lstFrames);
            
            var btnClear = new Button { Text = "Clear All", Width = 200 };
            btnClear.Click += (s, e) => {
                foreach(var m in _capturedFrames) m.Dispose();
                _capturedFrames.Clear();
                _lstFrames.Items.Clear();
                UpdateButtons();
            };
            pnlRight.Controls.Add(btnClear);
        }

        private void OnFrameReceived(Bitmap bmp)
        {
            // Run detection on a copy? Or on the bitmap?
            // Convert to Mat for detection
            // This runs on Camera Thread. 
            // detection might be slow, so maybe skip frames if busy?
            
            try
            {
                // Simple locking to grab latest frame for UI thread to process/draw?
                // Or process here and simple display on UI?
                // Process here is okay if fast.
                
                Mat mat = BitmapConverter.ToMat(bmp);
                
                // Detection for visualization
                var detected = CameraManager.Instance.DetectDotPattern(mat, mat, _rows, _cols, _type);
                
                // Convert back for display
                Bitmap display = BitmapConverter.ToBitmap(mat);
                
                lock(_lock)
                {
                    _currentMat?.Dispose();
                    _currentMat = mat; // Keep it if we want to capture
                    // Note: Mat is unmanaged. We need to be careful. 
                    // If we stored 'mat' in _currentMat, we should not dispose it yet.
                    
                    // Actually, let's clone for capture if clicked.
                    // For display, we pass 'display'.
                }
                
                this.BeginInvoke(new Action(() => 
                {
                    var old = _pbCam.Image;
                    _pbCam.Image = display;
                    old?.Dispose();
                    
                    if (detected != null)
                         _lblStatus.Text = "Pattern DETECTED";
                    else if (!_isAutoCalibrating) // Don't overwrite auto status
                         _lblStatus.Text = "Looking for pattern...";
                }));
            }
            catch {}
        }

        private void UpdateUI()
        {
             // Optional: Update status text color or simple anims
        }
        
        private void OnCaptureClick(object? sender, EventArgs e)
        {
            CaptureCurrentFrame();
        }

        private bool CaptureCurrentFrame()
        {
            Mat? capture = null;
            lock(_lock)
            {
                if (_currentMat != null && !_currentMat.IsDisposed)
                    capture = _currentMat.Clone();
            }
            
            if (capture != null)
            {
                // Verify detection again to be sure?
                // Or allows capturing bad frames? Better restrict to valid frames.
                var pts = CameraManager.Instance.DetectDotPattern(capture, null, _rows, _cols, _type);
                if (pts != null && pts.Length == _rows*_cols)
                {
                    _capturedFrames.Add(capture);
                    _lstFrames.Items.Add($"Frame {_capturedFrames.Count}");
                    UpdateButtons();
                    return true;
                }
                else
                {
                    if (!_isAutoCalibrating)
                        MessageBox.Show(this, "Pattern not detected in this frame. Please adjust angle/lighting.");
                    capture.Dispose();
                }
            }
            return false;
        }
        
        private void UpdateButtons()
        {
            _btnCalibrate.Enabled = _capturedFrames.Count >= 5;
            _btnCalibrate.Text = $"Calibrate ({_capturedFrames.Count} frames)";
        }
        
        private bool _isAutoCalibrating = false;

        private async void OnAutoCalibrateClick(object? sender, EventArgs e)
        {
            if (_isAutoCalibrating) return;
            
            if (MessageBox.Show(this, "Ensure the laser area is clear.\nThe machine will move around the current position.\nContinue?", "Auto Calibration", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            _isAutoCalibrating = true;
            _lblStatus.Text = "Starting Auto Calibration...";
            
            // Define offsets (mm) - 3x3 grid around center, spacing 15mm
            // Assumes current position is roughly centered over pattern
            var offsets = new List<(float x, float y)> 
            {
                (0, 0),
                (-15, 0), (15, 0),
                (0, -15), (0, 15),
                (-10, -10), (10, -10),
                (-10, 10), (10, 10)
            };

            try
            {
                // Store original pos
                // We assume we are in relative mode or we use relative moves
                // Let's use Relative Moves (G91) to be safer if we don't know absolute coords?
                // Or Get Current Pos, then Move To Absolute.
                // SerialInterface keeps track of MachinePosition.
                
                // Ensure we have a valid position
                if (SerialInterface.Instance.MachineState == "Unknown")
                {
                    MessageBox.Show(this, "Machine state unknown. Connect first.");
                    return;
                }

                PointF startPos = SerialInterface.Instance.MachinePosition;
                
                // Set to Relative Mode for moves? No, Absolute is better if we have startPos.
                // Let's use G0 for moves.
                
                foreach(var offset in offsets)
                {
                    if (!_isAutoCalibrating) break;

                    float targetX = startPos.X + offset.x;
                    float targetY = startPos.Y + offset.y;
                    
                    _lblStatus.Text = $"Moving to {offset.x}, {offset.y}...";
                    
                    // Move
                    SerialInterface.Instance.Write($"G0 X{targetX:F3} Y{targetY:F3}\n");
                    
                    // Wait for Idle
                    await WaitUntilIdle();
                    
                    // Wait a bit for vibration to settle
                    await System.Threading.Tasks.Task.Delay(500);
                    
                    _lblStatus.Text = "Capturing...";
                    
                    // Retry capture a few times if failed (maybe lighting/focus issue at edges)
                    bool captured = false;
                    for(int i=0; i<3; i++)
                    {
                        if (CaptureCurrentFrame())
                        {
                            captured = true;
                            break;
                        }
                        await System.Threading.Tasks.Task.Delay(500); // Wait and retry
                    }
                    
                    if (!captured)
                    {
                        // Log but continue? Or stop?
                        // Let's continue, maybe next pos is better.
                         _lblStatus.Text = "Capture Failed at this pos.";
                         await System.Threading.Tasks.Task.Delay(500);
                    }
                }
                
                // Return to start
                SerialInterface.Instance.Write($"G0 X{startPos.X:F3} Y{startPos.Y:F3}\n");
                await WaitUntilIdle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Auto Calibration Error: {ex.Message}");
            }
            finally
            {
                _isAutoCalibrating = false;
                _lblStatus.Text = "Auto Calibration Complete.";
            }
        }

        private async System.Threading.Tasks.Task WaitUntilIdle()
        {
             // Give time for command to start (buffer, parse, accelerator start)
             await System.Threading.Tasks.Task.Delay(250);

             // Simple polling loop
             while (true)
             {
                 if (SerialInterface.Instance.MachineState.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                     return;
                     
                 // If Alarm?
                 if (SerialInterface.Instance.MachineState.Contains("Alarm"))
                     throw new Exception("Machine Alarm triggered during move.");
                     
                 await System.Threading.Tasks.Task.Delay(100);
             }
        }
        
        private async void OnCalibrateClick(object? sender, EventArgs e)
        {
            _btnCalibrate.Enabled = false;
            _lblStatus.Text = "Calibrating... Please Wait...";
            
            double err = -1;
            double[]? camMatrix = null;
            double[]? distCoeffs = null;
            
            await System.Threading.Tasks.Task.Run(() => 
            {
                err = CameraManager.Instance.CalibrateCameraDots(
                    _capturedFrames, 
                    _rows, 
                    _cols, 
                    _spacing, 
                    _type, 
                    out var cm, 
                    out var dc
                );
                camMatrix = cm;
                distCoeffs = dc;
            });
            
            if (err >= 0 && camMatrix != null && distCoeffs != null)
            {
                MessageBox.Show(this, $"Calibration Successful!\nReprojection Error: {err:F4} px", "Result", MessageBoxButtons.OK, MessageBoxIcon.Information);
                
                // Save
                var calib = CameraManager.Instance.Calibration;
                calib.CameraMatrix = camMatrix;
                calib.DistCoeffs = distCoeffs;
                // calib.Pattern... already set?
                // Should we save the pattern used? Yes.
                calib.PatternRows = _rows;
                calib.PatternCols = _cols;
                calib.PatternSpacingMm = _spacing;
                calib.PatternType = _type;
                
                CameraManager.Instance.SaveCalibration();
                this.Close();
            }
            else
            {
                MessageBox.Show(this, "Calibration Failed. Ensure frames are distinct and pattern is clear.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnCalibrate.Enabled = true;
                _lblStatus.Text = "Failed.";
            }
        }
    }
}
