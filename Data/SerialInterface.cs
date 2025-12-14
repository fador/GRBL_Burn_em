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
            ConnectionStatusChanged?.Invoke(true);
            
            // Start Polling 
            StartPolling();
            
            // Initialize Grbl - Soft reset
            Write("\u0018"); // Ctrl-X (Soft Reset)
            Thread.Sleep(100);
            Write("$X\n"); // Unlock
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
                // Handle WPos if we rely on Work Coordinates? Usually Grbl reports MPos (Machine) and WCO (Work Offset).
                // GUI usually treats drawing as being in Work Coordinates.
                // For simplified GUI, let's assume MPos for now OR we should respect WPos.
                // Standard Grbl 1.1 usually reports MPos.
            }
            
            StatusReceived?.Invoke(MachineState, MachinePosition);
        }
    }
}
