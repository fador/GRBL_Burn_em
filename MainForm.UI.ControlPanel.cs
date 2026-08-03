using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em;

public partial class MainForm
{
    private void InitializeControlPanel()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        
        var lblDevice = new Label { Text = "Device Profile:", AutoSize = true, Margin = new Padding(3, 5, 3, 0) };
        var cbDevices = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        
        Action refreshDevices = () => 
        {
            cbDevices.Items.Clear();
            foreach(var profile in AppConfiguration.Instance.MachineProfiles)
            {
                cbDevices.Items.Add(profile.Name);
            }
            int idx = AppConfiguration.Instance.MachineProfiles.FindIndex(p => p.Id == AppConfiguration.Instance.ActiveProfileId);
            if (idx >= 0) cbDevices.SelectedIndex = idx;
        };
        refreshDevices();

        cbDevices.SelectedIndexChanged += (s, e) =>
        {
            if (cbDevices.SelectedIndex >= 0)
            {
                AppConfiguration.Instance.ActiveProfileId = AppConfiguration.Instance.MachineProfiles[cbDevices.SelectedIndex].Id;
                AppConfiguration.Instance.Save();
                if (SerialInterface.Instance.IsConnected)
                {
                    SerialInterface.Instance.Disconnect();
                }
                if (_workbench != null) _workbench.Invalidate();
            }
        };

        flow.Controls.Add(lblDevice);
        flow.Controls.Add(cbDevices);

        var btnConnect = new Button { Text = "Connect", Width = 200 };
        
        // Connect Logic
        btnConnect.Click += (s, e) => 
        {
             if (SerialInterface.Instance.IsConnected)
             {
                 SerialInterface.Instance.Disconnect();
             }
             else
             {
                 string port = AppConfiguration.Instance.ActiveProfile.PortName;
                 int baud = AppConfiguration.Instance.ActiveProfile.BaudRate;
                 if (string.IsNullOrEmpty(port))
                 {
                     MessageBox.Show("Please select a COM port in Options.", "Configuration Missing");
                     return;
                 }
                 try
                 {
                     SerialInterface.Instance.Connect(port, baud);
                 }
                 catch (Exception ex)
                 {
                     MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 }
             }
        };

        // Status Update
        SerialInterface.Instance.ConnectionStatusChanged += (connected) => 
        {
            if (btnConnect.IsDisposed) return;
            btnConnect.Invoke(() => 
            {
                if (connected)
                {
                    btnConnect.Text = "Disconnect";
                    btnConnect.BackColor = Color.Salmon;
                    _lblStatusConnection.Text = "Connected";
                    _lblStatusConnection.ForeColor = Color.Green;
                    
                    // Request Settings to update Work Area
                    SerialInterface.Instance.Write("$$");
                }
                else
                {
                    btnConnect.Text = "Connect";
                    btnConnect.BackColor = Color.FromName("Control");
                    _lblStatusConnection.Text = "Disconnected";
                    _lblStatusConnection.ForeColor = Color.Red;
                }
            });
        };
        
        SerialInterface.Instance.LineReceived += (line) => 
        {
             // Parse $130=... (X Max) and $131=... (Y Max)
             // Format: $130=200.000
             if (line.StartsWith("$130="))
             {
                 if (float.TryParse(line.Substring(5), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float xMax))
                 {
                     if (xMax > 0) 
                     {
                         AppConfiguration.Instance.ActiveProfile.WorkAreaWidth = xMax;
                         AppConfiguration.Instance.Save();
                         _workbench.Invalidate();
                     }
                 }
             }
             else if (line.StartsWith("$131="))
             {
                 if (float.TryParse(line.Substring(5), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float yMax))
                 {
                     if (yMax > 0) 
                     {
                         AppConfiguration.Instance.ActiveProfile.WorkAreaHeight = yMax;
                         AppConfiguration.Instance.Save();
                         _workbench.Invalidate();
                     }
                 }
             }
        };

