using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.ComponentModel;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em.Forms
{
    public class MathShapeForm : Form
    {
        public LaserPath? ResultPath { get; private set; }

        private ComboBox _cmbShapeType = null!;
        private PropertyGrid _propertyGrid = null!;
        private PictureBox _previewBox = null!;
        private Button _btnOk = null!;
        private Button _btnCancel = null!;

        private ShapeParameters _currentParams = null!;

        public MathShapeForm()
        {
            InitializeComponent();
            _cmbShapeType.SelectedIndex = 0;
        }

        private void InitializeComponent()
        {
            this.Text = "Mathematical Shape Generator";
            this.Size = new Size(800, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 250
            };

            // Left Panel: Controls
            var leftPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                Padding = new Padding(10)
            };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Shape Selector
            var lblShape = new Label { Text = "Shape Type:", AutoSize = true };
            _cmbShapeType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbShapeType.Items.AddRange(Enum.GetNames(typeof(MathShapeType)));
            _cmbShapeType.SelectedIndexChanged += OnShapeTypeChanged;
            
            var topFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
            topFlow.Controls.Add(lblShape);
            topFlow.Controls.Add(_cmbShapeType);
            leftPanel.Controls.Add(topFlow, 0, 0);

            // Properties
            _propertyGrid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                ToolbarVisible = false,
                HelpVisible = true
            };
            _propertyGrid.PropertyValueChanged += (s, e) => 
            {
                 if (e.ChangedItem != null && e.ChangedItem.Label == "Definitions")
                 {
                     _propertyGrid.SelectedObject = _propertyGrid.SelectedObject;
                 }
                 UpdatePreview();
            };
            leftPanel.Controls.Add(_propertyGrid, 0, 1);

            // Custom Panel (Hidden by default)
            _customPanel = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Fill, 
                FlowDirection = FlowDirection.TopDown,
                Visible = false,
                AutoScroll = true,
                WrapContents = false
            };
            
            _customPanel.Controls.Add(new Label { Text = "Formulas (x=..., y=...)", AutoSize = true });
            _txtFormula = new TextBox { Multiline = true, Height = 100, Width = 230, ScrollBars = ScrollBars.Vertical, Text = "x = t * cos(t)\r\ny = t * sin(t)", AcceptsReturn = true };
            _txtFormula.TextChanged += UpdateCustomParams;
            _customPanel.Controls.Add(_txtFormula);
            
            _customPanel.Controls.Add(new Label { Text = "Variables (a=5; b=10)", AutoSize = true });
            _txtDefinitions = new TextBox { Width = 230, Text = "a=10" };
            _txtDefinitions.TextChanged += UpdateCustomParams;
            _customPanel.Controls.Add(_txtDefinitions);
            
            var pnlStep = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            pnlStep.Controls.Add(new Label { Text = "Step:", Width = 40, TextAlign = ContentAlignment.MiddleLeft });
            _numStepSize = new NumericUpDown { DecimalPlaces = 3, Increment = 0.01M, Value = 0.1M, Width = 60 };
            _numStepSize.ValueChanged += UpdateCustomParams;
            pnlStep.Controls.Add(_numStepSize);
            
            pnlStep.Controls.Add(new Label { Text = "Max:", Width = 40, TextAlign = ContentAlignment.MiddleLeft });
            _numMaxSteps = new NumericUpDown { Minimum = 10, Maximum = 100000, Value = 63, Width = 70 };
            _numMaxSteps.ValueChanged += UpdateCustomParams;
            pnlStep.Controls.Add(_numMaxSteps);
            _customPanel.Controls.Add(pnlStep);

            var lblHelp = new Label 
            { 
                Text = "Functions:\nsin, cos, tan, sqrt, pow, abs, floor, ceil, min, max, log, pi, e\n\nExample:\nx = t * 10\ny = t * t",
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font(FontFamily.GenericSansSerif, 8)
            };
            _customPanel.Controls.Add(lblHelp);

            leftPanel.Controls.Add(_customPanel, 0, 1); // Add to same cell as PropertyGrid

            // Buttons
            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            _btnOk = new Button { Text = "Create", DialogResult = DialogResult.OK };
            _btnOk.Click += OnOkClick;
            
            btnPanel.Controls.Add(_btnCancel);
            btnPanel.Controls.Add(_btnOk);
            leftPanel.Controls.Add(btnPanel, 0, 2);

            split.Panel1.Controls.Add(leftPanel);

            // Right Panel: Preview
            _previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D
            };
            _previewBox.Paint += OnPreviewPaint;
            _previewBox.Resize += (s, e) => _previewBox.Invalidate();
            split.Panel2.Controls.Add(_previewBox);

            this.Controls.Add(split);
            this.AcceptButton = _btnOk;
            this.CancelButton = _btnCancel;
        }

        private FlowLayoutPanel _customPanel = null!;
        private TextBox _txtFormula = null!;
        private TextBox _txtDefinitions = null!;
        private NumericUpDown _numStepSize = null!;
        private NumericUpDown _numMaxSteps = null!;

        private void UpdateCustomParams(object? sender, EventArgs e)
        {
            if (_currentParams is CustomShapeParameters csp)
            {
                csp.Formula = _txtFormula.Text;
                csp.Definitions = _txtDefinitions.Text;
                csp.StepSize = (float)_numStepSize.Value;
                csp.MaxSteps = (int)_numMaxSteps.Value;
                UpdatePreview();
            }
        }

        private void OnShapeTypeChanged(object? sender, EventArgs e)
        {
            if (_cmbShapeType.SelectedItem is string typeName)
            {
                if (Enum.TryParse(typeName, out MathShapeType type))
                {
                    bool isCustom = type == MathShapeType.Custom;
                    _propertyGrid.Visible = !isCustom;
                    _customPanel.Visible = isCustom;

                    switch (type)
                    {
                        case MathShapeType.Spiral: _currentParams = new SpiralParameters(); break;
                        case MathShapeType.SineWave: _currentParams = new SineWaveParameters(); break;
                        case MathShapeType.Polygon: _currentParams = new PolygonParameters(); break;
                        case MathShapeType.Star: _currentParams = new StarParameters(); break;
                        case MathShapeType.Rose: _currentParams = new RoseParameters(); break;
                        case MathShapeType.Custom: 
                            var csp = new CustomShapeParameters();
                            // Init UI from defaults
                            _txtFormula.Text = csp.Formula;
                            _txtDefinitions.Text = csp.Definitions;
                            _numStepSize.Value = (decimal)csp.StepSize;
                            _numMaxSteps.Value = csp.MaxSteps;
                            _currentParams = csp; 
                            break;
                    }
                    
                    if (!isCustom) _propertyGrid.SelectedObject = _currentParams;
                    UpdatePreview();
                }
            }
        }

        private void UpdatePreview()
        {
            _previewBox.Invalidate();
        }

        private void OnPreviewPaint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);

            if (_currentParams == null) return;

            var points = _currentParams.Generate();
            if (points.Count < 2) return;

            // Fit to view
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            float w = maxX - minX;
            float h = maxY - minY;
            if (w == 0) w = 1; 
            if (h == 0) h = 1;

            float scaleX = (_previewBox.Width - 40) / w;
            float scaleY = (_previewBox.Height - 40) / h;
            float scale = Math.Min(scaleX, scaleY);

            // Center
            e.Graphics.TranslateTransform(_previewBox.Width / 2f, _previewBox.Height / 2f);
            e.Graphics.ScaleTransform(scale, -scale); // Y-Up for math
            e.Graphics.TranslateTransform(-(minX + w / 2f), -(minY + h / 2f));

            using var pen = new Pen(Color.Blue, 0); // Hairline
            e.Graphics.DrawLines(pen, points.ToArray());
        }

        private void OnOkClick(object? sender, EventArgs e)
        {
            if (_currentParams != null)
            {
                var points = _currentParams.Generate();
                if (points.Count >= 2)
                {
                    ResultPath = new LaserPath
                    {
                        Name = _currentParams.GetType().Name.Replace("Parameters", "") + "Path",
                        Points = points
                    };
                    ResultPath.UpdateBounds();
                }
            }
            this.Close(); // Result is already OK from DialogResult property but we handled Click
        }
    }

    public enum MathShapeType
    {
        Spiral,
        SineWave,
        Polygon,
        Star,
        Rose,
        Custom
    }

    public abstract class ShapeParameters
    {
        public abstract List<PointF> Generate();
    }

    public class SpiralParameters : ShapeParameters
    {
        [Category("Spiral"), Description("Number of full turns")]
        public float Turns { get; set; } = 5;
        
        [Category("Spiral"), Description("Inner Radius")]
        public float InnerRadius { get; set; } = 0;
        
        [Category("Spiral"), Description("Outer Radius")]
        public float OuterRadius { get; set; } = 50;
        
        [Category("Simulation"), Description("Number of segments per turn")]
        public int Accuracy { get; set; } = 64;

        public override List<PointF> Generate()
        {
            var points = new List<PointF>();
            int steps = (int)(Turns * Accuracy);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps; // 0 to 1
                float angle = t * Turns * 2f * (float)Math.PI;
                float r = InnerRadius + t * (OuterRadius - InnerRadius);
                points.Add(new PointF(r * (float)Math.Cos(angle), r * (float)Math.Sin(angle)));
            }
            return points;
        }
    }

    public class SineWaveParameters : ShapeParameters
    {
        [Category("Wave"), Description("Total Length of the wave (mm)")]
        public float Length { get; set; } = 100;

        [Category("Wave"), Description("Number of cycles")]
        public float Cycles { get; set; } = 5;

        [Category("Wave"), Description("Amplitude (Height from center, mm)")]
        public float Amplitude { get; set; } = 10;

        [Category("Simulation"), Description("Points per cycle")]
        public int Accuracy { get; set; } = 32;

        public override List<PointF> Generate()
        {
            var points = new List<PointF>();
            int steps = (int)(Cycles * Accuracy);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = t * Length;
                float angle = t * Cycles * 2f * (float)Math.PI;
                float y = Amplitude * (float)Math.Sin(angle);
                points.Add(new PointF(x, y));
            }
            return points;
        }
    }

    public class PolygonParameters : ShapeParameters
    {
        [Category("Dimensions"), Description("Radius (mm)")]
        public float Radius { get; set; } = 50;

        [Category("Dimensions"), Description("Number of sides")]
        public int Sides { get; set; } = 6;
        
        [Category("Dimensions"), Description("Start Angle (degrees)")]
        public float StartAngle { get; set; } = 0;

        public override List<PointF> Generate()
        {
            var points = new List<PointF>();
            if (Sides < 3) return points;

            for (int i = 0; i <= Sides; i++) // <= to close loop
            {
                float angle = (StartAngle * (float)Math.PI / 180f) + i * 2f * (float)Math.PI / Sides;
                points.Add(new PointF(Radius * (float)Math.Cos(angle), Radius * (float)Math.Sin(angle)));
            }
            return points;
        }
    }

    public class StarParameters : ShapeParameters
    {
        [Category("Dimensions"), Description("Outer Radius (mm)")]
        public float OuterRadius { get; set; } = 50;

        [Category("Dimensions"), Description("Inner Radius (mm)")]
        public float InnerRadius { get; set; } = 20;

        [Category("Dimensions"), Description("Number of points")]
        public int Points { get; set; } = 5;

        public override List<PointF> Generate()
        {
            var points = new List<PointF>();
            if (Points < 3) return points;

            int totalSteps = Points * 2;
            for (int i = 0; i <= totalSteps; i++)
            {
                float angle = i * (float)Math.PI / Points - (float)Math.PI/2f; // Start at top
                float r = (i % 2 == 0) ? OuterRadius : InnerRadius;
                points.Add(new PointF(r * (float)Math.Cos(angle), r * (float)Math.Sin(angle)));
            }
            return points;
        }
    }
    
    public class RoseParameters : ShapeParameters
    {
        [Category("Dimensions"), Description("Radius (amplitude)")]
        public float Radius { get; set; } = 50;
        
        [Category("Rose"), Description("Numerator (n)")]
        public int N { get; set; } = 5;
        
        [Category("Rose"), Description("Denominator (d)")]
        public int D { get; set; } = 1;
        
        [Category("Simulation"), Description("Points per cycle")]
        public int Accuracy { get; set; } = 100;

        public override List<PointF> Generate()
        {
            var points = new List<PointF>();
            float k = (float)N / D;
            // Period depends on k
            // if k is integer, period is 2pi or pi
            // if k is rational, period is something else
            // Simplification: We need D * (2pi) if N,D relative prime and both odd? 
            // Full period is 2*pi * D (if N, D coprime, D odd) or pi * D?
            // Let's just do enough turns
            
            float turns = D;
            if (N % 2 == 0 && D % 2 == 0) turns = D; // Not coprime, simplification
            else if (D % 2 == 0) turns = 2 * D;
            else if (N % 2 == 0) turns = 2 * D;
            else turns = D;
            
            // This logic is complex, let's just use a high default or let user pick 'Range' in future.
            // For N/D where D=1, period is 2pi (if N even) or pi (if N odd).
            // Let's iterate 0 to 2*PI*D for safety.
            
            float maxAngle = 2f * (float)Math.PI * D;
            if (D==1 && N%2!=0) maxAngle = (float)Math.PI; // Odd petal count matches in PI range? No, rose(k) usually 2pi for closure? Rose(3) closes in pi. Rose(2) closes in 2pi.
            if (D==1 && N%2==1) maxAngle = (float)Math.PI; // actually Rose(3) is 3 petals in Pi.
            
            // Let's ALWAYS do 2*PI*D to be safe, filtering dupes is handled by laser optimization anyway?
            maxAngle = (float)Math.PI * 2f * D; 

            int steps = (int)(maxAngle / (Math.PI * 2f) * Accuracy * N); 
            if (steps < 100) steps = 100;

            for (int i = 0; i <= steps; i++)
            {
                float theta = maxAngle * i / steps;
                float r = Radius * (float)Math.Cos(k * theta);
                points.Add(new PointF(r * (float)Math.Cos(theta), r * (float)Math.Sin(theta)));
            }
            return points;
        }
    }
}
