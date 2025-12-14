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
        
            // State persistence
            CommandType currentMode = CommandType.Travel; 
            float currentX = 0;
            float currentY = 0;
            float currentSpeed = 0;
            float currentPower = 0;
            bool isLaserOn = false; 

            if (lines.Length > 0)
            {
               // Scan for initial mode? Default is usually G0.
            }

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

                var parts = line.Split(' ');
                bool hasMove = false;
                float newX = currentX;
                float newY = currentY;

                foreach (var part in parts)
                {
                    if (part.StartsWith("G0")) { currentMode = CommandType.Travel; currentPower = 0; }
                    else if (part.StartsWith("G1")) { currentMode = CommandType.Cut; }
                    else if (part.StartsWith("M3") || part.StartsWith("M4")) { isLaserOn = true; }
                    else if (part.StartsWith("M5")) { isLaserOn = false; currentPower = 0; }
                    else if (part.StartsWith("X")) { newX = ParseValue(part.Substring(1)); hasMove = true; }
                    else if (part.StartsWith("Y")) { newY = ParseValue(part.Substring(1)); hasMove = true; }
                    else if (part.StartsWith("S")) { currentPower = ParseValue(part.Substring(1)); }
                    else if (part.StartsWith("F")) { currentSpeed = ParseValue(part.Substring(1)); }
                }

                // If this line caused a move, assign the current mode
                if (hasMove)
                {
                    cmd.Type = currentMode;
                    currentX = newX;
                    currentY = newY;
                }
                else
                {
                    // Even if no move, if G0/G1 was explicit, maybe mark it?
                    // But for visualization we care about segments.
                    // If it's just "G1", it changes mode but no line segment.
                    // "Other" is fine for non-move lines.
                    // EXCEPT if G1 is set, cmd.Type is defaulted to Other. 
                }

                // Update Start/End
                cmd.End = new PointF(newX, newY);
                cmd.Power = isLaserOn ? currentPower : 0; 
                if (!isLaserOn) cmd.Power = 0;

                // User Request: Treat Power 0 as Travel
                if (cmd.Type == CommandType.Cut && cmd.Power <= 0)
                {
                    cmd.Type = CommandType.Travel;
                }
                
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
