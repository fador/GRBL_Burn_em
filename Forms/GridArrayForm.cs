/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
namespace grbl_burn_em.Forms;

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;

public class GridArrayForm : Form
{
    // Use cached parameters if form is closed (DialogResult.OK)
    public ArrayParameters Parameters => _cachedParameters ?? GetParameters();
    private ArrayParameters? _cachedParameters = null;

    private NumericUpDown _numRows = null!;
    private NumericUpDown _numCols = null!;
    private NumericUpDown _numGapX = null!;
    private NumericUpDown _numGapY = null!;
    
    private NumericUpDown _numRowRotStep = null!;
    private NumericUpDown _numColRotStep = null!;
    
    private NumericUpDown _numRandRotMin = null!;
    private NumericUpDown _numRandRotMax = null!;
    private NumericUpDown _numRandPosX = null!;
    private NumericUpDown _numRandPosY = null!;
    private NumericUpDown _numRandScaleMin = null!;
    private NumericUpDown _numRandScaleMax = null!;
    
    private PictureBox _previewBox = null!;
    private Button _btnRandomize = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;
    
    private int _currentSeed;
    private ArrayLayoutGenerator _generator;
    
    // Cache source for preview
    private List<LaserObject> _previewSource = new();

    public GridArrayForm()
    {
        InitializeComponent();
        _currentSeed = new Random().Next();
        _generator = new ArrayLayoutGenerator(_currentSeed);
        
        // Grab current selection for preview
        if (ProjectState.Instance.SelectedObjects.Count > 0)
        {
            // Clone them so we don't modify originals during preview generation (though generator clones too)
            // Actually Generator takes source and Clones. We just need to pass reference.
            _previewSource = ProjectState.Instance.SelectedObjects.ToList();
        }
        
        // Hook form closing to cache parameters before controls are disposed
        this.FormClosing += (s, e) => 
        {
            if (this.DialogResult == DialogResult.OK)
            {
                _cachedParameters = GetParameters();
            }
        };
        
        UpdatePreview();
    }

    private ArrayParameters GetParameters()
    {
        return new ArrayParameters
        {
            Rows = (int)_numRows.Value,
            Cols = (int)_numCols.Value,
            GapX = (float)_numGapX.Value,
            GapY = (float)_numGapY.Value,
            RowRotStep = (float)_numRowRotStep.Value,
            ColRotStep = (float)_numColRotStep.Value,
            RandomRotMin = (float)_numRandRotMin.Value,
            RandomRotMax = (float)_numRandRotMax.Value,
            RandomPosX = (float)_numRandPosX.Value,
            RandomPosY = (float)_numRandPosY.Value,
            RandomScaleMin = (float)_numRandScaleMin.Value,
            RandomScaleMax = (float)_numRandScaleMax.Value,
            Seed = _currentSeed
        };
    }

    private void InitializeComponent()
    {
        this.Text = "Create Array";
        this.Size = new Size(1100, 700);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = true;
        this.MinimizeBox = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = false,
            Panel1MinSize = 350,
            SplitterDistance = 350 
        };

