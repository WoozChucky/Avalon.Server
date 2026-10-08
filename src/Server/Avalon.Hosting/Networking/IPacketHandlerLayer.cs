using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <summary>
/// A packet handler layer that dispatches outside <see cref="PacketManager" />, such as the World's game layer, whose
/// handlers the world server finds by attribute and runs on the tick. <see cref="PacketManager" /> reads its packet
/// types once, at startup, only so that it does not warn that they have no handler; it never dispatches them.
/// </summary>
public interface IPacketHandlerLayer
{
    /// <summary>The packet types this layer has a handler for.</summary>
    IEnumerable<NetworkPacketType> PacketTypes { get; }
}
