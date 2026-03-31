
namespace ClipBoard.Models;

public class FileListInfo
{
    public List<string> Files { get; set; } = [];
    public required string Directory { get; set; }
}