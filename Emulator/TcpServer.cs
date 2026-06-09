using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace grbl_burn_em_emulator;

public class TcpServer
{
    private static TcpServer? _instance;
    public static TcpServer Instance => _instance ??= new TcpServer();

    private TcpListener? _listener;
    private TcpClient? _client;
    public bool IsConnected => _client != null && _client.Connected;

    private readonly ConcurrentQueue<string> _commandQueue = new();

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

    public void EnqueueLine(string line) => _commandQueue.Enqueue(line);
    public bool TryDequeueLine(out string? line) => _commandQueue.TryDequeue(out line);
    public void ClearQueue() { while (_commandQueue.TryDequeue(out _)) { } }

    private async Task ListenLoop()
    {
        while (true)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync();
                Log?.Invoke("Client Connected!");
                _client = client;

                Send("Grbl 1.1h ['$' for help]\r\n");

                using var stream = client.GetStream();
                var lineBuffer = new StringBuilder();
                byte[] buffer = new byte[4096];

                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    lineBuffer.Append(data);

                    ProcessBuffer(lineBuffer);
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
                ClearQueue();
                Log?.Invoke("Client Disconnected");
            }
        }
    }

    private void ProcessBuffer(StringBuilder lineBuffer)
    {
        string buf = lineBuffer.ToString();

        while (true)
        {
            int qIdx = buf.IndexOf('?');
            int nIdx = buf.IndexOf('\n');
            int idx = (qIdx >= 0 && (nIdx < 0 || qIdx < nIdx)) ? qIdx : nIdx;
            if (idx < 0) break;

            if (idx == nIdx)
            {
                string line = buf.Substring(0, idx).TrimEnd('\r', '\n', ' ');
                buf = buf.Substring(idx + 1);

                if (!string.IsNullOrEmpty(line))
                {
                    Log?.Invoke($"RX: {line}");
                    EmulatorLogic.Instance.ParseLine(line);
                }
            }
            else
            {
                string before = buf.Substring(0, idx).TrimEnd('\r', ' ');
                buf = buf.Substring(idx + 1);

                if (!string.IsNullOrEmpty(before))
                {
                    lineBuffer.Clear();
                    lineBuffer.Append(before);
                    ProcessBuffer(lineBuffer);
                    buf = lineBuffer.ToString();
                }
                EmulatorLogic.Instance.ParseLine("?");
            }
        }

        lineBuffer.Clear();
        lineBuffer.Append(buf);
    }

    private readonly object _sendLock = new();
    public void Send(string data)
    {
        if (!IsConnected) return;
        try
        {
            lock (_sendLock)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(data);
                _client!.GetStream().Write(bytes, 0, bytes.Length);
            }
        }
        catch { }
    }
}
