using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
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
            logger.LogDebug("Target not found");

            target = world.Grid.FindCreature(packet.Target);
            if (target != null)
            {
                logger.LogDebug("Target found in another chunk");
            }
            
            return;
        }
        
        if (!IsFacingTarget(connection.Character!, target))
        {
            logger.LogTrace("Character is not facing the target");
            return;
        }

        if (packet.AutoAttack)
        {
            // Auto attack
            
            var distance = Vector3.Distance(connection.Character!.Position, target.Position);
            if (distance >= 1.5f)
            {
                logger.LogTrace("Target out of range");
                return;
            }
            
            // For now, just attack without any additional logic
            target.OnHit(attacker, 10);
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
    }
    
    private bool IsFacingTarget(ICharacter character, ICreature target)
    {
        var direction = target.Position - character.Position;
        direction.Normalize(); // Should it be normalized?
        
        // Orientation is in degrees (basically the Y rotation)
        var characterOrientation = character.Orientation.y;
        var forward = new Vector3(Mathf.Sin(characterOrientation * Mathf.Rad2Deg), 0, Mathf.Cos(characterOrientation * Mathf.Rad2Deg));
        var dot = Vector3.Dot(forward, direction);
        var angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
        
        return angle < 45;
    }
}
