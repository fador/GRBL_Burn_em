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

    public float X { get; set; } = 0;
    public float Y { get; set; } = 0;
    public float Z { get; set; } = 0;

    public string State { get; private set; } = "Idle";
    public bool IsLaserOn { get; private set; } = false;
    public float SpindleSpeed { get; private set; } = 0;
    public float FeedRate { get; private set; } = 1000;

    public bool IsAbsoluteMode { get; private set; } = true;
    public bool IsMmMode { get; private set; } = true;

    public float WorkAreaWidth { get; set; } = 400f;
    public float WorkAreaHeight { get; set; } = 400f;

    public event Action<string>? LogMessage;
    public event Action? StateChanged;
    public event Action<float, float, float>? BurnMark;

    private CancellationTokenSource? _moveCts;
    private CancellationTokenSource? _jogCts;

    public EmulatorLogic()
    {
        Task.Run(GCodeProcessingLoop);
    }

    public void ParseLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return;

        if (line == "?")
        {
            string status = $"<{State}|MPos:{X:F3},{Y:F3},{Z:F3}|FS:{FeedRate:F0},{SpindleSpeed:F0}>";
            TcpServer.Instance.Send(status + "\r\n");
            return;
        }
        if (line == "\u0018") { SoftReset(); return; }
        if (line == "$X") { HandleUnlock(); return; }
        if (line == "$$") { HandleSettingsDump(); return; }
        if (line.StartsWith("$J=")) { HandleJog(line); return; }
        if (line == "G90") { IsAbsoluteMode = true; TcpServer.Instance.Send("ok\r\n"); LogMessage?.Invoke("G90: Absolute mode"); return; }
        if (line == "G91") { IsAbsoluteMode = false; TcpServer.Instance.Send("ok\r\n"); LogMessage?.Invoke("G91: Relative mode"); return; }
        if (line == "G21") { IsMmMode = true; TcpServer.Instance.Send("ok\r\n"); return; }
        if (line == "G20") { IsMmMode = false; TcpServer.Instance.Send("ok\r\n"); return; }
        if (line == "G92") { X = 0; Y = 0; Z = 0; TcpServer.Instance.Send("ok\r\n"); LogMessage?.Invoke("G92: Origin set"); return; }
        if (line.StartsWith("G92")) { HandleG92(line); return; }

        TcpServer.Instance.EnqueueLine(line);
    }

    private void HandleUnlock()
    {
        State = "Idle";
        StateChanged?.Invoke();
        TcpServer.Instance.Send("ok\r\n");
        LogMessage?.Invoke("$X: Unlocked");
    }

    private void HandleSettingsDump()
    {
        string settings = $"$0=10\r\n$1=25\r\n$2=0\r\n$3=2\r\n$4=0\r\n" +
            $"$10=1\r\n$11=0.010\r\n$12=0.002\r\n$13=0\r\n" +
            $"$20=0\r\n$21=0\r\n$22=0\r\n$23=0\r\n" +
            $"$24=25.000\r\n$25=500.000\r\n$26=250\r\n$27=1.000\r\n" +
            $"$30=1000\r\n$31=0\r\n$32=0\r\n" +
            $"$100=800.000\r\n$101=800.000\r\n$102=250.000\r\n" +
            $"$110=500.000\r\n$111=500.000\r\n$112=500.000\r\n" +
            $"$120=10.000\r\n$121=10.000\r\n$122=10.000\r\n" +
            $"$130={WorkAreaWidth:F3}\r\n$131={WorkAreaHeight:F3}\r\n$132=200.000\r\n" +
            $"ok\r\n";
        TcpServer.Instance.Send(settings);
        LogMessage?.Invoke($"$$: Settings dump (work area {WorkAreaWidth}x{WorkAreaHeight})");
    }

    private void HandleG92(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var culture = CultureInfo.InvariantCulture;
        foreach (var part in parts)
        {
            if (part.StartsWith("X") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float xv)) X = xv;
            if (part.StartsWith("Y") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float yv)) Y = yv;
            if (part.StartsWith("Z") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float zv)) Z = zv;
        }
        TcpServer.Instance.Send("ok\r\n");
        LogMessage?.Invoke($"G92: Set pos to {X},{Y},{Z}");
    }

    private void HandleJog(string line)
    {
        _jogCts?.Cancel();
        _jogCts = new CancellationTokenSource();
        var token = _jogCts.Token;

        string paramsStr = line.Substring(3).Trim();
        bool jogAbsolute = false;
        float? jx = null, jy = null, jz = null, jf = null;

        var parts = paramsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var culture = CultureInfo.InvariantCulture;
        foreach (var part in parts)
        {
            if (part == "G90" || part.StartsWith("G90")) jogAbsolute = true;
            if (part == "G91" || part.StartsWith("G91")) jogAbsolute = false;
            if (part.StartsWith("X") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float xv)) jx = xv;
            if (part.StartsWith("Y") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float yv)) jy = yv;
            if (part.StartsWith("Z") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float zv)) jz = zv;
            if (part.StartsWith("F") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float fv)) jf = fv;
        }

        float moveSpeed = jf ?? FeedRate;
        if (moveSpeed <= 0) moveSpeed = 1000;

        float tX = jogAbsolute ? (jx ?? X) : X + (jx ?? 0);
        float tY = jogAbsolute ? (jy ?? Y) : Y + (jy ?? 0);
        float tZ = jogAbsolute ? (jz ?? Z) : Z + (jz ?? 0);

        LogMessage?.Invoke($"$J: {(jogAbsolute ? "G90" : "G91")} -> {tX:F1},{tY:F1} F{moveSpeed:F0}");

        _ = Task.Run(async () =>
        {
            try
            {
                await MoveToAsync(tX, tY, tZ, moveSpeed, token);
                State = "Idle";
                StateChanged?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Jog error: {ex.Message}");
            }
        }, token);
    }

    private void SoftReset()
    {
        _moveCts?.Cancel();
        _jogCts?.Cancel();

        TcpServer.Instance.ClearQueue();

        State = "Idle";
        IsLaserOn = false;
        SpindleSpeed = 0;
        IsAbsoluteMode = true;
        StateChanged?.Invoke();
        LogMessage?.Invoke("Soft Reset");
        TcpServer.Instance.Send("Grbl 1.1h ['$' for help]\r\n");
    }

    private async Task GCodeProcessingLoop()
    {
        while (true)
        {
            if (TcpServer.Instance.TryDequeueLine(out string? line) && line != null)
            {
                try
                {
                    await ProcessGCode(line!);
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
        string[] parts = line.ToUpper().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float? newX = null, newY = null, newZ = null;
        bool isMove = false, isRapid = false;
        bool hasM3 = false, hasM4 = false, hasM5 = false;

        var culture = CultureInfo.InvariantCulture;

        foreach (var part in parts)
        {
            if (part == "G0" || part == "G00") { isMove = true; isRapid = true; }
            if (part == "G1" || part == "G01") { isMove = true; isRapid = false; }
            if (part == "G90") { IsAbsoluteMode = true; }
            if (part == "G91") { IsAbsoluteMode = false; }

            if (part.StartsWith("X") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float xv)) newX = xv;
            if (part.StartsWith("Y") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float yv)) newY = yv;
            if (part.StartsWith("Z") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float zv)) newZ = zv;

            if (part == "M3" || part.StartsWith("M3")) hasM3 = true;
            if (part == "M4" || part.StartsWith("M4")) hasM4 = true;
            if (part == "M5" || part.StartsWith("M5")) hasM5 = true;

            if (part.StartsWith("S") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float sv)) SpindleSpeed = sv;
            if (part.StartsWith("F") && float.TryParse(part.Substring(1), NumberStyles.Any, culture, out float fv)) FeedRate = fv;
        }

        if (hasM3 || hasM4) IsLaserOn = true;
        if (hasM5) IsLaserOn = false;

        if (isMove && (newX.HasValue || newY.HasValue || newZ.HasValue))
        {
            float tX = IsAbsoluteMode ? (newX ?? X) : X + (newX ?? 0);
            float tY = IsAbsoluteMode ? (newY ?? Y) : Y + (newY ?? 0);
            float tZ = IsAbsoluteMode ? (newZ ?? Z) : Z + (newZ ?? 0);

            if (!isRapid && SpindleSpeed > 0) IsLaserOn = true;
            else if (!isRapid && SpindleSpeed == 0) IsLaserOn = false;

            _moveCts?.Cancel();
            _moveCts = new CancellationTokenSource();
            await MoveToAsync(tX, tY, tZ, isRapid ? 8000 : FeedRate, _moveCts.Token);
        }

        StateChanged?.Invoke();
    }

    private async Task MoveToAsync(float targetX, float targetY, float targetZ, float speed, CancellationToken token)
    {
        State = "Run";
        StateChanged?.Invoke();

        float startX = X, startY = Y, startZ = Z;
        float dist = MathF.Sqrt(
            MathF.Pow(targetX - startX, 2) +
            MathF.Pow(targetY - startY, 2) +
            MathF.Pow(targetZ - startZ, 2));

        if (dist < 0.001f)
        {
            X = targetX; Y = targetY; Z = targetZ;
            return;
        }

        float effectiveSpeed = speed > 0 ? speed : 100;
        float durationSec = dist / (effectiveSpeed / 60f);
        long durationMs = Math.Max((long)(durationSec * 1000), 10);

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            token.ThrowIfCancellationRequested();

            float t = Math.Clamp((float)sw.ElapsedMilliseconds / durationMs, 0f, 1f);
            X = startX + (targetX - startX) * t;
            Y = startY + (targetY - startY) * t;
            Z = startZ + (targetZ - startZ) * t;

            if (IsLaserOn && SpindleSpeed > 0 && !token.IsCancellationRequested)
                BurnMark?.Invoke(X, Y, SpindleSpeed);

            await Task.Delay(10, token);
        }

        X = targetX; Y = targetY; Z = targetZ;
    }
}
