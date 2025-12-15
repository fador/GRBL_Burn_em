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
        // Soft Reset to clear GRBL buffer
        SerialInterface.Instance.Write("\u0018"); 
    }

    private void OnSerialLineReceived(string line)
    {
        if (!_isRunning) return;

        // GRBL sends 'ok' when a command is accepted into the planner buffer (or executed if immediate).
        // It guarantees that the RX buffer slot for that command is free? 
        // Actually, it means the command is processed.
        // We decrement our "In Flight" counter.
        
        if (line.Contains("ok"))
        {
            _pendingCommandsCount--;
            //if (_pendingCommandsCount < 0) _pendingCommandsCount = 0;
            
            SendNext();
        }
        else if (line.Contains("error"))
        {
            // Counts as processed command.
            _pendingCommandsCount--;
            //if (_pendingCommandsCount < 0) _pendingCommandsCount = 0;
            
            Debug.WriteLine($"GRBL Error: {line}");
            SendNext();
        }
    }

    private void SendNext()
    {
        if (_isPaused || !_isRunning) return;

        // Try to fill Planner Buffer (Slots)
        // We ensure we don't have more pending commands than the reported available blocks.
        // Note: Safe strategy is to keep this slightly below Max? e.g. Max - 1?
        // Let's use MaxPlannerBlocks directly.
        
        while (_currentLineIndex < _gcodeLines.Count)
        {
            // Check if we have room
            if (_pendingCommandsCount >= MaxPlannerBlocks)
            {
               break; 
            }

            string line = _gcodeLines[_currentLineIndex];
            string lineToSend = line + "\n";
            
            SerialInterface.Instance.Write(lineToSend);
            _pendingCommandsCount++;
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
