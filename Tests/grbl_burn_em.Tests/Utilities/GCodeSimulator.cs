using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace grbl_burn_em.Tests.Utilities;

public class GCodeSimulator
{
    private static readonly Regex GCodeRegex = new Regex(@"([A-Z])([-+]?[0-9]*\.?[0-9]+)", RegexOptions.Compiled);

    public List<SimulatedPath> Paths { get; } = new();
    public List<SimulatedPoint> Points { get; } = new();

    public void Simulate(IEnumerable<string> gcodeLines)
    {
        PointF currentPos = new PointF(0, 0);
        float currentPower = 0;
        float currentFeedRate = 0;
        bool isAbsolute = true;

        SimulatedPath? activePath = null;

        foreach (var line in gcodeLines)
        {
            var cleanLine = line.Split(';')[0].Trim();
            if (string.IsNullOrWhiteSpace(cleanLine)) continue;

            var matches = GCodeRegex.Matches(cleanLine);
            var commands = new Dictionary<char, float>();
            foreach (Match match in matches)
            {
                char letter = match.Groups[1].Value[0];
                float value = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                commands[letter] = value;
            }

            if (commands.ContainsKey('G'))
            {
                int g = (int)commands['G'];
                if (g == 0 || g == 1)
                {
                    float nextX = commands.ContainsKey('X') ? (isAbsolute ? commands['X'] : currentPos.X + commands['X']) : currentPos.X;
                    float nextY = commands.ContainsKey('Y') ? (isAbsolute ? commands['Y'] : currentPos.Y + commands['Y']) : currentPos.Y;
                    
                    float movePower = currentPower;
                    if (g == 0) movePower = 0; // G0 is travel
                    if (commands.ContainsKey('S')) movePower = commands['S'];
                    
                    if (commands.ContainsKey('F')) currentFeedRate = commands['F'];

                    PointF nextPos = new PointF(nextX, nextY);

                    if (movePower > 0)
                    {
                        if (activePath == null)
                        {
                            activePath = new SimulatedPath { Power = movePower };
                            activePath.Points.Add(currentPos);
                            Paths.Add(activePath);
                        }
                        activePath.Points.Add(nextPos);
                    }
                    else
                    {
                        activePath = null;
                    }

                    currentPos = nextPos;
                    Points.Add(new SimulatedPoint { Position = currentPos, Power = movePower });
                    
                    // Update global currentPower if S was present
                    if (commands.ContainsKey('S')) currentPower = commands['S'];
                }
                else if (g == 90)
                {
                    isAbsolute = true;
                }
                else if (g == 91)
                {
                    isAbsolute = false;
                }
            }
            else if (commands.ContainsKey('M'))
            {
                int m = (int)commands['M'];
                if (m == 3 || m == 4)
                {
                    if (commands.ContainsKey('S')) currentPower = commands['S'];
                }
                else if (m == 5)
                {
                    currentPower = 0;
                    activePath = null;
                }
            }
            else if (commands.ContainsKey('S'))
            {
                currentPower = commands['S'];
                if (currentPower == 0) activePath = null;
            }
            else if (commands.ContainsKey('X') || commands.ContainsKey('Y'))
            {
                // Modal G0/G1 (assuming G1 if power > 0, but Grbl often stays in last mode)
                float nextX = commands.ContainsKey('X') ? (isAbsolute ? commands['X'] : currentPos.X + commands['X']) : currentPos.X;
                float nextY = commands.ContainsKey('Y') ? (isAbsolute ? commands['Y'] : currentPos.Y + commands['Y']) : currentPos.Y;
                
                float movePower = currentPower;
                if (commands.ContainsKey('S')) movePower = commands['S'];

                PointF nextPos = new PointF(nextX, nextY);

                if (movePower > 0)
                {
                    if (activePath == null)
                    {
                        activePath = new SimulatedPath { Power = movePower };
                        activePath.Points.Add(currentPos);
                        Paths.Add(activePath);
                    }
                    activePath.Points.Add(nextPos);
                }
                else
                {
                    activePath = null;
                }

                currentPos = nextPos;
                Points.Add(new SimulatedPoint { Position = currentPos, Power = movePower });
                
                if (commands.ContainsKey('S')) currentPower = commands['S'];
            }
        }
    }
}

public class SimulatedPath
{
    public List<PointF> Points { get; } = new();
    public float Power { get; set; }
}

public class SimulatedPoint
{
    public PointF Position { get; set; }
    public float Power { get; set; }
}
