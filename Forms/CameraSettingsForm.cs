/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Windows.Forms;
using grbl_burn_em.Controls;

namespace grbl_burn_em.Forms
{
    public class CameraSettingsForm : Form
    {
        private CameraControl _cameraControl = null!;

        public CameraSettingsForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Camera Settings";
            this.Size = new System.Drawing.Size(460, 700);
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
