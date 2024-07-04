using System;

namespace Avalon.Network.Packets.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class PacketAttribute : Attribute
{
    public ComponentType HandleOn { get; set; } = ComponentType.World;
    public NetworkPacketType Type { get; set; } = NetworkPacketType.UNKNOWN;
}

public enum ComponentType
{
    Auth,
    World,
    Client
}
