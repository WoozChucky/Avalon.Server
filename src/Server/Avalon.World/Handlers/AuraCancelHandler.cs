using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// CMSG_AURA_CANCEL (auras), on the tick, in the map pass: ends the sender's own helpful aura (the copy its key names,
/// or every copy when it names none) through its instance's aura system, answered with exactly one
/// SMSG_AURA_CANCEL_RESULT. The answer goes out at once, ahead of the aura update the instance sends later in the tick.
/// A connection with no character gets nothing; a failure is logged and answered NotFound.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_AURA_CANCEL)]
public class AuraCancelHandler(IWorld world, ILogger<AuraCancelHandler> logger) : WorldPacketHandler<CAuraCancelPacket>
{
    public override void Execute(IWorldConnection connection, CAuraCancelPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_AURA_CANCEL from a connection with no character");
            return;
        }

        AuraCancelResult result;
        try
        {
            result = world.InstanceRegistry.GetInstanceById(character.InstanceId) is IAuraHost host
                ? host.Auras.Cancel(character, new AuraId(packet.AuraId), packet.InstanceKey)
                : AuraCancelResult.NotFound;
        }
        catch (Exception e)
        {
            logger.LogError(e, "CMSG_AURA_CANCEL {AuraId} of {Character} threw; answering NotFound", packet.AuraId,
                character.Name);
            result = AuraCancelResult.NotFound;
        }

        connection.Send(SAuraCancelResultPacket.Create(packet.AuraId, result, connection.CryptoSession.Encrypt));
    }
}
