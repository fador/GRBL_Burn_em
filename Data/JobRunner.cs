/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace grbl_burn_em.Data;

public class JobRunner
{
    private List<string> _gcodeLines = new List<string>();
    private int _currentLineIndex = 0;
    public int PendingCommandsCount { get; set; } = 0;
    public int MaxPlannerBlocks { get; set; } = 15; // Default GRBL
    public int MaxBufferSize { get; set; } = 127; // Standard GRBL Rx Buffer
    private int _currentBytes = 0;
    private Queue<int> _sentLineLengths = new Queue<int>();

    private long _lastProgressTicks = 0;
    private const long ProgressInterval = 1000000; // 100ms

    private volatile bool _isRunning = false;
    private volatile bool _isPaused = false;
    private bool _allLinesSent = false;

    // Completion: after the last line is sent, the job is done when all lines were
    // acknowledged ('ok' drained) AND GRBL reports Idle. Give up if the machine goes
    // silent or never finishes.
    private long _completionWaitStartTicks;
    private const long CompletionWaitTimeout = 10 * 60 * 10000000L; // 10 minutes
    private long _lastStatusTicks;
    private const long StatusStarvationTimeout = 5 * 10000000L; // 5s without status reports

    public event Action<int, int>? ProgressChanged; // Current, Total
    public event Action? JobCompleted;
    public event Action<string>? JobFailed;
    public event Action? JobStopped;

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;

    private bool _laserModeWarned;

    public JobRunner()
    {
        SerialInterface.Instance.LineReceived += OnSerialLineReceived;
        SerialInterface.Instance.StatusReceived += (s, p) => _lastStatusTicks = Environment.TickCount64;
        SerialInterface.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    private readonly object _runnerLock = new object();

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
            _allLinesSent = false;
            _laserModeWarned = false;

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
        if (!_isRunning) return;
        _isPaused = true;
        SerialInterface.Instance.Write("!");

        // Laser safety: only GRBL laser mode ($32=1) turns the laser off during feed
        // hold. Warn once if that isn't confirmed - otherwise the laser may keep
        // burning at the current power while motion is paused.
        if (!SerialInterface.Instance.LaserModeEnabled && !_laserModeWarned)
        {
            _laserModeWarned = true;
            var main = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Visible);
            Action warn = () => MessageBox.Show(
                main,
                "GRBL laser mode ($32=1) is not enabled (or not confirmed by the machine).\n\n" +
                "During feed hold the laser may stay ON at its current power, burning in place.\n" +
                "Enable laser mode on the controller: send '$32=1' (requires PWM output).",
                "Laser Safety Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (main != null && main.InvokeRequired) main.BeginInvoke(warn);
            else if (main != null) main.Invoke(warn);
            else warn();
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
        bool wasRunning;
        lock (_runnerLock)
        {
            wasRunning = _isRunning;
            _isRunning = false;
            _isPaused = false;
            _allLinesSent = false;
            _cts?.Cancel();

            _gcodeLines.Clear();
            PendingCommandsCount = 0;
            _currentBytes = 0;
            _sentLineLengths.Clear();
        }

        if (!wasRunning) return;

        SerialInterface.Instance.EmptyBuffers();
        // Soft Reset to clear GRBL buffer and stop the machine.
        SerialInterface.Instance.Write("\u0018");
        Task.Run(() => JobStopped?.Invoke());
    }

    private void FailJob(string message)
    {
        lock (_runnerLock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            _isPaused = false;
            _allLinesSent = false;
        }
        Debug.WriteLine($"JobRunner: {message}");
        Task.Run(() => JobFailed?.Invoke(message));
    }

    private void OnConnectionStatusChanged(bool connected)
    {
        if (!connected && _isRunning)
        {
            FailJob("Connection to the machine was lost.");
        }
    }

    private void OnSerialLineReceived(string line)
    {
        lock (_runnerLock)
        {
            if (!_isRunning) return;

            // GRBL sends 'ok' when a command is accepted into the planner buffer
            // Or 'error'. In both cases, the command has left the RX buffer.
            if (line.Contains("ok") || line.Contains("error"))
            {
                DequeueCommand();

                if (line.Contains("error"))
                {
                    Debug.WriteLine($"GRBL Error: {line}");
                }
            }
            else if (line.Contains("ALARM:"))
            {
                Stop();
                string alarmCode = line.Substring(line.IndexOf(':') + 1);
                string msg = GrblErrors.GetAlarmMessage(alarmCode);
                var main = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Visible);
                Action show = () => MessageBox.Show(
                    main,
                    $"Machine Alarm: {line}\n{msg}", "Alarm", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (main != null && main.InvokeRequired) main.BeginInvoke(show);
                else if (main != null) main.Invoke(show);
                else Task.Run(show);
            }
        }
    }

    public bool DequeueCommand()
    {
        PendingCommandsCount--;
        if (PendingCommandsCount < 0) PendingCommandsCount = 0;

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

    private void SenderLoop()
    {
        try
        {
            while (true)
            {
                lock (_runnerLock)
                {
                    if (!_isRunning || (_cts != null && _cts.IsCancellationRequested)) break;

                    if (_isPaused)
                    {
                        continue;
                    }

                    if (_allLinesSent)
                    {
                        // All lines were written to the wire. The job is only complete
                        // when every line was acknowledged by GRBL AND the machine is
                        // back to Idle (the buffer has fully executed).
                        bool bufferDrained = _currentBytes <= 0 && PendingCommandsCount <= 0;
                        bool machineIdle = SerialInterface.Instance.MachineState.Equals("Idle", StringComparison.OrdinalIgnoreCase);

                        if (bufferDrained && machineIdle)
                        {
                            _isRunning = false;
                            Task.Run(() => JobCompleted?.Invoke());
                            break;
                        }

                        long now = Environment.TickCount64;

                        // The machine stopped reporting status (serial dead / GRBL lockup).
                        if (bufferDrained && now - _lastStatusTicks > StatusStarvationTimeout)
                        {
                            _isRunning = false;
                            Task.Run(() => JobFailed?.Invoke("No status from the machine while waiting for the job to finish."));
                            break;
                        }

                        // The machine never finished (large job on a slow controller,
                        // or the controller stopped mid-buffer).
                        if (now - _completionWaitStartTicks > CompletionWaitTimeout)
                        {
                            _isRunning = false;
                            Task.Run(() => JobFailed?.Invoke("Timed out waiting for the machine to finish."));
                            break;
                        }
                    }
                    else if (_currentLineIndex < _gcodeLines.Count)
                    {
                        string line = _gcodeLines[_currentLineIndex];
                        string lineToSend = line + "\n";
                        int lineBytes = lineToSend.Length;

                        // Check Output Buffer (Character Counting)
                        if (_currentBytes + lineBytes <= MaxBufferSize)
                        {
                            if (!SerialInterface.Instance.IsConnected)
                            {
                                FailJob("Connection to the machine was lost.");
                                break;
                            }

                            bool sent = SerialInterface.Instance.Write(lineToSend);
                            if (!sent)
                            {
                                FailJob("Failed to send data to the machine. Job stopped.");
                                break;
                            }

                            PendingCommandsCount++;
                            _currentBytes += lineBytes;
                            _sentLineLengths.Enqueue(lineBytes);

                            _currentLineIndex++;

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
                    else
                    {
                        _allLinesSent = true;
                        _completionWaitStartTicks = Environment.TickCount64;
                    }
                }

                Thread.Sleep(5); // Yield / Wait
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"JobRunner Sender Thread Exception: {ex}");
            _isRunning = false;
        }
    }
}
