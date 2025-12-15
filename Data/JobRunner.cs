using System;
using System.Collections.Generic;
using System.Diagnostics;

using System.Linq;
using System.Threading;

namespace laser_gui_test.Data;

public class JobRunner
{
    private List<string> _gcodeLines = new List<string>();
    private int _currentLineIndex = 0;
    public int PendingCommandsCount { get; set; } = 0;
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
    
    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;

    public JobRunner()
    {
        SerialInterface.Instance.LineReceived += OnSerialLineReceived;
        //SerialInterface.Instance.BufferLimitsReceived += OnBufferLimits;
    }
    
    private readonly object _runnerLock = new object();
/*
    private void OnBufferLimits(int availablePlanner, int availableRx)
    {
        // "Self-Healing" Flow Control
        // If we missed 'ok' responses, our PendingCommandsCount will be stuck High.
        // If the machine says "I have plenty of space", we should believe it and lower our count.

        lock (_runnerLock)
        {
            if (!_isRunning) return;

            // 1. Planner Sync
            // We want to verify that PendingCommandsCount isn't drastically different from "Used Slots".
            // Since there is latency, we only correct "Down" (if we think we used MORE than reality).
            // We don't correct "Up" because we might have just sent commands that haven't arrived/processed yet.
            
            int remoteUsedPlanner = MaxPlannerBlocks - availablePlanner; // e.g. 15 - 14 = 1 used
            if (remoteUsedPlanner < 0) remoteUsedPlanner = 0;

            if (PendingCommandsCount > remoteUsedPlanner)
            {
                // We drifted! Resync.
                //Debug.WriteLine($"[Resync] Planner: Local={PendingCommandsCount} RemoteUsed={remoteUsedPlanner}. Snapping to {remoteUsedPlanner}.");
                PendingCommandsCount = remoteUsedPlanner;
            }

            // 2. Rx Buffer Sync (Optional but good)
            int remoteUsedRx = MaxBufferSize - availableRx;
            if (remoteUsedRx < 0) remoteUsedRx = 0;
            
            if (_currentBytes > remoteUsedRx)
            {
                 //Debug.WriteLine($"[Resync] RX: Local={_currentBytes} RemoteUsed={remoteUsedRx}. Snapping to {remoteUsedRx}.");
                 _currentBytes = remoteUsedRx;
            }
            
            // If we were blocked, this might unblock us
            SendNext();
        }
    }
*/

    private Thread? _senderThread;
    private CancellationTokenSource? _cts;

    public void Start(IEnumerable<string> gcode)
    {
        lock (_runnerLock)
        {
            if (_isRunning) return;

            _gcodeLines = gcode.ToList();
            _currentLineIndex = 0;
            PendingCommandsCount = 0;
            _currentBytes = 0;
            _sentLineLengths.Clear();
            
            _isRunning = true;
            _isPaused = false;
            
            _cts = new CancellationTokenSource();
            _senderThread = new Thread(SenderLoop);
            _senderThread.IsBackground = true;
            _senderThread.Name = "JobSender";
            _senderThread.Start();
        }
    }

    public void Pause()
    {
        lock (_runnerLock)
        {
            if (!_isRunning) return;
            _isPaused = true;
            SerialInterface.Instance.Write("!");
        }
    }

    public void Resume()
    {
        lock (_runnerLock)
        {
            if (!_isRunning || !_isPaused) return;
            _isPaused = false;
            SerialInterface.Instance.Write("~");
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _isPaused = false;
        
        _cts?.Cancel();
        // Option: Wait for thread? 
        // _senderThread?.Join(500);

        _gcodeLines.Clear();
        PendingCommandsCount = 0;   
        _currentBytes = 0;
        _sentLineLengths.Clear();
        
        // Soft Reset to clear GRBL buffer
        SerialInterface.Instance.Write("\u0018"); 
    }

    public bool DequeueCommand() {

        PendingCommandsCount--;
        //if(PendingCommandsCount < 0) PendingCommandsCount = 0;

        // Update Byte Count
        if (_sentLineLengths.Count > 0)
        {            
            int len = _sentLineLengths.Dequeue();
            _currentBytes -= len;
            if (_currentBytes < 0) _currentBytes = 0; // Output safety
            return true;
        }
        return false;
    }

    private void OnSerialLineReceived(string line)
    {
        // lock (_runnerLock) - we only need to protect shared state updates
        lock(_runnerLock)
        {
            if (!_isRunning) return;

            // GRBL sends 'ok' when a command is accepted into the planner buffer
            // Or 'error'. In both cases, the command has left the RX buffer and entered the parser/executor.
            
            if (line.Contains("ok") || line.Contains("error"))
            {
                DequeueCommand();
                
                if (line.Contains("error"))
                {
                    Debug.WriteLine($"GRBL Error: {line}");
                }
                
                // No need to call SendNext, the loop will pick it up
            } else if(line.Contains("ALARM:"))
            {
                Stop();
                string alarmCode = line.Substring(line.IndexOf(':') + 1);
                string msg = GrblErrors.GetAlarmMessage(alarmCode);
                MessageBox.Show($"Machine Alarm: {line}\n{msg}", "Alarm", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void SenderLoop()
    {
        try
        {
            while (_isRunning && _cts != null && !_cts.IsCancellationRequested)
            {
                if (_isPaused)
                {
                    Thread.Sleep(50);
                    continue;
                }

                bool sent = false;
                lock (_runnerLock)
                {
                    // Check Completion
                    if (_currentLineIndex >= _gcodeLines.Count && PendingCommandsCount == 0 && SerialInterface.Instance.BytesToWrite() == 0)
                    {
                        _isRunning = false;
                        Task.Run(() => JobCompleted?.Invoke()); // Fire and forget on thread pool
                        break;
                    }

                    if (_currentLineIndex < _gcodeLines.Count)
                    {
                        string line = _gcodeLines[_currentLineIndex];
                        string lineToSend = line + "\n";
                        int lineBytes = lineToSend.Length;

                        // Check Output Buffer (User logic)
                        if (SerialInterface.Instance.BytesToWrite() + lineBytes <= MaxBufferSize)
                        {
                            SerialInterface.Instance.Write(lineToSend);

                            PendingCommandsCount++;
                            _currentBytes += lineBytes;
                            _sentLineLengths.Enqueue(lineBytes);

                            _currentLineIndex++;
                            sent = true;

                            long now = DateTime.Now.Ticks;
                            if (now - _lastProgressTicks > ProgressInterval)
                            {
                                int idx = _currentLineIndex;
                                int count = _gcodeLines.Count;
                                Task.Run(() => ProgressChanged?.Invoke(idx, count));
                                _lastProgressTicks = now;
                            }
                        }
                    }
                }

                if (!sent)
                {
                    Thread.Sleep(5); // Yield / Wait
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"JobRunner Sender Thread Exception: {ex}");
            _isRunning = false;
        }
    }

}
