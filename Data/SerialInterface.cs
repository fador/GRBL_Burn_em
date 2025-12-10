using System.IO.Ports;
using System.Diagnostics;

namespace laser_gui_test.Data;

public class SerialInterface
{
    private static SerialInterface? _instance;
    public static SerialInterface Instance => _instance ??= new SerialInterface();

    private SerialPort? _serialPort;
    public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
    
    public event Action<string>? DataReceived;
    public event Action<bool>? ConnectionStatusChanged;

    public void Connect(string portName, int baudRate)
    {
        Disconnect();
        try
        {
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.DataReceived += _serialPort_DataReceived;
            _serialPort.Open();
            ConnectionStatusChanged?.Invoke(true);
            
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
                ConnectionStatusChanged?.Invoke(false);
            }
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

    private void _serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_serialPort == null) return;
        try
        {
            string data = _serialPort.ReadExisting();
            DataReceived?.Invoke(data);
        }
        catch { }
    }
}
