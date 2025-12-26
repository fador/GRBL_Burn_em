using System;
using System.Net;
using System.Net.Sockets;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace grbl_burn_em_emulator;

public class CameraServer
{
    private static CameraServer? _instance;
    public static CameraServer Instance => _instance ??= new CameraServer();
    
    private TcpListener? _listener;
    
    public Func<Bitmap>? CaptureProvider;

    public void Start(int port)
    {
        try 
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Task.Run(ListenLoop);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CamServer Error: " + ex.Message);
        }
    }

    private async Task ListenLoop()
    {
        while (true)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync();
                Task.Run(() => HandleClient(client));
            }
            catch { break; }
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new BinaryReader(stream);
            using var writer = new BinaryWriter(stream);
            
            // Protocol:
            // Client sends 1 byte command: 'S' (Snap)
            // Server responds:
            // [4 bytes int] Length
            // [Bytes] Jpeg Data
            
            while(client.Connected)
            {
                byte cmd = reader.ReadByte();
                if (cmd == (byte)'S')
                {
                    Bitmap? bmp = CaptureProvider?.Invoke();
                    if (bmp != null)
                    {
                        using var ms = new MemoryStream();
                        bmp.Save(ms, ImageFormat.Jpeg);
                        byte[] data = ms.ToArray();
                        
                        writer.Write((int)data.Length);
                        writer.Write(data);
                        writer.Flush();
                        
                        bmp.Dispose();
                    }
                    else
                    {
                        writer.Write((int)0);
                    }
                }
            }
        }
        catch
        {
            client.Close();
        }
    }
}
