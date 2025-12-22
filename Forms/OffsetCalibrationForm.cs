using System;
using System.Drawing;
using System.Windows.Forms;
using grbl_burn_em.Data;
using Point = System.Drawing.Point;

namespace grbl_burn_em.Forms
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
            this.Size = new Size(900, 600);
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
                SerialInterface.Instance.Write("M3 S100\n"); 
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
            
            var btnAuto = new Button { Text = "Auto Center (Burn & Scan)", Width = 200, Height = 40, BackColor = Color.LightSkyBlue };
            btnAuto.Click += OnAutoCenterClick;
            pnlRight.Controls.Add(btnAuto);

            var btnConfirm = new Button { Text = "Confirm Offset", Width = 200, Height = 50, BackColor = Color.LightGreen };
            btnConfirm.Click += OnConfirmClick;
            pnlRight.Controls.Add(btnConfirm);
        }
        
        private Button CreateJogBtn(string text, float x, float y)
        {
            var btn = new Button { Text = text, Width = 60, Height = 60 };
            btn.MouseDown += (s, e) => {
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
        
        private async void OnAutoCenterClick(object? sender, EventArgs e)
        {
            if (MessageBox.Show("This will move the machine.\nEnsure the camera can see the burn mark (roughly).\nProceed?", "Auto Center", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            _lblPos.Text = "Status: Auto Centering...";
            
            try
            {
                var p1 = await CaptureSpotLocation();
                if (p1 == null) throw new Exception("Could not find dark spot (burn mark). Adjust light/threshold.");
                
                float moveDist = 5.0f;
                PointF startMachinePos = SerialInterface.Instance.MachinePosition;
                
                await SerialInterface.Instance.MoveRelative(moveDist, 0);
                await System.Threading.Tasks.Task.Delay(500); 
                
                var p2 = await CaptureSpotLocation();
                if (p2 == null) throw new Exception("Lost spot after moving X.");
                
                await SerialInterface.Instance.MoveRelative(0, moveDist);
                await System.Threading.Tasks.Task.Delay(500); 
                
                var p3 = await CaptureSpotLocation();
                if (p3 == null) throw new Exception("Lost spot after moving Y.");
                
                float vx_x = p2.Value.X - p1.Value.X;
                float vx_y = p2.Value.Y - p1.Value.Y;
                
                float vy_x = p3.Value.X - p2.Value.X;
                float vy_y = p3.Value.Y - p2.Value.Y;
                
                float det = vx_x * vy_y - vx_y * vy_x;
                if (Math.Abs(det) < 0.1f) throw new Exception("Singular matrix. Movement not detected.");
                
                var img = _pbCam.Image;
                if (img == null) throw new Exception("Camera image is missing.");
                float cx = img.Width / 2f;
                float cy = img.Height / 2f;
                
                float du = cx - p3.Value.X; 
                float dv = cy - p3.Value.Y;
                
                float dX = (vy_y * du - vy_x * dv) / det * moveDist;
                float dY = (-vx_y * du + vx_x * dv) / det * moveDist;
                
                await SerialInterface.Instance.MoveRelative(dX, dY);
                await System.Threading.Tasks.Task.Delay(500);
                
                var pFinal = await CaptureSpotLocation();
                if (pFinal != null)
                {
                    float distErr = (float)Math.Sqrt(Math.Pow(pFinal.Value.X - cx, 2) + Math.Pow(pFinal.Value.Y - cy, 2));
                    _lblPos.Text = $"Centered! Err: {distErr:F1}px";
                    
                    if (distErr < 20) 
                    {
                         MessageBox.Show("Centered Successfully!");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task<PointF?> CaptureSpotLocation()
        {
            await System.Threading.Tasks.Task.Delay(200);
            
            if (_pbCam.Image == null) return null;
            
            var bmp = (Bitmap)_pbCam.Image.Clone();
            return await System.Threading.Tasks.Task.Run(() => Tools.ImageUtils.FindDarkestSpot(bmp));
        }

        private void OnConfirmClick(object? sender, EventArgs e)
        {
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
                 
                 AppConfiguration.Instance.CameraOverlayX = offX;
                 AppConfiguration.Instance.CameraOverlayY = offY; 
                 
                 AppConfiguration.Instance.Save();
                 
                 this.DialogResult = DialogResult.OK;
                 this.Close();
             }
        }
    }
}
