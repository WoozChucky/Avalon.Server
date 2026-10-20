using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Auras;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Stores the player's current target on the connection. Read each tick by
/// <c>ThreatBroadcastService</c> to mirror the encounter's threat list back to the client.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_TARGET_UNIT)]
public class TargetUnitHandler(IWorld world, ILogger<TargetUnitHandler> logger)
    : WorldPacketHandler<CTargetUnitPacket>
{
    // Packet handlers run on the tick thread. This lookup key is reused, never stored in the character dictionary.
    private readonly ObjectGuid _targetLookup = new();

    public override void Execute(IWorldConnection connection, CTargetUnitPacket packet)
    {
        if (connection.Character is null || connection.Character.IsDead)
        {
            logger.LogDebug("Dropped CMSG_TARGET_UNIT from dead/missing char");
            return;
        }

        if (packet.TargetGuid is { } guid)
        {
            _targetLookup.Set(ObjectType.Character, (uint)guid);
            // Exact equality also rejects other object types and malformed high bits before looking up the character.
            if (_targetLookup.RawValue == guid
                && world.InstanceRegistry.GetInstanceById(connection.Character.InstanceId) is { } instance
                && instance.Characters.TryGetValue(_targetLookup, out ICharacter? target)
                && ArrivalProtection.IsProtected(target))
            {
                connection.CurrentTargetGuid = null;
                return;
            }
        }

        connection.CurrentTargetGuid = packet.TargetGuid;
    }
}
