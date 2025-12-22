using System;
using System.Collections.Generic;
using System.Windows.Forms;
using grbl_burn_em.Data;

namespace grbl_burn_em.Forms;

public class SelectedObjectsForm : Form
{
    public SelectedObjectsForm(List<LaserObject> objects)
    {
        Text = "Selected Objects (Debug)";
        Size = new System.Drawing.Size(800, 400);
        StartPosition = FormStartPosition.CenterParent;

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = objects,
            ReadOnly = true,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            HeaderText = "Name", 
            DataPropertyName = "Name", 
            Width = 100 
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            HeaderText = "Type", 
            DataPropertyName = "Type", 
            Width = 80 
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            HeaderText = "Position", 
            DataPropertyName = "Position", 
            Width = 150 
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            HeaderText = "Size", 
            DataPropertyName = "Size", 
            Width = 150 
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn 
        { 
            HeaderText = "ID", 
            DataPropertyName = "Id", 
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill 
        });

        var panel = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(5) };
        var btnClose = new Button { Text = "Close", Dock = DockStyle.Right, Width = 100 };
        btnClose.Click += (s, e) => Close();
        
        panel.Controls.Add(btnClose);
        
        Controls.Add(grid);
        Controls.Add(panel);
    }
}