        SerialInterface.Instance.StatusReceived += (state, pos) => 
        {
            if (_statusStrip.IsDisposed) return;
            _statusStrip.BeginInvoke(() => 
            {
                _lblStatusState.Text = $"State: {state}";
                _lblStatusPos.Text = $"Pos: {pos.X:F3}, {pos.Y:F3}";
                
                // Update Workbench Laser Position
                _workbench.LaserPosition = pos;
            });
        };

        // Wire Workbench Mouse Event
        if (_workbench != null)
        {
            _workbench.MousePositionChanged += (pos) => 
            {
                 if (this.IsDisposed) return;
                 this.Invoke(() => _lblMousePos.Text = $"Mouse: {pos.X:F2}, {pos.Y:F2}");
            };
        }
        
        _jobRunner.ProgressChanged += (curr, total) => 
        {
             if (_statusStrip.IsDisposed) return;
             _statusStrip.BeginInvoke(() => 
             {
                 _progressBar.Visible = true;
                 _progressBar.Maximum = total;
                 _progressBar.Value = Math.Min(curr, total);
             });
        };
        
        _jobRunner.JobCompleted += () => 
        {
             if (_statusStrip.IsDisposed) return;
             _statusStrip.Invoke(() => 
             {
                 _progressBar.Visible = false;
                 MessageBox.Show("Job Completed!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
             });
        };

        _jobRunner.JobFailed += (msg) => 
        {
             if (_statusStrip.IsDisposed) return;
             _statusStrip.Invoke(() => 
             {
                 _progressBar.Visible = false;
                 MessageBox.Show($"Job failed: {msg}", "Job Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
             });
        };

        _jobRunner.JobStopped += () => 
        {
             if (_statusStrip.IsDisposed) return;
             _statusStrip.Invoke(() => 
             {
                 _progressBar.Visible = false;
             });
        };

        var btnStart = new Button { Text = "Start", Width = 200, BackColor = Color.LightGreen };
        btnStart.Click += (s, e) => 
        {
            if (!SerialInterface.Instance.IsConnected)
            {
                MessageBox.Show("Not connected.", "Error");
                return;
            }
            
            // Generate GCode
            var generator = new GrblGenerator();
            var objects = ProjectState.Instance.Objects.ToList();
            
            if (!CheckSafetyBounds(objects)) return;

            var lines = generator.Generate(objects);
            _jobRunner.Start(lines);
        };


        var btnStop = new Button { Text = "STOP", Width = 200, BackColor = Color.Red, ForeColor = Color.White };
        btnStop.Click += (s, e) => 
        {
             _jobRunner.Stop();
        };

        var btnPause = new Button { Text = "Pause/Resume", Width = 200, BackColor = Color.Yellow };
        btnPause.Click += (s, e) => 
        {
            if (_jobRunner.IsPaused) _jobRunner.Resume();
            else _jobRunner.Pause();
        };

        flow.Controls.Add(btnConnect);
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        var flowGen = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var btnGenerate = new Button { Text = "G-Code", Width = 90, BackColor = Color.LightBlue };
        btnGenerate.Click += (s, e) => GenerateGCode();
        
        var btnPreview = new Button { Text = "Preview", Width = 90, BackColor = Color.LightYellow };
        btnPreview.Click += (s, e) => ShowPreview();
        
        flowGen.Controls.Add(btnGenerate);
        flowGen.Controls.Add(btnPreview);
        flow.Controls.Add(flowGen);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });

        flow.Controls.Add(btnStart);

        flow.Controls.Add(btnPause);
        flow.Controls.Add(btnStop);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        _controlPanel.Controls.Add(flow);

        // Snapping Toggle
        var chkSnap = new CheckBox { Text = "Snap to Grid", AutoSize = true };
        chkSnap.CheckedChanged += (s, e) => { if (_workbench != null) _workbench.IsSnappingEnabled = chkSnap.Checked; };
        flow.Controls.Add(chkSnap);

