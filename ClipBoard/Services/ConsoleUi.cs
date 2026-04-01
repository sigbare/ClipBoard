using System.Text;

namespace ClipBoard.Services;

public static class ConsoleUi
{
    public static void HelloMessage(bool isLinux)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        
        Console.WriteLine("╔════════════════════════════════════════╗");
        Console.WriteLine("║     P2P File Share & Clipboard Sync    ║");
        Console.WriteLine("║           Version 2.5                  ║");
        Console.WriteLine($"║           OS: {(isLinux ? "Linux" : "Windows║")}                    ║");
        Console.WriteLine("╚════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Device ID: {isLinux}");
        Console.WriteLine($"Share Directory: {isLinux}");
        if (isLinux)
        {
            Console.WriteLine("Note: Only Ctrl+C copies are synced (not mouse selection)");
        }
        Console.WriteLine();
    }

    public static void MainMenu()
    {
            Console.WriteLine();
            Console.WriteLine("┌─────────────────────────────────────┐");
            Console.WriteLine("│           MAIN MENU                 │");
            Console.WriteLine("├─────────────────────────────────────┤");
            Console.WriteLine("│ 1. Start as Server                  │");
            Console.WriteLine("│ 2. Connect to Server                │");
            Console.WriteLine("│ 3. Show File List                   │");
            Console.WriteLine("│ 4. Send File                        │");
            Console.WriteLine("│ 5. Send Text to Clipboard           │");
            Console.WriteLine("│ 6. Show Connection Status           │");
            Console.WriteLine("│ 7. Disconnect                       │");
            Console.WriteLine("│ 8. Exit                             │");
            Console.WriteLine("└─────────────────────────────────────┘");
            Console.Write("Select action: ");
    }

    public static void ShowStatus(
        string peerId, bool isServerRunning, bool isConnected, string sharedDirectory, bool isLinux)
    {
        Console.WriteLine("\n┌─────────────────────────────────────┐");
        Console.WriteLine("│         CONNECTION STATUS           │");
        Console.WriteLine("├─────────────────────────────────────┤");
        Console.WriteLine($"│ Device ID:   {peerId,-24}│");
        Console.WriteLine($"│ Server mode: {(isServerRunning ? "Active" : "Inactive"),-24}│");
        Console.WriteLine($"│ Connected:   {(isConnected ? "Yes" : "No"),-24}│");
        Console.WriteLine($"│ Directory:   {sharedDirectory,-24}│");
        Console.WriteLine($"│ OS:          {(isLinux ? "Linux" : "Windows"),-24}│");
        Console.WriteLine("└─────────────────────────────────────┘");
    }
}