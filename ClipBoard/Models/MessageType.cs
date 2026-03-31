namespace ClipBoard.Models;

public enum MessageType
{
    FileTransfer,
    ClipboardSync,
    Heartbeat,
    Disconnect,
    FileListRequest,
    FileListResponse
}