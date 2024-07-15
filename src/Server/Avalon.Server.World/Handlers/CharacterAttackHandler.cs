using Avalon.Network.Packets.Combat;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class CharacterAttackHandler(ILogger<CharacterAttackHandler> logger, IWorld world) : IWorldPacketHandler<CCharacterAttackPacket>
{
    public Task ExecuteAsync(WorldPacketContext<CCharacterAttackPacket> ctx, CancellationToken token = default)
    {
        var packet = ctx.Packet;
        
        var attacker = ctx.Connection.Character;
        if (attacker == null)
        {
            logger.LogWarning("Character not found in connection");
            return Task.CompletedTask;
        }

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
            
            
            return Task.CompletedTask;
        }

        if (packet.AutoAttack)
        {
            
        }
        else
        {
            if (packet.SpellId == null)
            {
                logger.LogWarning("SpellId is null");
                return Task.CompletedTask;
            }
        }
        
        // For now, just attack without any additional logic
        target.OnHit(attacker, 10);
        
        return Task.CompletedTask;
    }
}
