using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using ClipBoard.Models;

namespace ClipBoard.Services;

public class Server : IDisposable
{
    public string ServerName { get; private set; }
    public IPAddress? ServerIp { get; private set; }
    public int ActiveConnections => _connectedClients.Count;
    public bool IsServerRunning {get; private set;} = false;
    
    private TcpListener? _server;
    private readonly CancellationTokenSource _cts;
    private int? _portRunning;
    private readonly ConcurrentDictionary<string, ClientConnection> _connectedClients;
    private int _connectionCounter = 0;
    
    private const int MAX_CONNECTIONS = 10000;
    private const int BUFFER_SIZE = 8192;
    private const int RECEIVE_TIMEOUT = 30000;
    private const int SEND_TIMEOUT = 30000;
    
    public Server()
    {
        ServerName = "Server_" + Environment.MachineName;
        ServerIp = Dns.GetHostEntry(Dns.GetHostName()).AddressList
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        
        _cts = new CancellationTokenSource();
        _connectedClients = new ConcurrentDictionary<string, ClientConnection>();
    }
    
    public async Task<OperationalResult> StartServerAsync(int port)
    {
        if (IsServerRunning)
            return OperationalResult.Failure(
                _portRunning != null ? 
                $"Server already running on port {_portRunning}"
                : "Server already running", ErrorType.Conflict);
        
        try
        {
            _server = new TcpListener(IPAddress.Any, port);
            
            _server.Server.SetSocketOption(SocketOptionLevel.Socket, 
                SocketOptionName.ReuseAddress, true);
            _server.Server.SetSocketOption(SocketOptionLevel.Socket, 
                SocketOptionName.KeepAlive, true);
            
            _server.Start(MAX_CONNECTIONS); 
            
            _portRunning = port;
            IsServerRunning = true;
            
            _ = Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);
            
            _ = Task.Run(() => MonitorConnectionsAsync(_cts.Token), _cts.Token);
            
            return OperationalResult.Success();
        }
        catch (Exception ex)
        {
            return OperationalResult.Failure(
                "Error starting server",
                ErrorType.Iternal,
                ex);
        }
    }
    
    public async Task<OperationalResult> StopServerAsync()
    {
        if (!IsServerRunning)
            return OperationalResult.Failure("Server is not running", ErrorType.NotFound);
        
        try
        {
            _cts.Cancel();
            
            var clients = _connectedClients.Values.ToList();
            foreach (var client in clients)
            {
                await DisconnectClientAsync(client);
            }
            
            _server?.Stop();
            IsServerRunning = false;
            _portRunning = null;
            
            return OperationalResult.Success();
        }
        catch (Exception ex)
        {
            return OperationalResult.Failure("Error stopping server", ErrorType.Iternal, ex);
        }
    }
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        
        foreach (var client in _connectedClients.Values)
        {
            client.Dispose();
        }
        
        _server?.Stop();
        GC.SuppressFinalize(this);
    }

    public async Task<bool> SendToClientAsync(string clientId, byte[] data, 
        CancellationToken token = default)
    {
        if (!_connectedClients.TryGetValue(clientId, out var client))
            return false;
        
        try
        {
            if (!client.TcpClient.Connected)
                return false;
            
            var lengthPrefix = BitConverter.GetBytes(data.Length);
            await client.Stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, token);
            await client.Stream.WriteAsync(data, token);
            await client.Stream.FlushAsync(token);
            
            client.LastActivity = DateTime.UtcNow;
            return true;
        }
        catch
        {
            await DisconnectClientAsync(client);
            return false;
        }
    }
    
    public async Task BroadcastAsync(byte[] data, string? excludeClientId = null, 
        CancellationToken token = default)
    {
        var tasks = _connectedClients
            .Where(kvp => kvp.Key != excludeClientId)
            .Select(kvp => SendToClientAsync(kvp.Key, data, token));
        
        await Task.WhenAll(tasks);
    }

    public async Task<List<string>> GetConnectedClientsAsync()
        => await Task.Run(() => _connectedClients.Keys.ToList());

    public async Task<ClientConnection?> GetClientInfoAsync(string clientId)
        => await Task.Run(() => _connectedClients.TryGetValue(clientId, out var client) ? client : null);

    private async Task AcceptClientsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsServerRunning)
        {
            try
            {
                if (_connectedClients.Count >= MAX_CONNECTIONS)
                {
                    await Task.Delay(1000, token);
                    continue;
                }
                
                var acceptTask = _server!.AcceptTcpClientAsync(token).AsTask();
                var timeoutTask = Task.Delay(5000, token);
                
                var completedTask = await Task.WhenAny(acceptTask, timeoutTask);

                if (completedTask == timeoutTask)
                    continue;
                
                var tcpClient = await acceptTask;

                ConfigureClientSocket(tcpClient);
                
                var clientId = GenerateClientId();
                var clientInfo = new ClientConnection
                {
                    Id = clientId,
                    TcpClient = tcpClient,
                    ConnectedAt = DateTime.UtcNow,
                    Stream = tcpClient.GetStream(),
                    LastActivity = DateTime.UtcNow
                };
                
                _connectedClients.TryAdd(clientId, clientInfo);
                
                _ = Task.Run(() => HandleClientAsync(clientInfo, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Task.Delay(1000, token);
            }
        }
    }
    
    private static void ConfigureClientSocket(TcpClient client)
    {
        client.NoDelay = true;
        client.ReceiveTimeout = RECEIVE_TIMEOUT;
        client.SendTimeout = SEND_TIMEOUT;
        client.ReceiveBufferSize = BUFFER_SIZE;
        client.SendBufferSize = BUFFER_SIZE;
        
        client.Client.SetSocketOption(SocketOptionLevel.Socket, 
            SocketOptionName.KeepAlive, true);
        
        if (OperatingSystem.IsLinux())
        {
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, 
                SocketOptionName.TcpKeepAliveInterval, 30);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, 
                SocketOptionName.TcpKeepAliveTime, 60);
        }
    }
    
    private async Task HandleClientAsync(ClientConnection client, CancellationToken token)
    {
        var buffer = new byte[BUFFER_SIZE];
        var messageBuffer = new List<byte>();
        
        try
        {
            while (!token.IsCancellationRequested && client.TcpClient.Connected)
            {
                try
                {
                    var readTask = client.Stream.ReadAsync(buffer, 0, buffer.Length, token);
                    var timeoutTask = Task.Delay(RECEIVE_TIMEOUT, token);
                    
                    var completedTask = await Task.WhenAny(readTask, timeoutTask);
                    if (completedTask == timeoutTask)
                        break;
                    
                    var bytesRead = await readTask;
                    
                    if (bytesRead == 0)
                        break;
                    
                    client.LastActivity = DateTime.UtcNow;
                    
                    var receivedData = buffer[..bytesRead];
                    await ProcessClientDataAsync(client, receivedData, token);
                }
                catch (IOException ex) when (ex.InnerException is SocketException se)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    break;
                }
            }
        }
        finally
        {
            await DisconnectClientAsync(client);
        }
    }
    
    private async Task ProcessClientDataAsync(ClientConnection client, byte[] data, 
        CancellationToken token)
    {
       throw new NotImplementedException();
    }
    
    private async Task DisconnectClientAsync(ClientConnection client)
    {
        if (_connectedClients.TryRemove(client.Id, out _))
        {
            
            try
            {
                client.Stream?.Close();
                client.TcpClient?.Close();
                client.Dispose();
            }
            catch (Exception ex)
            {
            }
            
            var disconnectMessage = System.Text.Encoding.UTF8.GetBytes(
                $"Client {client.Id} disconnected");
            await BroadcastAsync(disconnectMessage, client.Id);
        }
    }
    
    private async Task MonitorConnectionsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, token);
                
                var now = DateTime.UtcNow;
                var deadClients = _connectedClients
                    .Where(kvp => (now - kvp.Value.LastActivity).TotalMinutes > 5)
                    .Select(kvp => kvp.Value)
                    .ToList();
                
                foreach (var client in deadClients)
                {
                    await DisconnectClientAsync(client);
                }

            }
            catch
            {
                
            }
        }
    }
    
    private string GenerateClientId()
    {
        return $"Client_{Interlocked.Increment(ref _connectionCounter)}_{Guid.NewGuid():N}";
    }
    
}


