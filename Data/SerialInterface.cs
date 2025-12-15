using System.IO.Ports;
using System.Diagnostics;
using System.Text;

namespace laser_gui_test.Data;

public class SerialInterface
{
    private static SerialInterface? _instance;
    public static SerialInterface Instance => _instance ??= new SerialInterface();

    private SerialPort? _serialPort;
    public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
    
    public event Action<string>? DataReceived;
    public event Action<string>? LineReceived;
    public event Action<string>? LineSent; // Added LineSent event
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string, PointF>? StatusReceived; // State, Pos

    public string MachineState { get; private set; } = "Unknown";
    public PointF MachinePosition { get; private set; } = new PointF(0, 0);

    private System.Threading.Timer? _pollTimer;
    private bool _isPolling = false;

    public void Connect(string portName, int baudRate)
    {
        Disconnect();
        try
        {
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.DataReceived += _serialPort_DataReceived;
            _serialPort.Open();
            _serialPort.DiscardInBuffer(); // Clear any existing data
            ConnectionStatusChanged?.Invoke(true);
            
            // Allow some time for the machine to reset and send welcome message
            Thread.Sleep(500); 

            // Start Polling 
            StartPolling();
            
            // Initialize Grbl - Soft reset
            Write("\u0018"); // Ctrl-X (Soft Reset)
            Thread.Sleep(100);
            Write("$X\n"); // Unlock
            Write("$10=3\n"); // Enable Buffer Stats (as per user request)
            Write("?"); // Status report
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Serial Connect Error: {ex.Message}");
            throw;
        }
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
                _serialPort = null;
                ConnectionStatusChanged?.Invoke(false);
            }
        }
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

    public void Write(string data)
    {
        if (IsConnected && _serialPort != null)
        {
            try 
            { 
                 _serialPort.Write(data);
                 if(data != "?") LineSent?.Invoke(data.Trim()); // Invoke event
            }
            catch (Exception ex) 
            { 
                Debug.WriteLine($"Write Error: {ex.Message}"); 
            }
        }
    }

    public string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    private StringBuilder _rxBuffer = new StringBuilder();

    public event Action<int>? RxBufferSizeReceived;

    public int RxBufferSize { get; private set; } = 127;

    private void _serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort == null) return;
        try
        {
            string data = _serialPort.ReadExisting();
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
                        
                        // Parse
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
        catch { }
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
                         if (int.TryParse(vals[1], out int rx))
                         {
                             // Only update if changed or specifically needed?
                             // We assume the max is what we see when idle.
                             // But we just emit it.
                             if (rx > RxBufferSize) // If we see a larger buffer reported, assume that's the max? 
                             {
                                 // Actually, Bf reports AVAILABLE bytes. 
                                 // So the maximum value we ever see is the buffer size.
                                 // But initially we might want to capture it.
                                 // Let's just event it out.
                                 if (RxBufferSize != rx)
                                 {
                                     RxBufferSize = rx; // This logic acts as a "max peak" detector if we start low?
                                     // Actually no, Bf fluctuates. We want the Init value.
                                     // User said "read the machine data WHEN CONNECTING".
                                     // So we assume the connection is Idle and buffer is empty.
                                     RxBufferSizeReceived?.Invoke(rx);
                                 }
                             }
                             // Or should we just fire it? 
                             // Let's fire it if it's likely the full buffer (e.g. at startup)
                             // Simple approach: Always fire, let consumer decide? 
                             // Consumer might reset MaxBufferSize on every update? No that's bad if buffer fills.
                             // Wait, successful parsing of 'Bf' means we know the CURRENT available. 
                             // The question is "configuring the serial buffer". 
                             // If we are Idle and just connected, the Available = Max.
                             if (MachineState == "Idle") 
                             {
                                  // We can optimistically assume current available is Max if we just connected
                                  // But let's just expose the value.
                                  // We will implement logic in MainForm to capture the FIRST one or Max one.
                                  RxBufferSizeReceived?.Invoke(rx);
                             }
                         }
                     }
                }
            }
            
            StatusReceived?.Invoke(MachineState, MachinePosition);
        }
    }
}
