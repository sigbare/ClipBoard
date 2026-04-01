using System.Net.Sockets;

namespace ClipBoard.Models;


public class ClientConnection : IDisposable
{
    public string Id { get; set; } = string.Empty;
    public TcpClient? TcpClient { get; set; }
    public NetworkStream? Stream { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActivity { get; set; }
    
    public void Dispose()
    {
        Stream?.Dispose();
        TcpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}