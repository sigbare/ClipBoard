
using System.Net;
using Core.Models.Enums;

namespace Core.Models;


public class Node
{
    public string Id {get; set;}
    public string Name {get; private set;}
    public required IPEndPoint IPEndPoint {get; set;}
    public NodeStatus Status {get; set;}
    public NodeType Type {get; set;}

    private Node()
    {
        Id = Guid.NewGuid().ToString();
        Name = $"Node_{Id[..8]}";
        Status = NodeStatus.Offline;
        Type = NodeType.Peer;
    }

    public Node(IPEndPoint endPoint, string? name = null) : this()
    {
        IPEndPoint = endPoint;
        if (!string.IsNullOrWhiteSpace(name))
        {
            Name = name;
        }
    }


}