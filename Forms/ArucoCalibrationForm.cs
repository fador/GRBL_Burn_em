using System;
using System.Drawing;
using System.Windows.Forms;
using laser_gui_test.Data;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.Aruco;

namespace laser_gui_test.Forms
{
    public class ArucoCalibrationForm : Form
    {
        private PictureBox _pbCamera = null!;
        private Button _btnAddFrame = null!;
        private Button _btnCalibrate = null!;
        private Button _btnReset = null!;
        private Label _lblStatus = null!;
        private int _framesCount = 0;
        
        private Mat? _currentFrameMat;
        private object _lock = new object();

        public ArucoCalibrationForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new System.Drawing.Size(800, 600);
            this.Text = "ArUco Camera Calibration";

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Camera
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Controls

            _pbCamera = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            layout.Controls.Add(_pbCamera, 0, 0);

            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnAddFrame = new Button { Text = "Capture Frame", Width = 120 };
            _btnAddFrame.Click += OnAddFrame;
            
            _btnCalibrate = new Button { Text = "Calculate", Width = 100 };
            _btnCalibrate.Click += OnCalibrate;
            
            _btnReset = new Button { Text = "Reset", Width = 80 };
            _btnReset.Click += OnReset;
            
            _lblStatus = new Label { Text = "Frames: 0", AutoSize = true, Padding = new Padding(5), TextAlign = ContentAlignment.MiddleLeft };

            controls.Controls.Add(_btnAddFrame);
            controls.Controls.Add(_btnCalibrate);
            controls.Controls.Add(_btnReset);
            controls.Controls.Add(_lblStatus);
            
            layout.Controls.Add(controls, 0, 1);
            this.Controls.Add(layout);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CameraManager.Instance.ResetCalibration();
            UpdateStatus();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CameraManager.Instance.FrameReceived += OnFrameReceivedInvoke;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CameraManager.Instance.FrameReceived -= OnFrameReceivedInvoke;
            lock(_lock)
            {
                _currentFrameMat?.Dispose();
                _currentFrameMat = null;
            }
            base.OnFormClosing(e);
        }

        private bool _isProcessing = false;

        private void OnFrameReceivedInvoke(Bitmap bmp)
        {
            if (this.IsDisposed) return;
            if (_isProcessing) return; // Drop frame if busy
             
            _isProcessing = true;
            
            // Process on THIS thread (Background Thread from CameraManager or Pool)
            // Do NOT Invoke yet.
            try 
            {
                using var clone = new Bitmap(bmp); // Work on clone
                
                // --- Heavy Processing Start ---
                using var mat = BitmapConverter.ToMat(clone);
                
                // Update Current Frame for Capture (Thread Safe clone)
                lock(_lock)
                {
                    _currentFrameMat?.Dispose();
                    _currentFrameMat = mat.Clone();
                }

                CameraManager.Instance.DetectArucoMarkers(mat, out var corners, out var ids);

                if (ids != null && ids.Length > 0)
                {
                    OpenCvSharp.Aruco.CvAruco.DrawDetectedMarkers(mat, corners, ids);
                }

                var preview = BitmapConverter.ToBitmap(mat);
                // --- Heavy Processing End ---

                // Invoke only for UI Update
                this.BeginInvoke(new Action(() => 
                {
                    if (!this.IsDisposed)
                    {
                        var old = _pbCamera.Image;
                        _pbCamera.Image = preview;
                        old?.Dispose();
                    }
                    else
                    {
                        preview.Dispose();
                    }
                    _isProcessing = false;
                }));
            } 
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Processing Error: {ex.Message}");
                _isProcessing = false;
            }
        }

        // ProcessFrame Removed (Inlined above for correct threading context)

        private void OnAddFrame(object? sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_currentFrameMat != null)
                {
                    bool added = CameraManager.Instance.AddCalibrationFrame(_currentFrameMat);
                    if (added)
                    {
                        _framesCount++;
                        UpdateStatus();
                        MessageBox.Show("Frame Added!", "info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("No Markers Detected!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }
        
        private void OnCalibrate(object? sender, EventArgs e)
        {
            if (_framesCount < 5)
            {
                MessageBox.Show("Need at least 5 frames.", "Info");
                return;
            }
            
            try
            {
                double error = CameraManager.Instance.CalibrateCameraAruco();
                MessageBox.Show($"Calibration Complete!\nReprojection Error: {error:F4}", "Success");
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Failed");
            }
        }
        
        private void OnReset(object? sender, EventArgs e)
        {
            CameraManager.Instance.ResetCalibration();
            _framesCount = 0;
            UpdateStatus();
        }
        
        private void UpdateStatus()
        {
            _lblStatus.Text = $"Frames Collected: {_framesCount} (Need 5+)";
        }
    }
}
