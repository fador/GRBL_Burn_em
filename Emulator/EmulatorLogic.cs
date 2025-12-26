using System;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace grbl_burn_em_emulator;

public class EmulatorLogic
{
    private static EmulatorLogic? _instance;
    public static EmulatorLogic Instance => _instance ??= new EmulatorLogic();

    public float X { get; private set; } = 0;
    public float Y { get; private set; } = 0;
    public float Z { get; private set; } = 0;
    
    // Machine State
    public string State { get; private set; } = "Idle";
    public bool IsLaserOn { get; private set; } = false;
    public float SpindleSpeed { get; private set; } = 0;
    
    // Feed Rate
    public float FeedRate { get; private set; } = 1000;
    
    public event Action<string>? LogMessage;
    public event Action? StateChanged;
    public event Action<float, float, float>? BurnMark;

    private System.Collections.Concurrent.ConcurrentQueue<string> _commandQueue = new();
    private CancellationTokenSource? _moveCts;

    public EmulatorLogic()
    {
        Task.Run(ProcessingLoop);
    }

    public void ParseLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return;
        
        // Real-time commands handled immediately
        if (line == "?")
        {
            string status = $"<{State}|MPos:{X:F3},{Y:F3},{Z:F3}|FS:{FeedRate:F0},{SpindleSpeed:F0}>";
            TcpServer.Instance.Send(status + "\r\n");
            return;
        }
        if (line == "\u0018") // Ctrl-X
        {
            SoftReset();
            return;
        }
        if (line == "$X")
        {
            // Unlock
            State = "Idle";
            StateChanged?.Invoke();
            TcpServer.Instance.Send("ok\r\n");
            return;
        }

        // Queue G-Code
        _commandQueue.Enqueue(line);
    }

    private void SoftReset()
    {
        _moveCts?.Cancel();
        
        // Clear Queue
        while (_commandQueue.TryDequeue(out _)) { }
        
        State = "Idle";
        IsLaserOn = false;
        SpindleSpeed = 0;
        StateChanged?.Invoke();
        LogMessage?.Invoke("Soft Reset");
        TcpServer.Instance.Send("Grbl 1.1h ['$' for help]\r\n");
    }

    private async Task ProcessingLoop()
    {
        while (true)
        {
            if (_commandQueue.TryDequeue(out string? line))
            {
                try
                {
                    await ProcessGCode(line);
                    TcpServer.Instance.Send("ok\r\n");
                }
                catch (Exception ex)
                {
                    TcpServer.Instance.Send($"error:{ex.Message}\r\n");
                }
            }
            else
            {
                await Task.Delay(10);
            }
        }
    }

    private async Task ProcessGCode(string line)
    {
        // Simple Parser with Invariant Culture
        string[] parts = line.ToUpper().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        float? newX = null;
        float? newY = null;
        bool isMove = false;
        bool isRapid = false;
        
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        foreach (var part in parts)
        {
            if (part.StartsWith("G0")) { isMove = true; isRapid = true; } // Covers G0 and G00
            if (part == "G00") { isMove = true; isRapid = true; } // Safety
            if (part.StartsWith("G1")) { isMove = true; isRapid = false; } // Covers G1 and G01
            if (part == "G01") { isMove = true; isRapid = false; }
            
            if (part.StartsWith("X")) if(float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float xVal)) newX = xVal;
            if (part.StartsWith("Y")) if(float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float yVal)) newY = yVal;
            
            if (part.StartsWith("M3")) { IsLaserOn = true; }
            if (part.StartsWith("M5")) { IsLaserOn = false; }
            
            if (part.StartsWith("S")) 
            {
                 if (float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float sVal)) SpindleSpeed = sVal;
            }
            
            if (part.StartsWith("F"))
            {
                 if (float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float fVal)) FeedRate = fVal;
            }
        }
        
        if (isMove && (newX.HasValue || newY.HasValue))
        {
            // User Request: Treat G1 with S>0 as Laser ON (Implicit M3)
            if (!isRapid && SpindleSpeed > 0) IsLaserOn = true;
            else IsLaserOn = false;
            
            await MoveTo(newX ?? X, newY ?? Y, isRapid ? 8000 : FeedRate); // 8000 max rapid
        }
        
        StateChanged?.Invoke();
    }
    
    private async Task MoveTo(float targetX, float targetY, float speed)
    {
        State = "Run";
        StateChanged?.Invoke();
        
        float startX = X;
        float startY = Y;
        float dist = MathF.Sqrt(MathF.Pow(targetX - startX, 2) + MathF.Pow(targetY - startY, 2));
        
        if (dist > 0.001)
        {
            // Calculate Duration
            // Speed is mm/min => mm/sec = speed / 60
            float outputSpeed = speed; 
            if(outputSpeed <= 0) outputSpeed = 100;
            
            float durationSec = dist / (outputSpeed / 60.0f);
            long durationMs = (long)(durationSec * 1000);
            
            // Should be at least 1 frame
            if (durationMs < 10) durationMs = 10;
            
            _moveCts = new CancellationTokenSource();
            var token = _moveCts.Token;

            Stopwatch sw = Stopwatch.StartNew();
            while(sw.ElapsedMilliseconds < durationMs)
            {
                if (token.IsCancellationRequested) return;

                float t = (float)sw.ElapsedMilliseconds / durationMs;
                X = startX + (targetX - startX) * t;
                Y = startY + (targetY - startY) * t;
                
                // If Laser is ON and Spindle > 0, Burn
                if (IsLaserOn && SpindleSpeed > 0)
                {
                    BurnAt(X, Y);
                }
                
                await Task.Delay(10); // Update rate
            }
        }
        
        X = targetX;
        Y = targetY;
        State = "Idle";
        StateChanged?.Invoke();
    }
    
    private void BurnAt(float x, float y)
    {
        BurnMark?.Invoke(x, y, SpindleSpeed);
    }

}
