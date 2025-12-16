using RJCP.IO.Ports;
using System.Diagnostics;
using System.Text;

namespace laser_gui_test.Data;

public class SerialInterface
{
    private static SerialInterface? _instance;
    public static SerialInterface Instance => _instance ??= new SerialInterface();

    private SerialPortStream? _serialPort;
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

    // Constructor
    public SerialInterface()
    {
        _serialPort = new SerialPortStream();
        DataReceivedEvent += _serialPort_DataReceived;
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
        try
        {
            if(_serialPort == null) _serialPort = new SerialPortStream();
            _serialPort.PortName = portName;
            _serialPort.BaudRate = baudRate;
            _serialPort.WriteTimeout = 10; // Prevent UI freeze on blocked write
            _serialPort.DataReceived += DataReceivedEvent;
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

    private readonly object _writeLock = new object();

    public bool Write(string data)
    {
        if (IsConnected && _serialPort != null)
        {
            if(data.Length > _serialPort.WriteBufferSize - _serialPort.BytesToWrite)
            {                
                return false;
            }
            try 
            { 
                 lock (_writeLock)
                 {
                     _serialPort.Write(data);
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

    public string[] GetAvailablePorts()
    {
        return _serialPort?.GetPortNames() ?? Array.Empty<string>();
    }

    private StringBuilder _rxBuffer = new StringBuilder();

    public event Action<int, int>? BufferLimitsReceived; // Planner, Rx

    public event EventHandler<SerialDataReceivedEventArgs> DataReceivedEvent;

    protected virtual void _serialPort_DataReceived(object? sender, SerialDataReceivedEventArgs args)
    {
         if (_serialPort == null || !_serialPort.IsOpen) return;
         
         try
         {
             string data = _serialPort.ReadExisting();
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
                             //Debug.WriteLine($"Line Received: {_rxBuffer.ToString()}");
                             string line = _rxBuffer.ToString().Trim();
                             _rxBuffer.Clear();
                             if (string.IsNullOrEmpty(line)) continue;
                             
                             // Parse
                             if (line.StartsWith("<"))
                             {
                                 ParseStatus(line);
                             } else {
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
         catch (Exception ex)
         {
             Debug.WriteLine($"Serial RX Error: {ex.Message}");
         }
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
