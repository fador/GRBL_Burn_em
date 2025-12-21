using System;
using System.Drawing;
using System.Windows.Forms;
using laser_gui_test.Data;

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
        
        private Bitmap? _currentFrame;
        private object _lock = new object();

        public ArucoCalibrationForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(800, 600);
            this.Text = "ArUco Camera Calibration (Disabled)";

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Camera
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Controls

            _pbCamera = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            layout.Controls.Add(_pbCamera, 0, 0);

            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnAddFrame = new Button { Text = "Capture Frame", Width = 120, Enabled = false };
            _btnAddFrame.Click += OnAddFrame;
            
            _btnCalibrate = new Button { Text = "Calculate", Width = 100, Enabled = false };
            _btnCalibrate.Click += OnCalibrate;
            
            _btnReset = new Button { Text = "Reset", Width = 80 };
            _btnReset.Click += OnReset;
            
            _lblStatus = new Label { Text = "ArUco not supported in this version", AutoSize = true, Padding = new Padding(5), TextAlign = ContentAlignment.MiddleLeft };

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
                _currentFrame?.Dispose();
                _currentFrame = null;
            }
            base.OnFormClosing(e);
        }

        private void OnFrameReceivedInvoke(Bitmap bmp)
        {
            if (this.IsDisposed) return;
            
            try 
            {
                var copy = new Bitmap(bmp);
                this.BeginInvoke(new Action(() => 
                {
                    var old = _pbCamera.Image;
                    _pbCamera.Image = copy;
                    old?.Dispose();
                }));
            } 
            catch { }
        }

        private void OnAddFrame(object? sender, EventArgs e)
        {
             // Stub
        }
        
        private void OnCalibrate(object? sender, EventArgs e)
        {
             // Stub
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
