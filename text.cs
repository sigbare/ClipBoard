private NetworkStream _stream;
    private CancellationTokenSource _cts;
    private readonly string _shareDirectory;
    private readonly string _peerId;
    private bool _isConnected;
    
    private string _lastClipboardText = "";
    private string _lastSentClipboardText = "";
    private readonly Lock _lock = new();
    private readonly bool _isLinux;
    private readonly PipeReader _reader;


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

    [DllImport("user32.dll")]
    private static extern int GetClipboardSequenceNumber();

    private const uint CF_TEXT = 1;
    private const uint CF_UNICODETEXT = 13;
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
        ConsoleUi.HelloMessage(_isLinux);

        _cts = new CancellationTokenSource();

        _ = Task.Run(() => MonitorClipboardAsync(_cts.Token));

        await MainMenuAsync();
    }

    private async Task MainMenuAsync()
    {
        while (true)
        {
            ConsoleUi.MainMenu();
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
                    ConsoleUi.ShowStatus(_peerId,_isServerRunning,_isConnected,_shareDirectory,_isLinux);
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

    private async Task ReceiveMessagesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isConnected)
        {
           ReadResult result = await _reader.ReadAsync(token);
           ReadOnlySequence<byte> buffer = result.Buffer;

            try
            {
                while (TryReadMessage(ref buffer, out var message))
                {
                    if(message != null)
                        await ProcessMessageAsync(message);
                }

                if (result.IsCompleted) break;
            }
            finally
            {
                _reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
    }

    private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out NetworkMessage? message)
    {
        message = null;
        if (buffer.Length < 4) return false;

        int length = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(0, 4).ToArray());

        if (buffer.Length < 4 + length) return false;

        var payload = buffer.Slice(4, length);
        message = NetworkMessage.Deserialize(payload);

        buffer = buffer.Slice(buffer.GetPosition(4 + length));
        return true;
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
                    bool success = SetClipboardText(clipboardData.Text);
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
        SetClipboardText(text);
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
        int lastSequenceNumber = 0;
        
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_isLinux)
                {
                    // On Windows, use sequence number for efficient monitoring
                    int currentSequence = GetClipboardSequenceNumber();
                    if (currentSequence != lastSequenceNumber)
                    {
                        lastSequenceNumber = currentSequence;
                        string currentText = GetWindowsClipboardText();
                        
                        if (!string.IsNullOrEmpty(currentText) && 
                            currentText != _lastClipboardText && 
                            _isConnected &&
                            currentText != _lastSentClipboardText)
                        {
                            Console.WriteLine($"\n📋 Clipboard changed detected!");
                            Console.WriteLine($"   New text: {currentText.Substring(0, Math.Min(50, currentText.Length))}...");
                            
                            _lastClipboardText = currentText;
                            await SendClipboardDataAsync(currentText);
                            Console.WriteLine($"   ✅ Text sent to peer");
                        }
                        else if (!string.IsNullOrEmpty(currentText) && currentText != _lastClipboardText)
                        {
                            _lastClipboardText = currentText;
                        }
                    }
                }
                else
                {
                    // Linux: check clipboard
                    string currentText = GetLinuxClipboardOnly();
                    
                    if (!string.IsNullOrEmpty(currentText) && 
                        currentText != _lastClipboardText && 
                        _isConnected &&
                        currentText != _lastSentClipboardText)
                    {
                        Console.WriteLine($"\n📋 Clipboard changed detected!");
                        Console.WriteLine($"   New text: {currentText.Substring(0, Math.Min(50, currentText.Length))}...");
                        
                        _lastClipboardText = currentText;
                        await SendClipboardDataAsync(currentText);
                        Console.WriteLine($"   ✅ Text sent to peer");
                    }
                    else if (!string.IsNullOrEmpty(currentText) && currentText != _lastClipboardText)
                    {
                        _lastClipboardText = currentText;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    Console.WriteLine($"⚠️ Clipboard monitoring error: {ex.Message}");
            }

            await Task.Delay(500, token);
        }
    }

    // Cross-platform clipboard functions
    private bool SetClipboardText(string text)
    {
        if (_isLinux)
        {
            return SetLinuxClipboard(text);
        }
        else
        {
            return SetWindowsClipboardTextUnicode(text);
        }
    }

    // Windows Clipboard functions with Unicode support
    private string GetWindowsClipboardText()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return null;

            try
            {
                // Try Unicode first
                if (IsClipboardFormatAvailable(CF_UNICODETEXT))
                {
                    IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                    if (handle != IntPtr.Zero)
                    {
                        IntPtr pointer = GlobalLock(handle);
                        if (pointer != IntPtr.Zero)
                        {
                            try
                            {
                                string text = Marshal.PtrToStringUni(pointer);
                                return text;
                            }
                            finally
                            {
                                GlobalUnlock(handle);
                            }
                        }
                    }
                }
                
                // Fallback to ANSI if Unicode not available
                if (IsClipboardFormatAvailable(CF_TEXT))
                {
                    IntPtr handle = GetClipboardData(CF_TEXT);
                    if (handle != IntPtr.Zero)
                    {
                        IntPtr pointer = GlobalLock(handle);
                        if (pointer != IntPtr.Zero)
                        {
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
                    }
                }
                
                return null;
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

    private bool SetWindowsClipboardTextUnicode(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                EmptyClipboard();
                
                // Use Unicode format
                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
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

                SetClipboardData(CF_UNICODETEXT, handle);
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

    // Linux Clipboard functions with UTF-8 support
    private string GetLinuxClipboardOnly()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-selection clipboard -o",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            
            using (var process = System.Diagnostics.Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(200);
                
                if (!string.IsNullOrEmpty(output))
                {
                    return output.Trim();
                }
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    private bool SetLinuxClipboard(string text)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xclip",
                Arguments = "-selection clipboard",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            
            using (var process = System.Diagnostics.Process.Start(psi))
            {
                // Write UTF-8 encoded text
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                process.StandardInput.Close();
                process.WaitForExit(1000);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Error setting Linux clipboard: {ex.Message}");
            Console.WriteLine("   Install xclip: sudo apt-get install xclip");
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