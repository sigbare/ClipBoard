using System.Buffers;
using System.Text;
using System.Text.Json;

namespace ClipBoard.Models;

public class NetworkMessage
{
    public MessageType Type { get; set; }
    public required string SenderId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public required object Data { get; set; }

    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = false };

    public byte[] Serialize()
    {
        var json = JsonSerializer.Serialize(this, _jsonSerializerOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    public static NetworkMessage Deserialize(ReadOnlySequence<byte> data)
    {
        //TODO: fix it later don't drop app
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<NetworkMessage>(json) ?? throw new NullReferenceException();
    }
}