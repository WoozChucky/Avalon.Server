using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_ATTACK)]
public class CharacterAttackHandler(ILogger<CharacterAttackHandler> logger, IWorld world) : WorldPacketHandler<CCharacterAttackPacket>
{
    public override void Execute(WorldConnection connection, CCharacterAttackPacket packet)
    {
        var attacker = connection.Character!;

        var attackerChunkId = attacker.ChunkId;

        var target = world.Grid.FindCreature(packet.Target, attackerChunkId);
        if (target == null)
        {
            logger.LogWarning("Target not found");

            {
                target = world.Grid.FindCreature(packet.Target);
                if (target != null)
                {
                    logger.LogWarning("Target found in another chunk");
                }
            }
            
            return;
        }

        if (packet.AutoAttack)
        {
            // Auto attack
        }
        else
        {
            // Cast spell
            
            if (packet.SpellId == null)
            {
                logger.LogWarning("SpellId is null");
                return;
            }
        }
        
        // For now, just attack without any additional logic
        target.OnHit(attacker, 10);
    }
}
