using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace grbl_burn_em_emulator;

public class TcpServer
{
    private static TcpServer? _instance;
    public static TcpServer Instance => _instance ??= new TcpServer();

    private TcpListener? _listener;
    private TcpClient? _client;
    public bool IsConnected => _client != null && _client.Connected;
    
    public event Action<string>? Log;

    public void Start(int port)
    {
        try 
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Log?.Invoke($"Server starting on {port}...");
            Task.Run(ListenLoop);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Server Start Error: {ex.Message}");
        }
    }

    private async Task ListenLoop()
    {
        while (true)
        {
            try 
            {
                var client = await _listener!.AcceptTcpClientAsync();
                Log?.Invoke("Client Connected!");
                _client = client;
                
                // Send Welcome
                Send("Grbl 1.1h ['$' for help]\r\n");
                
                using (var stream = client.GetStream())
                {
                    byte[] buffer = new byte[1024];
                    while (client.Connected)
                    {
                         int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                         if (bytesRead == 0) break;
                         
                         string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                         
                         // Process Lines
                         // Handle fragmentation slightly properly? Assuming lines for now
                         // For detailed parser, we'd need a buffer
                         string[] lines = data.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                         foreach(var line in lines)
                         {
                             EmulatorLogic.Instance.ParseLine(line);
                         }
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Client Error: {ex.Message}");
            }
            finally
            {
                _client?.Close();
                _client = null;
                Log?.Invoke("Client Disconnected");
            }
        }
    }

    private readonly object _sendLock = new object();
    public void Send(string data)
    {
        if (!IsConnected) return;
        try 
        {
            lock(_sendLock)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(data);
                _client!.GetStream().Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            // Ignore write errors
        }
    }
}
