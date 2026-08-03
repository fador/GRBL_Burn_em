/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace grbl_burn_em.Data.GCode;

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
                    if (TryParseCode(part, out int code))
                    {
                        switch (code)
                        {
                            case 0: currentMode = CommandType.Travel; break; // G0 does NOT change S (modal)
                            case 1: currentMode = CommandType.Cut; break;
                            case 3:
                            case 4: isLaserOn = true; break;
                            case 5:
                            case 30: isLaserOn = false; currentPower = 0; break;
                        }
                    }
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

    /// <summary>
    /// Parses a G/M word with an exact numeric code, e.g. "G1" -> 1, "M30" -> 30,
    /// "M3S500" -> 3 (letter followed by digits, then optional words).
    /// Returns false for axis words ("X10") or anything without a numeric code.
    /// </summary>
    private static bool TryParseCode(string part, out int code)
    {
        code = 0;
        if (part.Length < 2) return false;
        char letter = part[0];
        if (letter != 'G' && letter != 'M') return false;

        int i = 1;
        while (i < part.Length && char.IsDigit(part[i])) i++;
        if (i == 1) return false;
        return int.TryParse(part.Substring(1, i - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
    }
}
