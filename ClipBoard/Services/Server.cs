using System.Net;
using System.Net.Sockets;
using ClipBoard.Models;

namespace ClipBoard.Services;

public class Server
{
    private TcpListener _server;
    private int? _portRunning;
    private bool _isServerRunning = false;

    public Server(){}
    public async Task<OperationalResult> StartServerAsync(int port)
    {
        if (_isServerRunning)
            return OperationalResult.Failure(
                _portRunning != null ? 
                $"Server Allready running {_portRunning}"
                :"UnFound Running port", ErrorType.Conflict);

        try
        {
            _server = new TcpListener(IPAddress.Any, port);
            _server.Start();
            _portRunning = port;
            _isServerRunning = true;
        }
        catch(Exception ex)
        {
            return OperationalResult.Failure(
                "Error Starting server",
                ErrorType.Iternal,
                ex);
        }
    }
}