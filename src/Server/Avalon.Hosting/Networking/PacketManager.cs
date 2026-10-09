using System.Reflection;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

/// <summary>
/// Stores meta information about packets: each one's type and the handler it dispatches to. Built once, at
/// startup, and read-only afterwards, so every connection reads it concurrently without a lock (#866).
/// </summary>
public interface IPacketManager
{
    /// <summary>
    /// Try to get a packet info by packet type
    /// </summary>
    /// <param name="packetType"></param>
    /// <param name="info"></param>
    /// <returns>True if found</returns>
    bool TryGetPacketInfo(NetworkPacketType packetType, out PacketInfo info);
}

public class PacketManager : IPacketManager
{
    private readonly Dictionary<NetworkPacketType, PacketInfo> _infos = new();

    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="packetTypes">The packet types this server receives.</param>
    /// <param name="packetHandlerTypes">The <see cref="IPacketHandlerNew" /> handlers this manager dispatches to.</param>
    /// <param name="otherLayers">
    /// Handler layers that dispatch their packets themselves (<see cref="IPacketHandlerLayer" />). A packet one of them
    /// handles gets no info here, exactly like an unhandled one, but is not reported as lacking a handler.
    /// </param>
    public PacketManager(ILoggerFactory loggerFactory, IEnumerable<Type> packetTypes, Type[]? packetHandlerTypes = null,
        IEnumerable<IPacketHandlerLayer>? otherLayers = null)
    {
        HashSet<NetworkPacketType> handledElsewhere = otherLayers?.SelectMany(layer => layer.PacketTypes).ToHashSet() ?? [];
        ILogger<PacketManager> logger = loggerFactory.CreateLogger<PacketManager>();
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static;
        foreach (Type packetType in packetTypes)
        {
            FieldInfo? networkPacketTypeInfo = packetType.GetFields(Flags)
                .FirstOrDefault(field => field.FieldType == typeof(NetworkPacketType));
            if (networkPacketTypeInfo == null)
            {
                logger.LogWarning("Packet type {PacketType} does not have a NetworkPacketType field", packetType);
                continue;
            }

            var networkPacketType = (NetworkPacketType)networkPacketTypeInfo!.GetValue(null)!;

            // last or default so it can be overriden via plugins - last one is chosen
            Type? packetHandlerType = packetHandlerTypes?
                .LastOrDefault(x =>
                    x is { IsAbstract: false, IsInterface: false } &&
                    x
                        .GetInterfaces()
                        .Any(i => i.IsGenericType && i.GenericTypeArguments.First() == packetType)
                );

            if (packetHandlerType == null)
            {
                if (handledElsewhere.Contains(networkPacketType))
                {
                    logger.LogDebug("Packet {PacketType} is handled by another handler layer", packetType);
                }
                else
                {
                    logger.LogWarning("Packet {PacketType} does not have a handler", packetType);
                }

                continue;
            }

            _infos.Add(networkPacketType, new PacketInfo(packetType, packetHandlerType));

            logger.LogDebug("Registered packet {Header} with handler {HandlerType}", networkPacketType, packetHandlerType);

        }
    }

    public bool TryGetPacketInfo(NetworkPacketType packetType, out PacketInfo packetInfo)
    {
        return _infos.TryGetValue(packetType, out packetInfo);
    }
}
