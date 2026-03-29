#region Using
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
#endregion

namespace ClipBoard;

public enum MessageType
{
    FileTransfer,
    ClipboardSync,
    Heartbeat,
    Disconnect,
    FileListRequest,
    FileListResponse
}

public class NetworkMessage
{
    public MessageType Type { get; set; }
    public string SenderId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public object Data { get; set; }

    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = false };

    public byte[] Serialize()
    {
        var json = JsonSerializer.Serialize(this, _jsonSerializerOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    public static NetworkMessage Deserialize(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<NetworkMessage>(json);
    }
}

public class FileTransferInfo
{
    public string FileName { get; set; }
    public long FileSize { get; set; }
    public byte[] FileData { get; set; }
    public string DestinationPath { get; set; }
}

public class ClipboardData
{
    public string Text { get; set; }
    public DateTime Timestamp { get; set; }
    public string SenderId { get; set; }
}

public class FileListInfo
{
    public List<string> Files { get; set; }
    public string Directory { get; set; }
}

public class P2PFileShareApp
{
    private TcpListener _server;
    private TcpClient _client;
    private NetworkStream _stream;
    private CancellationTokenSource _cts;
    private string _shareDirectory;
    private string _peerId;
    private bool _isConnected;
    private bool _isServerRunning;
    private string _lastClipboardText = "";
    private string _lastSentClipboardText = "";
    private readonly object _lock = new();

    // Windows API imports for clipboard
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_TEXT = 1;
    private const uint GMEM_MOVEABLE = 0x0002;

    public P2PFileShareApp()
    {
        _peerId = Environment.MachineName;
        _shareDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "P2PFileShare");

        Directory.CreateDirectory(_shareDirectory);
    }

    public async Task RunAsync()
    {
        Console.WriteLine("╔════════════════════════════════════════╗");
        Console.WriteLine("║     P2P File Share & Clipboard Sync    ║");
        Console.WriteLine("║           Version 2.2                  ║");
        Console.WriteLine("╚════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Device ID: {_peerId}");
        Console.WriteLine($"Share Directory: {_shareDirectory}");
        Console.WriteLine();

        _cts = new CancellationTokenSource();

        // Start clipboard monitoring
        _ = Task.Run(() => MonitorClipboardAsync(_cts.Token));

        await MainMenuAsync();
    }

    private async Task MainMenuAsync()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("┌─────────────────────────────────────┐");
            Console.WriteLine("│           MAIN MENU                 │");
            Console.WriteLine("├─────────────────────────────────────┤");
            Console.WriteLine("│ 1. Start as Server                 │");
            Console.WriteLine("│ 2. Connect to Server               │");
            Console.WriteLine("│ 3. Show File List                  │");
            Console.WriteLine("│ 4. Send File                       │");
            Console.WriteLine("│ 5. Send Text to Clipboard          │");
            Console.WriteLine("│ 6. Show Connection Status          │");
            Console.WriteLine("│ 7. Disconnect                      │");
            Console.WriteLine("│ 8. Exit                            │");
            Console.WriteLine("└─────────────────────────────────────┘");
            Console.Write("Select action: ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                    await StartServerAsync();
                    break;
                case "2":
                    await ConnectToServerAsync();
                    break;
                case "3":
                    ShowFileList();
                    break;
                case "4":
                    await SendFileAsync();
                    break;
                case "5":
                    await SendClipboardTextAsync();
                    break;
                case "6":
                    ShowStatus();
                    break;
                case "7":
                    await DisconnectAsync();
                    break;
                case "8":
                    await ShutdownAsync();
                    return;
                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    break;
            }
        }
    }

    private async Task StartServerAsync()
    {
        if (_isServerRunning)
        {
            Console.WriteLine("Server is already running!");
            return;
        }

        Console.Write("Enter server port (8888): ");
        var portStr = Console.ReadLine();
        int port = string.IsNullOrEmpty(portStr) ? 8888 : int.Parse(portStr);

        try
        {
            _server = new TcpListener(IPAddress.Any, port);
            _server.Start();
            _isServerRunning = true;
            Console.WriteLine($"✅ Server started on port {port}");
            Console.WriteLine($"   Waiting for connections...");

            _ = Task.Run(() => AcceptClientsAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error starting server: {ex.Message}");
        }
    }

    private async Task AcceptClientsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isServerRunning)
        {
            try
            {
                var client = await _server.AcceptTcpClientAsync();
                Console.WriteLine($"🔗 Client connected: {client.Client.RemoteEndPoint}");

                if (_isConnected)
                {
                    Console.WriteLine("   Disconnecting previous client...");
                    await DisconnectAsync();
                }

                _client = client;
                _stream = client.GetStream();
                _isConnected = true;

                _ = Task.Run(() => ReceiveMessagesAsync(token));
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    Console.WriteLine($"❌ Error accepting connection: {ex.Message}");
            }
        }
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
                SenderId = _peerId,
                Data = $"Connected {_peerId}"
            };
            await SendMessageAsync(helloMsg);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Connection error: {ex.Message}");
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isConnected)
        {
            try
            {
                var lengthBytes = new byte[4];
                int bytesRead = 0;
                while (bytesRead < 4)
                {
                    int read = await _stream.ReadAsync(lengthBytes, bytesRead, 4 - bytesRead, token);
                    if (read == 0) throw new Exception("Connection closed");
                    bytesRead += read;
                }

                int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                var messageBytes = new byte[messageLength];
                bytesRead = 0;
                while (bytesRead < messageLength)
                {
                    int read = await _stream.ReadAsync(messageBytes, bytesRead, messageLength - bytesRead, token);
                    if (read == 0) throw new Exception("Connection closed");
                    bytesRead += read;
                }

                var message = NetworkMessage.Deserialize(messageBytes);
                await ProcessMessageAsync(message);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Console.WriteLine($"⚠️ Connection lost: {ex.Message}");
                    _isConnected = false;
                }
                break;
            }
        }
    }

    private async Task ProcessMessageAsync(NetworkMessage message)
    {
        switch (message.Type)
        {
            case MessageType.ClipboardSync:
                var clipboardData = JsonSerializer.Deserialize<ClipboardData>(
                    JsonSerializer.Serialize(message.Data));
                
                // Don't process if this is our own message
                if (clipboardData.SenderId == _peerId)
                {
                    break;
                }
                
                Console.WriteLine($"📋 Received clipboard text from {message.SenderId}:");
                Console.WriteLine($"   {clipboardData.Text}");
                
                try
                {
                    bool success = SetWindowsClipboardText(clipboardData.Text);
                    if (success)
                    {
                        Console.WriteLine("   ✅ Text copied to system clipboard");
                        // Update last clipboard text to prevent echo
                        _lastClipboardText = clipboardData.Text;
                    }
                    else
                    {
                        Console.WriteLine("   ⚠️ Failed to copy to system clipboard");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Failed to copy to clipboard: {ex.Message}");
                }
                break;

            case MessageType.FileTransfer:
                var fileInfo = JsonSerializer.Deserialize<FileTransferInfo>(
                    JsonSerializer.Serialize(message.Data));
                await ReceiveFileAsync(fileInfo);
                break;

            case MessageType.Heartbeat:
                Console.WriteLine($"💓 Heartbeat from {message.SenderId}: {message.Data}");
                break;

            case MessageType.Disconnect:
                Console.WriteLine($"👋 {message.SenderId} disconnected");
                _isConnected = false;
                break;

            case MessageType.FileListRequest:
                await SendFileListAsync();
                break;

            case MessageType.FileListResponse:
                var fileList = JsonSerializer.Deserialize<FileListInfo>(
                    JsonSerializer.Serialize(message.Data));
                Console.WriteLine($"\n📁 File list from {message.SenderId}:");
                foreach (var file in fileList.Files)
                {
                    Console.WriteLine($"   - {file}");
                }
                break;
        }
    }

    private async Task SendMessageAsync(NetworkMessage message)
    {
        if (!_isConnected || _stream == null) return;

        lock (_lock)
        {
            var data = message.Serialize();
            var lengthBytes = BitConverter.GetBytes(data.Length);
            _stream.Write(lengthBytes, 0, lengthBytes.Length);
            _stream.Write(data, 0, data.Length);
        }
        await Task.CompletedTask;
    }

    private async Task SendFileAsync()
    {
        if (!_isConnected)
        {
            Console.WriteLine("❌ Not connected to a server!");
            return;
        }

        Console.Write("Enter file path to send: ");
        var filePath = Console.ReadLine();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Console.WriteLine("❌ File not found!");
            return;
        }

        try
        {
            var fileName = Path.GetFileName(filePath);
            var fileData = await File.ReadAllBytesAsync(filePath);

            var fileInfo = new FileTransferInfo
            {
                FileName = fileName,
                FileSize = fileData.Length,
                FileData = fileData,
                DestinationPath = _shareDirectory
            };

            var message = new NetworkMessage
            {
                Type = MessageType.FileTransfer,
                SenderId = _peerId,
                Data = fileInfo
            };

            Console.WriteLine($"📤 Sending file {fileName} ({GetFileSize(fileData.Length)})...");
            await SendMessageAsync(message);
            Console.WriteLine("✅ File sent successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error sending file: {ex.Message}");
        }
    }

    private async Task ReceiveFileAsync(FileTransferInfo fileInfo)
    {
        var filePath = Path.Combine(_shareDirectory, fileInfo.FileName);

        if (File.Exists(filePath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.FileName);
            var ext = Path.GetExtension(fileInfo.FileName);
            var counter = 1;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(_shareDirectory, $"{nameWithoutExt}_{counter}{ext}");
                counter++;
            }
        }

        await File.WriteAllBytesAsync(filePath, fileInfo.FileData);
        Console.WriteLine($"📥 Received file: {fileInfo.FileName} ({GetFileSize(fileInfo.FileSize)})");
        Console.WriteLine($"   Saved as: {Path.GetFileName(filePath)}");
    }

    private async Task SendClipboardTextAsync()
    {
        if (!_isConnected)
        {
            Console.WriteLine("❌ Not connected to a server!");
            return;
        }

        Console.Write("Enter text to send to shared clipboard: ");
        var text = Console.ReadLine();

        if (string.IsNullOrEmpty(text))
        {
            Console.WriteLine("❌ Text cannot be empty!");
            return;
        }

        await SendClipboardDataAsync(text);
        Console.WriteLine($"📋 Text sent to shared clipboard: {text.Length} characters");
        
        // Also update local clipboard
        SetWindowsClipboardText(text);
        _lastClipboardText = text;
    }

    private async Task SendClipboardDataAsync(string text)
    {
        var clipboardData = new ClipboardData
        {
            Text = text,
            Timestamp = DateTime.Now,
            SenderId = _peerId
        };

        var message = new NetworkMessage
        {
            Type = MessageType.ClipboardSync,
            SenderId = _peerId,
            Data = clipboardData
        };

        await SendMessageAsync(message);
        _lastSentClipboardText = text;
    }

    private async Task MonitorClipboardAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var currentText = GetWindowsClipboardText();
                
                // Check if clipboard changed and we're connected
                if (!string.IsNullOrEmpty(currentText) && 
                    currentText != _lastClipboardText && 
                    _isConnected &&
                    currentText != _lastSentClipboardText)
                {
                    Console.WriteLine($"\n📋 Clipboard changed detected!");
                    Console.WriteLine($"   New text: {currentText}");
                    
                    _lastClipboardText = currentText;
                    
                    // Send to peer
                    await SendClipboardDataAsync(currentText);
                    Console.WriteLine($"   ✅ Text sent to peer");
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    Console.WriteLine($"⚠️ Clipboard monitoring error: {ex.Message}");
            }

            await Task.Delay(500, token); // Check every 500ms for faster response
        }
    }

    // Windows Clipboard functions
    private string GetWindowsClipboardText()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return null;

            try
            {
                if (!IsClipboardFormatAvailable(CF_TEXT))
                    return null;

                IntPtr handle = GetClipboardData(CF_TEXT);
                if (handle == IntPtr.Zero)
                    return null;

                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    return null;

                try
                {
                    string text = Marshal.PtrToStringAnsi(pointer);
                    return text;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return null;
        }
    }

    private bool SetWindowsClipboardText(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                EmptyClipboard();
                
                byte[] bytes = Encoding.ASCII.GetBytes(text + "\0");
                IntPtr handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                
                if (handle == IntPtr.Zero)
                    return false;

                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                {
                    GlobalFree(handle);
                    return false;
                }

                try
                {
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(handle);
                }

                SetClipboardData(CF_TEXT, handle);
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return false;
        }
    }

    private void ShowFileList()
    {
        Console.WriteLine($"\n📁 Files in {_shareDirectory}:");
        var files = Directory.GetFiles(_shareDirectory);

        if (files.Length == 0)
        {
            Console.WriteLine("   No files");
            return;
        }

        for (int i = 0; i < files.Length; i++)
        {
            var fileInfo = new FileInfo(files[i]);
            Console.WriteLine($"   {i + 1}. {Path.GetFileName(files[i])} ({GetFileSize(fileInfo.Length)})");
        }
    }

    private async Task SendFileListAsync()
    {
        var files = new List<string>();
        var directoryFiles = Directory.GetFiles(_shareDirectory);

        foreach (var file in directoryFiles)
        {
            var fileInfo = new FileInfo(file);
            files.Add($"{Path.GetFileName(file)} ({GetFileSize(fileInfo.Length)})");
        }

        var fileListInfo = new FileListInfo
        {
            Files = files,
            Directory = _shareDirectory
        };

        var message = new NetworkMessage
        {
            Type = MessageType.FileListResponse,
            SenderId = _peerId,
            Data = fileListInfo
        };

        await SendMessageAsync(message);
    }

    private async Task DisconnectAsync()
    {
        if (_isConnected)
        {
            var disconnectMsg = new NetworkMessage
            {
                Type = MessageType.Disconnect,
                SenderId = _peerId,
                Data = "Disconnecting"
            };
            await SendMessageAsync(disconnectMsg);

            _stream?.Close();
            _client?.Close();
            _isConnected = false;
            Console.WriteLine("✅ Disconnected from server");
        }

        if (_isServerRunning)
        {
            _server?.Stop();
            _isServerRunning = false;
            Console.WriteLine("✅ Server stopped");
        }
    }

    private async Task ShutdownAsync()
    {
        Console.WriteLine("Shutting down...");
        _cts.Cancel();
        await DisconnectAsync();
        Console.WriteLine("Goodbye!");
    }

    private void ShowStatus()
    {
        Console.WriteLine("\n┌─────────────────────────────────────┐");
        Console.WriteLine("│         CONNECTION STATUS           │");
        Console.WriteLine("├─────────────────────────────────────┤");
        Console.WriteLine($"│ Device ID:   {_peerId,-24}│");
        Console.WriteLine($"│ Server mode: {(_isServerRunning ? "Active" : "Inactive"),-24}│");
        Console.WriteLine($"│ Connected:   {(_isConnected ? "Yes" : "No"),-24}│");
        Console.WriteLine($"│ Directory:   {_shareDirectory,-24}│");
        Console.WriteLine("└─────────────────────────────────────┘");
    }

    private string GetFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public static async Task Main(string[] args)
    {
        var app = new P2PFileShareApp();
        await app.RunAsync();
    }
}