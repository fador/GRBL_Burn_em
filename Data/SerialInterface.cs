/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using RJCP.IO.Ports;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace grbl_burn_em.Data;

public class SerialInterface
{
    private static SerialInterface? _instance;
    public static SerialInterface Instance => _instance ??= new SerialInterface();

    private SerialPortStream? _serialPort;
    private TcpClient? _tcpClient;
    private NetworkStream? _netStream;
    public bool IsConnected => (_serialPort != null && _serialPort.IsOpen) || (_tcpClient != null && _tcpClient.Connected);
    
    public event Action<string>? DataReceived;
    public event Action<string>? LineReceived;
    public event Action<string>? LineSent; // Added LineSent event
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string, PointF>? StatusReceived; // State, Pos

    public string MachineState { get; private set; } = "Unknown";
    public PointF MachinePosition { get; private set; } = new PointF(0, 0);
    
    private StringBuilder _rxBuffer = new StringBuilder();
    public event Action<int, int>? BufferLimitsReceived;

    private System.Threading.Timer? _pollTimer;
    private bool _isPolling = false;

    // Constructor
    public SerialInterface()
    {
        _serialPort = new SerialPortStream();
        //_serialPort.DataReceived += _serialPort_DataReceived; // We subscribe in Connect
    }

    public int BytesToWrite()
    {
        return _serialPort != null ? _serialPort.BytesToWrite : 0;
    }

    public int WriteBufferSize()
    {
        return _serialPort != null ? _serialPort.WriteBufferSize : 0;
    }

    public bool EmptyBuffers()
    {
        _serialPort?.DiscardInBuffer();
        _serialPort?.DiscardOutBuffer();
        return true;
    }

    public void Connect(string portName, int baudRate)
    {
        Disconnect();
        
        // Check for Emulator
        // Check for Emulator or TCP
        if (portName.StartsWith("TCP:", StringComparison.OrdinalIgnoreCase))
        {
             ConnectTcp(portName.Substring(4));
             return;
        }
        if (portName.Contains(":")) // Assume IP:Port
        {
             ConnectTcp(portName);
             return;
        }

        try
        {
            if(_serialPort == null) _serialPort = new SerialPortStream();
            _serialPort.PortName = portName;
            _serialPort.BaudRate = baudRate;
            _serialPort.WriteTimeout = 10; // Prevent UI freeze on blocked write
            _serialPort.DataReceived += _serialPort_DataReceived;
            _serialPort.Open();
            _serialPort.DiscardInBuffer(); // Clear any existing data
            
            ConnectionStatusChanged?.Invoke(true);
            
            // Allow some time for the machine to reset and send welcome message
            Thread.Sleep(200); 

            // Start Polling 
            StartPolling();
            
            // Initialize Grbl - Soft reset
            Write("\u0018"); // Ctrl-X (Soft Reset)
            Thread.Sleep(100);
            Write("$X\n"); // Unlock
            //Write("$10=3\n"); // Enable Buffer Stats (as per user request)
            Write("?"); // Status report
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Serial Connect Error: {ex.Message}");
            throw;
        }
    }

    private void ConnectTcp(string hostPort)
    {
        try
        {
            var parts = hostPort.Split(':');
            string host = parts[0];
            int port = parts.Length > 1 ? int.Parse(parts[1]) : 2345;
            
            _tcpClient = new TcpClient();
            _tcpClient.Connect(host, port);
            _netStream = _tcpClient.GetStream();
            
            ConnectionStatusChanged?.Invoke(true);
            StartPolling();
            
            // Start Read Loop
            Task.Run(TcpReadLoop);
            
             // Initialize Grbl
            Write("\u0018"); 
            Thread.Sleep(100);
            Write("$X\n");
            Write("?");
        }
        catch (Exception ex)
        {
             Debug.WriteLine($"TCP Connect Error: {ex.Message}");
             Disconnect();
             throw;
        }
    }

    private async Task TcpReadLoop()
    {
        byte[] buffer = new byte[4096];
        while (IsConnected && _netStream != null)
        {
            try
            {
                int bytesRead = await _netStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break; // Disconnected
                
                string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                ProcessIncomingData(data);
            }
            catch { break; }
        }
        Disconnect();
    }

    public void Disconnect()
    {
        if (_serialPort != null)
        {
            try
            {
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
            }
            catch { }
            finally
            {
                _serialPort = null;
            }
        }
        if (_tcpClient != null)
        {
            try 
            {
                _tcpClient.Close();
                _tcpClient = null;
                _netStream = null;
            } catch {}
        }
        ConnectionStatusChanged?.Invoke(false);
    }

    public void StartPolling()
    {
        if (_isPolling) return;
        _isPolling = true;
        _pollTimer = new System.Threading.Timer(PollCallback, null, 200, 200);
    }

