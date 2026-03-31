

namespace ClipBoard.Models;

public class ClipboardData
{
    public required string Text { get; set; }
    public DateTime Timestamp { get; set; }
    public required string SenderId { get; set; }
}