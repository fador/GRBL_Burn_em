using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace laser_gui_test.Data.GCode;

public class GCodeParser
{
    public static List<GCodeCommand> Parse(string gcode)
    {
        var result = new List<GCodeCommand>();
        var lines = gcode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        float currentX = 0;
        float currentY = 0;
        float currentSpeed = 0;
        float currentPower = 0;
        bool isLaserOn = false; // M4/M3 status

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            // Comment
            if (line.StartsWith(";") || line.StartsWith("(")) continue;

            var cmd = new GCodeCommand
            {
                OriginalCommand = line,
                LineIndex = i,
                Start = new PointF(currentX, currentY),
                Type = CommandType.Other,
                Speed = currentSpeed,
                Power = currentPower
            };

            // Simple Parsing
            // We assume standard Grbl format: G0 X.. Y.., G1 X.. Y.. S..
            var parts = line.Split(' ');
            string gCmd = "";
            bool hasMove = false;
            float newX = currentX;
            float newY = currentY;

            foreach (var part in parts)
            {
                if (part.StartsWith("G0")) { gCmd = "G0"; }
                else if (part.StartsWith("G1")) { gCmd = "G1"; }
                else if (part.StartsWith("M3") || part.StartsWith("M4")) { isLaserOn = true; }
                else if (part.StartsWith("M5")) { isLaserOn = false; currentPower = 0; }
                else if (part.StartsWith("X")) { newX = ParseValue(part.Substring(1)); hasMove = true; }
                else if (part.StartsWith("Y")) { newY = ParseValue(part.Substring(1)); hasMove = true; }
                else if (part.StartsWith("S")) { currentPower = ParseValue(part.Substring(1)); }
                else if (part.StartsWith("F")) { currentSpeed = ParseValue(part.Substring(1)); }
            }

            if (gCmd == "G0")
            {
                cmd.Type = CommandType.Travel;
            }
            else if (gCmd == "G1")
            {
                cmd.Type = CommandType.Cut;
                // If S was not in this line, it retains previous S (Modal).
                // But G1 without move is just a setting change.
            }

            // Update State
            cmd.End = new PointF(newX, newY);
            cmd.Power = isLaserOn ? currentPower : 0; // If M5, Power is effectively 0 even if S is set? Grbl: M5 disables laser.
            // Actually S0 also disables laser.
            // If M5 is active, Power is 0 for visualization.
            if (!isLaserOn) cmd.Power = 0;
            
            // However, our generator emits "M4 S0".
            // Then "G1 X.. S.."
            // So if checking for G1, we should update power.
            
            if (hasMove)
            {
                currentX = newX;
                currentY = newY;
            }
            
            // Only add useful commands or all?
            // Let's add all, but mark Types.
            result.Add(cmd);
        }

        return result;
    }

    private static float ParseValue(string val)
    {
        if (float.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }
        return 0;
    }
}
