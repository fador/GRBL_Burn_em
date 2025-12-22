using System;
using System.Collections.Generic;
using System.Drawing;
using grbl_burn_em.Data;

namespace grbl_burn_em.Data.Generators;

public class CalibrationGridGenerator
{
    public float MinSpeed { get; set; } = 1000;
    public float MaxSpeed { get; set; } = 5000;
    public float MinPower { get; set; } = 10;
    public float MaxPower { get; set; } = 100;
    public int Rows { get; set; } = 5; // Power Axis (Y)
    public int Cols { get; set; } = 5; // Speed Axis (X)
    public float CellSize { get; set; } = 10; // mm
    public float Spacing { get; set; } = 2; // mm
    public bool IsEngrave { get; set; } = false;
    public float StartX { get; set; } = 0;
    public float StartY { get; set; } = 0;

    public List<LaserObject> Generate()
    {
        var result = new List<LaserObject>();
        
        // Axis Labels
        // If Cut:
        //   X Axis: Speed
        //   Y Axis: Power
        // If Engrave:
        //   X Axis: Power (Variable)
        //   Y Axis: Speed (Constant Scan)

        string xTitle = IsEngrave ? "Power (%)" : "Speed (mm/min)";
        string yTitle = IsEngrave ? "Speed (mm/min)" : "Power (%)";
        
        float currentY = StartY;
        
        // Loop Rows (Y)
        for (int row = 0; row < Rows; row++)
        {
            float yVal; 
            if (IsEngrave)
                yVal = MinSpeed + (MaxSpeed - MinSpeed) * row / (Math.Max(1, Rows - 1));
            else
                yVal = MinPower + (MaxPower - MinPower) * row / (Math.Max(1, Rows - 1));

            float currentX = StartX;
            
            // Loop Cols (X)
            for (int col = 0; col < Cols; col++)
            {
                float xVal;
                if (IsEngrave)
                    xVal = MinPower + (MaxPower - MinPower) * col / (Math.Max(1, Cols - 1));
                else
                    xVal = MinSpeed + (MaxSpeed - MinSpeed) * col / (Math.Max(1, Cols - 1));

                float power = IsEngrave ? xVal : yVal;
                float speed = IsEngrave ? yVal : xVal;

                // Grid Cell
                var rect = new LaserRectangle
                {
                    Position = new PointF(currentX, currentY),
                    Size = new SizeF(CellSize, CellSize),
                    Power = power,
                    Speed = speed,
                    Mode = IsEngrave ? LayerMode.Fill : LayerMode.Cut,
                    Name = $"Grid P{power:0} S{speed:0}"
                };
                result.Add(rect);
                
                // If Engrave/Fill, add a Cut Outline on top?
                // Or user can use the Grid Overlay option?
                // Implementation plan said "Add Grid overlay".
                if (IsEngrave)
                {
                    var outline = new LaserRectangle
                    {
                         Position = new PointF(currentX, currentY),
                         Size = new SizeF(CellSize, CellSize),
                         Power = 0, // High speed, low power? or just default cut?
                         Speed = null, // Inherit
                         Mode = LayerMode.Cut,
                         Name = "Grid Outline"
                    };
                    // Actually, outline should probably use current layer settings or a specific cosmetic setting.
                    // Let's force it to be low power so it just marks? Or standard cut if the user wants to cut it out.
                    // Let's leave parameters null to inherit layer settings for the cutout.
                     result.Add(outline);
                }

                // X Axis Labels (Top Row)
                if (row == 0)
                {
                    var lblX = new LaserText
                    {
                        Text = $"{xVal:0}",
                        FontSize = 3,
                        Position = new PointF(currentX, StartY - 3),
                        Rotation = 0
                    };
                    lblX.UpdateTextSize();
                    result.Add(lblX);
                }
                
                currentX += CellSize + Spacing;
            }

            // Y Axis Label (Left of Row)
            var lblY = new LaserText
            {
                Text = $"{yVal:0}",
                FontSize = 3,
                Position = new PointF(StartX - 7, currentY + CellSize/2 - 1.5f)
            };
            lblY.UpdateTextSize();
            result.Add(lblY);

            currentY += CellSize + Spacing;
        }
        
        // Axis Titles
        var titleX = new LaserText
        {
            Text = xTitle,
            FontSize = 4,
            Position = new PointF(StartX, StartY - 8)
        };
        titleX.UpdateTextSize();
        result.Add(titleX);
        
        var titleY = new LaserText
        {
            Text = yTitle,
            FontSize = 4,
            Position = new PointF(StartX - 15, StartY + (Rows * (CellSize + Spacing)) / 2),
            Rotation = -90
        };
        titleY.UpdateTextSize();
        result.Add(titleY);

        return result;
    }
}
