using System.Net;
using Core.Models;

Console.WriteLine("=====================================");
Console.WriteLine("Welcome to the Clipboard Manager!");


var node = new Node(){IPEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8080)};


Console.WriteLine($"Node ID: {node.Id}");
Console.WriteLine($"Node IP: {node.IPEndPoint.Address}");