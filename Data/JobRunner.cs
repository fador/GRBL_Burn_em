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
    private int _bufferBytes = 0;
    private const int MaxBufferSize = 127; 
    
    private bool _isRunning = false;
    private bool _isPaused = false;
    
    public event Action<int, int>? ProgressChanged; // Current, Total
    public event Action? JobCompleted;
    
    // Commands in buffer
    private Queue<int> _pendingCommands = new Queue<int>(); // Length of each pending command

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
        _bufferBytes = 0;
        _pendingCommands.Clear();
        _isRunning = true;
        _isPaused = false;
        
        // Disable Polling during heavy streaming? 
        // Actually GRBL handles "?" during streaming fine. 
        // Character counting needs to account for "?"? No, real-time commands are not buffered.
        
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
        _bufferBytes = 0;
        
        // Soft Reset to clear GRBL buffer
        SerialInterface.Instance.Write("\u0018"); 
    }

    private void OnSerialLineReceived(string line)
    {
        if (!_isRunning) return;

        if (line.Contains("ok"))
        {
            if (_pendingCommands.Count > 0)
            {
                int len = _pendingCommands.Dequeue();
                _bufferBytes -= len;
                if (_bufferBytes < 0) _bufferBytes = 0; // Safety
            }
            // Send more if possible
            SendNext();
        }
        else if (line.Contains("error"))
        {
            // Log error?
             Debug.WriteLine($"GRBL Error: {line}");
             // Treat as ack? Usually yes, counts as processed command.
             if (_pendingCommands.Count > 0)
            {
                int len = _pendingCommands.Dequeue();
                _bufferBytes -= len;
            }
            SendNext();
        }
    }

    private void SendNext()
    {
        if (_isPaused || !_isRunning) return;

        // Try to fill buffer
        while (_currentLineIndex < _gcodeLines.Count)
        {
            string line = _gcodeLines[_currentLineIndex];
            string lineToSend = line + "\n";
            int len = lineToSend.Length;

            if (_bufferBytes + len <= MaxBufferSize)
            {
                SerialInterface.Instance.Write(lineToSend);
                _bufferBytes += len;
                _pendingCommands.Enqueue(len);
                _currentLineIndex++;
                
                ProgressChanged?.Invoke(_currentLineIndex, _gcodeLines.Count);
            }
            else
            {
                // Buffer full
                break;
            }
        }

        if (_currentLineIndex >= _gcodeLines.Count && _pendingCommands.Count == 0)
        {
            _isRunning = false;
            JobCompleted?.Invoke();
        }
    }
}
