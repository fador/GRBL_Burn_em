using System;
using System.Windows.Forms;
using laser_gui_test.Controls;

namespace laser_gui_test.Forms
{
    public class CameraSettingsForm : Form
    {
        private CameraControl _cameraControl;

        public CameraSettingsForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Camera Settings";
            this.Size = new System.Drawing.Size(350, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true; // Keep on top of workbench

            _cameraControl = new CameraControl
            {
                Dock = DockStyle.Fill
            };
            
            this.Controls.Add(_cameraControl);
            
            // Handle closing - maybe hide instead of close?
            // For now, let it close and we create new one on open, or Singleton form?
            // Singleton form is better to keep state (device selection).
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
