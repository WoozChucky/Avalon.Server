using System.Reflection;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.World;

/// <summary>
/// The World's game layer: every <see cref="PacketHandlerAttribute" /> type in this assembly, which the
/// <see cref="WorldServer" /> constructor builds and runs on the tick. Registered as an <see cref="IPacketHandlerLayer" />
/// so that <see cref="PacketManager" />, which maps only the connection layer, does not report these packets as
/// lacking a handler.
/// </summary>
public sealed class GamePacketHandlerLayer : IPacketHandlerLayer
{
    public IEnumerable<NetworkPacketType> PacketTypes => Discover().Keys;

    /// <summary>
    /// The handler type for each packet type, found by attribute. Throws when two handlers name the same packet type.
    /// </summary>
    internal static Dictionary<NetworkPacketType, Type> Discover() =>
        typeof(GamePacketHandlerLayer).Assembly.GetTypes()
            .Where(x => x.GetCustomAttribute<PacketHandlerAttribute>() != null)
            .ToDictionary(x => x.GetCustomAttribute<PacketHandlerAttribute>()!.PacketType, x => x);
}
