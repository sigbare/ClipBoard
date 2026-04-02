namespace Core.Models.Enums;

[Flags]
public enum NodeType
{   
    None = 0,
    Peer = 1,
    Seed = 1 << 1,
    Relay = 1 << 2,
    Bootstrap = 1 << 3
}