        // Left Panel: Controls
        var leftPanel = new FlowLayoutPanel 
        { 
            Dock = DockStyle.Fill, 
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10),
            AutoScroll = true,
            WrapContents = false
        };
        
        // Helper to create grouped controls
        GroupBox CreateGroup(string title, int height)
        {
            return new GroupBox 
            { 
                Text = title, 
                Size = new Size(310, height), // Widen groupbox
                Margin = new Padding(0, 0, 0, 10)
            };
        }

        void AddField(Control parent, string label, out NumericUpDown num, decimal val, decimal min, decimal max, int yPos, int decimalPlaces = 0)
        {
            var lbl = new Label { Text = label, Location = new Point(10, yPos + 2), AutoSize = true };
            num = new NumericUpDown { Minimum = min, Maximum = max, Value = val, DecimalPlaces = decimalPlaces, Location = new Point(180, yPos), Width = 100 }; // Move num right
            num.ValueChanged += (s, e) => UpdatePreview();
            parent.Controls.Add(lbl);
            parent.Controls.Add(num);
        }

        // 1. Grid Settings
        var grpGrid = CreateGroup("Grid Dimensions", 120);
        AddField(grpGrid, "Rows:", out _numRows, 2, 1, 100, 20);
        AddField(grpGrid, "Columns:", out _numCols, 2, 1, 100, 45);
        AddField(grpGrid, "Gap X (mm):", out _numGapX, 5, -1000, 1000, 70, 2);
        AddField(grpGrid, "Gap Y (mm):", out _numGapY, 5, -1000, 1000, 95, 2); // Increased height to fit
        grpGrid.Height = 130;
        leftPanel.Controls.Add(grpGrid);
        
        // 2. Incremental Rotation
        var grpIncRot = CreateGroup("Incremental Rotation", 80);
        AddField(grpIncRot, "Row Step (°):", out _numRowRotStep, 0, -360, 360, 20, 1);
        AddField(grpIncRot, "Col Step (°):", out _numColRotStep, 0, -360, 360, 45, 1);
        leftPanel.Controls.Add(grpIncRot);
        
        // 3. Randomization
        var grpRand = CreateGroup("Randomization", 180);
        AddField(grpRand, "Pos Jitter X (mm):", out _numRandPosX, 0, 0, 1000, 20, 2);
        AddField(grpRand, "Pos Jitter Y (mm):", out _numRandPosY, 0, 0, 1000, 45, 2);
        AddField(grpRand, "Rand Rot Min (°):", out _numRandRotMin, 0, -360, 360, 70, 1);
        AddField(grpRand, "Rand Rot Max (°):", out _numRandRotMax, 0, -360, 360, 95, 1);
        AddField(grpRand, "Scale Min (%):", out _numRandScaleMin, 100, 1, 500, 120, 0);
        AddField(grpRand, "Scale Max (%):", out _numRandScaleMax, 100, 1, 500, 145, 0);

        leftPanel.Controls.Add(grpRand);

        // Randomize Button
        _btnRandomize = new Button { Text = "New Random Seed", Width = 310, Height = 30 }; // Widen button
        _btnRandomize.Click += (s, e) => { _currentSeed = new Random().Next(); UpdatePreview(); };
        leftPanel.Controls.Add(_btnRandomize);
        
        // Buttons
        var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 310, Height = 40, Margin = new Padding(0, 10, 0, 0) }; // Widen panel
        _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        _btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
        btnPanel.Controls.Add(_btnCancel);
        btnPanel.Controls.Add(_btnOk);
        leftPanel.Controls.Add(btnPanel);
        
        split.Panel1.Controls.Add(leftPanel);
        
        // Right Panel: Preview
        _previewBox = new PictureBox 
        { 
            Dock = DockStyle.Fill, 
            BackColor = Color.White,
            BorderStyle = BorderStyle.Fixed3D 
        };
        _previewBox.Paint += OnPreviewPaint;
        split.Panel2.Controls.Add(_previewBox);

        this.Controls.Add(split);
        this.AcceptButton = _btnOk;
        this.CancelButton = _btnCancel;
        
        // Fix splitter distance not applying by setting it after load
        this.Load += (s, e) => { split.SplitterDistance = 380; };
    }
    
    private void UpdatePreview()
    {
        _previewBox.Invalidate();
    }
    
    private void OnPreviewPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        
        // 1. Generate Objects
        // Use live parameters for preview
        var content = _generator.Generate(_previewSource, GetParameters());
        if (content.Count == 0) return;
        
        // 2. Calculate Bounds to Fit View
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        
        foreach(var obj in content)
        {
            var b = obj.GetBounds();
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
        }
        
        float contentW = maxX - minX;
        float contentH = maxY - minY;
        
        if (contentW <= 0 || contentH <= 0) return;
        
        // Add padding
        float pad = 20;
        
        // Scale to fit
        float viewW = _previewBox.Width - pad * 2;
        float viewH = _previewBox.Height - pad * 2;
        
        float scale = Math.Min(viewW / contentW, viewH / contentH);
        if (scale > 10) scale = 10; // Cap zoom
        
        // Center
        float cx = minX + contentW / 2f;
        float cy = minY + contentH / 2f;
        
        float viewCx = _previewBox.Width / 2f;
        float viewCy = _previewBox.Height / 2f;
        
        g.TranslateTransform(viewCx, viewCy);
        g.ScaleTransform(scale, -scale); // Flip Y to match Workbench Y-Up
        g.TranslateTransform(-cx, -cy);
        
        // Draw
        foreach(var obj in content)
        {
            obj.Draw(g, 1.0f/scale);
        }
    }
}
