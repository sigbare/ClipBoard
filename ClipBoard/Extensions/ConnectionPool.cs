

using System.Collections.Concurrent;
using System.Net.Sockets;

namespace ClipBoard.Extensions;

public class ConnectionPool(int maxPoolSize = 100) : IDisposable
{
    private readonly ConcurrentBag<TcpClient> _availableConnections = [];
    private readonly SemaphoreSlim _semaphore = new(maxPoolSize, maxPoolSize);
    private readonly int _maxPoolSize = maxPoolSize;
    private bool _disposed;

    public async Task<TcpClient> GetConnectionAsync(string host, int port)
    {
        await _semaphore.WaitAsync();
        
        if (_availableConnections.TryTake(out var client) && client.Connected)
        {
            return client;
        }
        
        client = new TcpClient();
        await client.ConnectAsync(host, port);
        ConfigureClient(client);
        return client;
    }
    
    public void ReturnConnection(TcpClient client)
    {
        if (_disposed) return;
        
        if (client.Connected && _availableConnections.Count < _maxPoolSize)
        {
            _availableConnections.Add(client);
            _semaphore.Release();
        }
        else
        {
            client.Dispose();
            _semaphore.Release();
        }
    }
    
    private static void ConfigureClient(TcpClient client)
    {
        client.NoDelay = true;
        client.ReceiveTimeout = 30000;
        client.SendTimeout = 30000;
    }
    
    public void Dispose()
    {
        _disposed = true;
        foreach (var client in _availableConnections)
        {
            client.Dispose();
        }
        _semaphore.Dispose();
    }
}