using System;
using System.Drawing;
using System.Windows.Forms;
using laser_gui_test.Data;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Point = System.Drawing.Point;

namespace laser_gui_test.Forms
{
    public class OffsetCalibrationForm : Form
    {
        private PictureBox _pbCam = null!;
        private PointF _startPos;
        private Label _lblPos = null!;
        
        public OffsetCalibrationForm()
        {
            InitializeComponent();
            
            // Record Start Position (Assuming machine is idle)
            _startPos = SerialInterface.Instance.MachinePosition;
            if (_lblPos != null) _lblPos.Text = $"Start Pos: {_startPos.X:F3}, {_startPos.Y:F3}\nCurrent Pos: {_startPos.X:F3}, {_startPos.Y:F3}";
            
            CameraManager.Instance.FrameReceived += OnFrameReceived;
            SerialInterface.Instance.StatusReceived += OnStatusReceived;
            
            this.FormClosing += (s, e) => {
                 CameraManager.Instance.FrameReceived -= OnFrameReceived;
                 SerialInterface.Instance.StatusReceived -= OnStatusReceived;
            };
        }
        
        private void OnStatusReceived(string state, PointF pos)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => OnStatusReceived(state, pos)));
                return;
            }
            _lblPos.Text = $"Start Pos: {_startPos.X:F3}, {_startPos.Y:F3}\nCurrent Pos: {pos.X:F3}, {pos.Y:F3}";
        }

        private void InitializeComponent()
        {
            this.Size = new System.Drawing.Size(900, 600);
            this.Text = "Head-Mounted Camera Offset Calibration";

            var split = new SplitContainer { Dock = DockStyle.Fill };
            this.Controls.Add(split);

            // Left: Camera View
            _pbCam = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            _pbCam.Paint += OnCameraPaint;
            split.Panel1.Controls.Add(_pbCam);

            // Right: Controls
            var pnlRight = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            split.Panel2.Controls.Add(pnlRight);
            
            pnlRight.Controls.Add(new Label { Text = "Instructions:", Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), AutoSize = true });
            pnlRight.Controls.Add(new Label { Text = "1. Ensure Laser is focused on a scrap material.\n2. Click 'Pulse Laser' to mark the start spot.\n3. Use Jog controls to move the machine until the\n   Camera Crosshair is EXACTLY on the burn mark.\n4. Click 'Confirm Offset'.", AutoSize = true, Width = 280, Height = 100 });

            _lblPos = new Label { Text = "Pos: 0,0", AutoSize = true };
            pnlRight.Controls.Add(_lblPos);

            var btnPulse = new Button { Text = "Pulse Laser", Width = 200, Height = 40, BackColor = Color.Salmon, ForeColor = Color.White };
            btnPulse.Click += (s, e) => {
                // Pulse laser: M3 S100, G4 P0.5, M5
                SerialInterface.Instance.Write("M3 S100\n"); // Weak power for marking? Or S1000? 
                // Let's assume S50 is enough for diode, or S100.
                // Depending on machine max S value (usually 1000). S50 = 5%. 
                
                // Fire and wait
                System.Threading.Tasks.Task.Delay(200).ContinueWith(t => SerialInterface.Instance.Write("M5\n"));
            };
            pnlRight.Controls.Add(btnPulse);
            
            pnlRight.Controls.Add(new Label { Text = "Jog Controls:", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
            
            // Jog Grid
            var pnlJog = new TableLayoutPanel { RowCount = 3, ColumnCount = 3, AutoSize = true };
            pnlJog.Controls.Add(CreateJogBtn("Y+", 0, 10), 1, 0);
            pnlJog.Controls.Add(CreateJogBtn("X-", -10, 0), 0, 1);
            pnlJog.Controls.Add(new Label { Text = "" }, 1, 1);
            pnlJog.Controls.Add(CreateJogBtn("X+", 10, 0), 2, 1);
            pnlJog.Controls.Add(CreateJogBtn("Y-", 0, -10), 1, 2);
            pnlRight.Controls.Add(pnlJog);
            
            var btnConfirm = new Button { Text = "Confirm Offset", Width = 200, Height = 50, BackColor = Color.LightGreen };
            btnConfirm.Click += OnConfirmClick;
            pnlRight.Controls.Add(btnConfirm);
        }
        
        private Button CreateJogBtn(string text, float x, float y)
        {
            var btn = new Button { Text = text, Width = 60, Height = 60 };
            btn.MouseDown += (s, e) => {
                // Jog
                // $J=G91 X.. Y.. F..
                string cmd = $"$J=G91 X{x} Y{y} F1000\n";
                SerialInterface.Instance.Write(cmd);
            };
            return btn;
        }

        private void OnFrameReceived(Bitmap bmp)
        {
            try
            {
                 // Clone for UI
                 var copy = new Bitmap(bmp);
                 this.BeginInvoke(new Action(()=>
                 {
                     var old = _pbCam.Image;
                     _pbCam.Image = copy;
                     old?.Dispose();
                     // Invalidate to paint crosshair? PictureBox paints image then Paint event.
                 }));
            }
            catch {}
        }
        
        private void OnCameraPaint(object? sender, PaintEventArgs e)
        {
             // Draw Crosshair
             var w = _pbCam.Width;
             var h = _pbCam.Height;
             var cx = w / 2;
             var cy = h / 2;
             
             // Draw Green Cross
             using var pen = new Pen(Color.LimeGreen, 2);
             e.Graphics.DrawLine(pen, cx - 20, cy, cx + 20, cy);
             e.Graphics.DrawLine(pen, cx, cy - 20, cx, cy + 20);
             e.Graphics.DrawEllipse(pen, cx - 10, cy - 10, 20, 20);
        }
        
        private void OnConfirmClick(object? sender, EventArgs e)
        {
             // Logic:
             // StartPos (Machine Coords) = Where we burned the dot.
             // CurrentPos (Machine Coords) = Where the camera center is now.
             // So, the CAMERA (at CurrentPos) is looking at the DOT (at StartPos).
             // That means Physically, the Camera is at StartPos (over the dot).
             // Wait.
             // Let H = Head Position. C = Camera Position.
             // Relationship: C = H + Offset.
             // 1. At Start: Head is at H1. We burn a dot at H1. (Dot is at H1).
             // 2. We move Head to H2. Camera is now looking at the Dot. 
             //    So Camera Position C2 = Dot Position = H1.
             //    We know C2 = H2 + Offset.
             //    So H1 = H2 + Offset.
             //    Offset = H1 - H2.
             
             var current = SerialInterface.Instance.MachinePosition;
             float offX = _startPos.X - current.X;
             float offY = _startPos.Y - current.Y;
             
             var res = MessageBox.Show($"Calculated Offset:\nX: {offX:F3}\nY: {offY:F3}\n\nSave this offset?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
             if (res == DialogResult.Yes)
             {
                 var calib = CameraManager.Instance.Calibration;
                 calib.OffsetX = offX;
                 calib.OffsetY = offY;
                 calib.IsHeadMounted = true;
                 
                 CameraManager.Instance.SaveCalibration();
                 
                 // Also Update Legacy Config if needed?
                 AppConfiguration.Instance.CameraOverlayX = offX;
                 AppConfiguration.Instance.CameraOverlayY = offY; // Wait, overlay X/Y normally is pixel offset? 
                 // Actually in Controls/CameraControl.cs:
                 // _nudX.Value ... UpdateOverlay uses wbc.OverlayImagePosition.
                 // If that position is in mm?
                 // WorkbenchControl typically uses World Coordinates for the overlay.
                 // So OverlayImagePosition should be the Offset?
                 // Let's assume consistent unit usage (mm).
                 
                 AppConfiguration.Instance.Save();
                 
                 this.DialogResult = DialogResult.OK;
                 this.Close();
             }
        }
    }
}