    public void StopPolling()
    {
        _isPolling = false;
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void PollCallback(object? state)
    {
        if (IsConnected)
        {
            Write("?");
        }
    }

    private readonly object _writeLock = new object();

    public bool Write(string data)
    {
        if (IsConnected)
        {
            if (_serialPort != null)
            {
                if(data.Length > _serialPort.WriteBufferSize - _serialPort.BytesToWrite)
                {                
                    return false;
                }
            }
            
            try 
            { 
                 lock (_writeLock)
                 {
                     if (_serialPort != null && _serialPort.IsOpen)
                     {
                        _serialPort.Write(data);
                     }
                     else if (_netStream != null)
                     {
                        byte[] bytes = Encoding.ASCII.GetBytes(data);
                        _netStream.Write(bytes, 0, bytes.Length);
                        _netStream.Flush();
                     }
                 }
                 if(data != "?") LineSent?.Invoke(data.Trim()); // Invoke event
            }
            catch (TimeoutException)
            {
                Debug.WriteLine("Serial Write Timeout - Port Blocked?");
                return false;
            }
            catch (Exception ex) 
            { 
                Debug.WriteLine($"Write Error: {ex.Message}"); 
                return false;
            }
        }
        return true;
    }

    protected virtual void _serialPort_DataReceived(object? sender, SerialDataReceivedEventArgs args)
    {
         if (_serialPort == null || !_serialPort.IsOpen) return;
         try
         {
             string data = _serialPort.ReadExisting();
             ProcessIncomingData(data);
         }
         catch (Exception ex)
         {
             Debug.WriteLine($"Serial RX Error: {ex.Message}");
         }
    }

    private void ProcessIncomingData(string data)
    {
         if (!string.IsNullOrEmpty(data))
         {
             DataReceived?.Invoke(data);

             // Process Lines
             foreach(char c in data)
             {
                 if (c == '\n' || c == '\r')
                 {
                     if (_rxBuffer.Length > 0)
                     {
                         string line = _rxBuffer.ToString().Trim();
                         _rxBuffer.Clear();
                         if (string.IsNullOrEmpty(line)) continue;
                         
                         if (line.StartsWith("<"))
                         {
                             ParseStatus(line);
                         } 
                         else 
                         {
                            LineReceived?.Invoke(line);
                         }
                     }
                 }
                 else
                 {
                     _rxBuffer.Append(c);
                 }
             }
         }
    }

    public string[] GetAvailablePorts()
    {
         var ports = _serialPort?.GetPortNames() ?? Array.Empty<string>();
         var list = new List<string>(ports);
         list.Add("TCP:127.0.0.1:2345"); // Emulator Option
         return list.ToArray();
    }

    public async Task MoveRelative(float dx, float dy)
    {
        if (!IsConnected) return;

        // Ensure Relative Mode or use Incremental Move
        // Safer to use G91 for the move then G90 back, or just assume user knows.
        // We will send "$J=G91 X.. Y.." for jogging which is safer as it doesn't change modal state?
        // Or "G91 G0 X.. Y.. \n G90"
        
        // Using G0
        string cmd = $"G91 G0 X{dx:F3} Y{dy:F3}\nG90\n";
        Write(cmd);
        
        // Wait for Idle
        await WaitUntilIdle();
    }

    public async Task WaitUntilIdle()
    {
         // Wait for state to be Run then Idle?
         // Or just wait for Idle.
         
         // Give it a moment to register Start
         await Task.Delay(100);
         
         int timeout = 10000; // 10s
         int waited = 0;
         while (waited < timeout)
         {
             if (MachineState.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                 return;
                 
             if (MachineState.Contains("Alarm"))
                 throw new Exception("Machine Alarm");
                 
             await Task.Delay(100);
             waited += 100;
         }
         throw new TimeoutException("Timed out waiting for machine Idle");
    }

    private void ParseStatus(string line)
    {
        // Format: <Idle|MPos:0.000,0.000,0.000|FS:0,0|WCO:0.000,0.000,0.000>
        // Remove < and >
        line = line.TrimStart('<').TrimEnd('>');
        var parts = line.Split('|');
        
        if (parts.Length > 0)
        {
            MachineState = parts[0];
            
            foreach(var part in parts.Skip(1))
            {
                if (part.StartsWith("MPos:"))
                {
                    var coords = part.Substring(5).Split(',');
                    if (coords.Length >= 2)
                    {
                        if (float.TryParse(coords[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                            float.TryParse(coords[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float y))
                        {
                            MachinePosition = new PointF(x, y);
                        }
                    }
                }
                else if (part.StartsWith("Bf:"))
                {
                     // Bf:15,128  (Planner, Rx)
                     var vals = part.Substring(3).Split(',');
                     if (vals.Length >= 2)
                     {
                         bool pParsed = int.TryParse(vals[0], out int planner);
                         bool rParsed = int.TryParse(vals[1], out int rx);

                         if (pParsed && rParsed)
                         {
                             // We fire an event with both limits
                             BufferLimitsReceived?.Invoke(planner-1, rx);
                         }
                     }
                }
            }
            
            StatusReceived?.Invoke(MachineState, MachinePosition);
        }
    }
}
