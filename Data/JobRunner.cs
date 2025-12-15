using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Diagnostics;

namespace laser_gui_test.Data;

public class JobRunner
{
    private List<string> _gcodeLines = new List<string>();
    private int _currentLineIndex = 0;
    private int _pendingCommandsCount = 0;
    public int MaxPlannerBlocks { get; set; } = 15; // Default GRBL
    public int MaxBufferSize { get; set; } = 128; // Standard GRBL Rx Buffer
    private int _currentBytes = 0;
    private Queue<int> _sentLineLengths = new Queue<int>();

    private long _lastProgressTicks = 0;
    private const long ProgressInterval = 1000000; // 100ms 
    
    private bool _isRunning = false;
    private bool _isPaused = false;
    
    public event Action<int, int>? ProgressChanged; // Current, Total
    public event Action? JobCompleted;
    
    private Queue<int> _pendingCommands = new Queue<int>(); // Legacy

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;

    public JobRunner()
    {
        SerialInterface.Instance.LineReceived += OnSerialLineReceived;
    }

    public void Start(IEnumerable<string> gcode)
    {
        if (_isRunning) return;

        _gcodeLines = gcode.ToList();
        _currentLineIndex = 0;
        _pendingCommandsCount = 0;
        _pendingCommands.Clear();
        
        _currentBytes = 0;
        _sentLineLengths.Clear();
        
        _isRunning = true;
        _isPaused = false;
        
        SendNext();
    }

    public void Pause()
    {
        if (!_isRunning) return;
        _isPaused = true;
        SerialInterface.Instance.Write("!");
    }

    public void Resume()
    {
        if (!_isRunning || !_isPaused) return;
        _isPaused = false;
        SerialInterface.Instance.Write("~");
        SendNext();
    }

    public void Stop()
    {
        _isRunning = false;
        _isPaused = false;
        _gcodeLines.Clear();
        _pendingCommands.Clear();
        _pendingCommandsCount = 0;   
        _currentBytes = 0;
        _sentLineLengths.Clear();
        
        // Soft Reset to clear GRBL buffer
        SerialInterface.Instance.Write("\u0018"); 
    }

    private void OnSerialLineReceived(string line)
    {
        if (!_isRunning) return;

        // GRBL sends 'ok' when a command is accepted into the planner buffer
        // Or 'error'. In both cases, the command has left the RX buffer and entered the parser/executor.
        
        if (line.Contains("ok") || line.Contains("error"))
        {
            _pendingCommandsCount--;
            // Update Byte Count
            if (_sentLineLengths.Count > 0)
            {
                int len = _sentLineLengths.Dequeue();
                _currentBytes -= len;
                if (_currentBytes < 0) _currentBytes = 0; // Output safety
            }
            
            if (line.Contains("error"))
            {
                Debug.WriteLine($"GRBL Error: {line}");
            }
            
            SendNext();
        }
    }

    private void SendNext()
    {
        if (_isPaused || !_isRunning) return;

        // Flow Control:
        // 1. Planner Buffer (Slots)
        // 2. RX Byte Buffer (Size)
        
        while (_currentLineIndex < _gcodeLines.Count)
        {
            // 1. Check Planner Slots
            if (_pendingCommandsCount >= MaxPlannerBlocks)
            {
               break; 
            }

            string line = _gcodeLines[_currentLineIndex];
            string lineToSend = line + "\n";
            int lineBytes = lineToSend.Length; // ASCII 1 byte per char

            // 2. Check RX Buffer Size
            if (_currentBytes + lineBytes > MaxBufferSize)
            {
                // Not enough room in RX buffer
                break;
            }

            SerialInterface.Instance.Write(lineToSend);
            
            _pendingCommandsCount++;
            _currentBytes += lineBytes;
            _sentLineLengths.Enqueue(lineBytes);
            
            _currentLineIndex++;
            
            long now = DateTime.Now.Ticks;
            if (now - _lastProgressTicks > ProgressInterval || _currentLineIndex == _gcodeLines.Count)
            {
                ProgressChanged?.Invoke(_currentLineIndex, _gcodeLines.Count);
                _lastProgressTicks = now;
            }
        }

        if (_currentLineIndex >= _gcodeLines.Count && _pendingCommandsCount == 0)
        {
            _isRunning = false;
            JobCompleted?.Invoke();
        }
    }
}
