
using System.Net.Sockets;
using ClipBoard.Models;

namespace ClipBoard.Services;

public class Client
{
    private ClientConnection _client;
    public Client()
    {
        
    }

     private async Task ConnectToServerAsync()
    {
        if (_isConnected)
        {
            Console.WriteLine("Already connected to a server!");
            return;
        }

        Console.Write("Enter server IP address (127.0.0.1): ");
        var ip = Console.ReadLine();
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

        Console.Write("Enter server port (8888): ");
        var portStr = Console.ReadLine();
        int port = string.IsNullOrEmpty(portStr) ? 8888 : int.Parse(portStr);

        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port);
            _stream = _client.GetStream();
            _isConnected = true;
            Console.WriteLine($"✅ Connected to {ip}:{port}");

            _ = Task.Run(() => ReceiveMessagesAsync(_cts.Token));

            var helloMsg = new NetworkMessage
            {
                Type = MessageType.Heartbeat,
                SenderId = _peerId + Guid.NewGuid().ToString().Take(4),
                Data = $"Connected {_peerId}"
            };
            await SendMessageAsync(helloMsg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Connection error: {ex.Message}");
        }
    }

}