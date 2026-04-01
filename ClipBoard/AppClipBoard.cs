

using System.Net.Sockets;
using ClipBoard.Models;
using ClipBoard.Services;

namespace ClipBoard;


public class AppClipBoard
{
    private readonly CancellationTokenSource _cts;
    private readonly SystemInfo _systemInfo;
    private Server? _server;
    public AppClipBoard()
    {
        _cts = new CancellationTokenSource();
        _systemInfo = new();
        _server = null;
    }

    public async Task RunAsync()
    {
        ConsoleUi.HelloMessage(_systemInfo.IsLinux);
    }

    private async Task AppMenuAsync()
    {
        while (_cts.IsCancellationRequested)
        {
            ConsoleUi.MainMenu();
            
            var handler = MappToAction(Console.ReadLine()) switch
            {
                UserChoice.ServerStart => ServerStartAsync(),
                _ => HandlerError()
            };

            await handler;
        }
    }

    private async Task ServerStartAsync()
    {
        if(_server != null)
        {
            Console.WriteLine("Server Already Running");
            return;
        }
        _server = new Server();

        Console.WriteLine("Enter server port (def 8888)");


        while (_cts.IsCancellationRequested)
        {
            var port = Console.ReadLine();
            if(port == null || port == "")
                port = "8888";

            if(int.TryParse(port, out var result))
            {
                try
                {
                    var serverResul = await _server.StartServerAsync(result);

                    if (serverResul.IsFailure)
                    {
                        Console.WriteLine(serverResul.UserMessage);
                    }
                    break;
                }
                catch(SocketException ex)
                {
                    if(ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        Console.WriteLine("This port alredy use try again");
                    }
                }
            }
        }
    }

    private Task HandlerError()
    {
        throw new NotImplementedException();
    }

    #region HelperMethods
    private static UserChoice MappToAction(string? input)
    {
        if(int.TryParse(input, out var result))
        {
            if(Enum.IsDefined(typeof(UserChoice), result))
            {
                return (UserChoice)result;
            }
        }
        return UserChoice.UnSupported;
    }
    #endregion

}