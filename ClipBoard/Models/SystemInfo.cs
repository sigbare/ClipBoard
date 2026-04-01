namespace ClipBoard.Models;

public class SystemInfo
{
    public bool IsLinux {get; private set;}
    public string MachinName {get; private set;}

    public SystemInfo()
    {
        IsLinux =  Environment.OSVersion.Platform == PlatformID.Unix || 
                   Environment.OSVersion.Platform == PlatformID.MacOSX;

        MachinName = Environment.MachineName;
    }
}