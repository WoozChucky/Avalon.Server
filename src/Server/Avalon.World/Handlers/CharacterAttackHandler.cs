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
    
    private const float MAxMeleeAttackRange = 1.5f;
    private const uint MaxAngle = 65;
    
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
            logger.LogTrace("Character {Name} is not facing the target {Target}", connection.Character!.Name, target.Name);
            return;
        }

        if (packet.AutoAttack)
        {
            // Auto attack
            
            var distance = Vector3.Distance(connection.Character!.Position, target.Position);
            if (distance >= MAxMeleeAttackRange)
            {
                logger.LogTrace("Target {Target} out of range ({Distance})", target.Name, distance);
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
        direction.Normalize();
        
        // Orientation is in degrees (the Y rotation)
        var characterOrientation = character.Orientation.y;
        var radians = characterOrientation * Mathf.Deg2Rad;
        var forward = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
    
        var angle = Vector3.Angle(forward, direction);
        
        logger.LogTrace("Angle: {Angle}", angle);
        
        return angle < MaxAngle;
    }
}
