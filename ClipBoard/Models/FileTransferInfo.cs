

namespace ClipBoard.Models;

public class FileTransferInfo
{
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public required byte[] FileData { get; set; }
    public required string DestinationPath { get; set; }
}