        flow.Controls.Add(new Label { Text = "--------", AutoSize = true });
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 

        // Drawing Framing
        var grpFraming = new GroupBox { Text = "Alignment / Marking", Width = 200, Height = 220 };
        var flowFraming = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        
        var lblPwr = new Label { Text = "Power (%):", AutoSize = true };
        var numFramePower = new NumericUpDown { Minimum = 0, Maximum = 100, Value = (decimal)AppConfiguration.Instance.FramingPower };
        
        var lblSpd = new Label { Text = "Speed:", AutoSize = true };
        var numFrameSpeed = new NumericUpDown { Minimum = 100, Maximum = 10000, Value = (decimal)AppConfiguration.Instance.FramingSpeed, Increment = 100 };

        var btnFrame = new Button { Text = "Frame All Bound", Width = 180, BackColor = Color.LightYellow };
        var btnOutline = new Button { Text = "Outline Objects", Width = 180, BackColor = Color.LightCyan };
        var btnMark = new Button { Text = "Mark Centers (X)", Width = 180, BackColor = Color.LightCyan };
        
        btnFrame.Click += (s, e) => 
        {
            AppConfiguration.Instance.FramingPower = (float)numFramePower.Value;
            AppConfiguration.Instance.FramingSpeed = (float)numFrameSpeed.Value;
            AppConfiguration.Instance.Save();
            
            var objects = ProjectState.Instance.Objects.ToList();
            if (!CheckSafetyBounds(objects)) return;
            
            var gen = new GrblGenerator();
            var lines = gen.GenerateFraming(objects, AppConfiguration.Instance.FramingPower, AppConfiguration.Instance.FramingSpeed);
            
            _jobRunner.Start(lines);
        };

        
        btnOutline.Click += (s, e) => 
        {
             AppConfiguration.Instance.FramingPower = (float)numFramePower.Value;
             AppConfiguration.Instance.FramingSpeed = (float)numFrameSpeed.Value;
             AppConfiguration.Instance.Save();

             var gen = new GrblGenerator();
             var objects = ProjectState.Instance.SelectedObjects.Any() ? ProjectState.Instance.SelectedObjects : ProjectState.Instance.Objects.ToList();
             if (!CheckSafetyBounds(objects)) return;

             var lines = gen.GenerateObjectOutlines(objects, AppConfiguration.Instance.FramingPower, AppConfiguration.Instance.FramingSpeed);
             _jobRunner.Start(lines);
        };


        btnMark.Click += (s, e) => 
        {
             AppConfiguration.Instance.FramingPower = (float)numFramePower.Value;
             AppConfiguration.Instance.FramingSpeed = (float)numFrameSpeed.Value;
             AppConfiguration.Instance.Save();

             var gen = new GrblGenerator();
             var objects = ProjectState.Instance.SelectedObjects.Any() ? ProjectState.Instance.SelectedObjects : ProjectState.Instance.Objects.ToList();
             if (!CheckSafetyBounds(objects)) return;

             var lines = gen.GenerateCenterMarks(objects, AppConfiguration.Instance.FramingPower, AppConfiguration.Instance.FramingSpeed);
             _jobRunner.Start(lines);
        };


        flowFraming.Controls.Add(lblPwr);
        flowFraming.Controls.Add(numFramePower);
        flowFraming.Controls.Add(lblSpd);
        flowFraming.Controls.Add(numFrameSpeed);
        flowFraming.Controls.Add(btnFrame);
        flowFraming.Controls.Add(btnOutline);
        flowFraming.Controls.Add(btnMark);
        grpFraming.Controls.Add(flowFraming);
        flow.Controls.Add(grpFraming);
        
        flow.Controls.Add(new Label { Text = "--------", AutoSize = true }); 
        if (_workbench != null) _workbench.Invalidate();

        if (_controlPanel != null) _controlPanel.Controls.Add(flow);
    }
}
