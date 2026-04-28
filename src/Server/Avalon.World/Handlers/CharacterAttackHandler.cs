using Avalon.World.Public;
using System.Diagnostics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_ATTACK)]
public class CharacterAttackHandler(ILogger<CharacterAttackHandler> logger, IWorld world)
    : WorldPacketHandler<CCharacterAttackPacket>
{
    private const float MAxMeleeAttackRange = 1.5f;
    private const uint MaxAngle = 65;

    private IUnit? GetTarget(ISimulationContext context, ObjectGuid targetGuid)
    {
        IUnit? target = null;

        switch (targetGuid.Type)
        {
            case ObjectType.Creature:
                if (context.Creatures.TryGetValue(targetGuid, out ICreature? creature))
                {
                    target = creature;
                }

                break;
            case ObjectType.Character:
                if (context.Characters.TryGetValue(targetGuid, out ICharacter? character))
                {
                    target = character;
                }

                break;
            default:
                logger.LogWarning("Invalid target type");
                break;
        }

        return target;
    }

    public override void Execute(IWorldConnection connection, CCharacterAttackPacket packet)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(CharacterAttackHandler),
            ActivityKind.Server);
        activity?.SetTag("connection.accountId", connection.AccountId);
        activity?.SetTag("connection.characterId", connection.Character?.Guid.Id);

        ICharacter attacker = connection.Character!;

        if (attacker.IsDead)
        {
            logger.LogDebug("Dropped CMSG_ATTACK from dead char {Name}", attacker.Name);
            return;
        }

        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(attacker.InstanceId);
        if (context == null)
        {
            logger.LogWarning("Instance not found for character {CharacterId}", attacker.Guid);
            activity?.AddEvent(new ActivityEvent("InstanceNotFound"));
            return;
        }

        // TODO: Some attacks might not require a target (e.g. AoE spells)
        ObjectGuid targetGuid = new(packet.Target);

        IUnit? target = GetTarget(context, targetGuid);
        if (target == null)
        {
            logger.LogDebug("Target not found");
            activity?.AddEvent(new ActivityEvent("TargetNotFound"));
            return;
        }

        if (!IsFacingTarget(attacker, target))
        {
            logger.LogTrace("Character {Name} is not facing the target {Target}", attacker.Name, target.Guid);
            activity?.AddEvent(new ActivityEvent("NotFacingTarget"));
            return;
        }

        if (packet.AutoAttack)
        {
            // Auto attack

            float distance = Vector3.Distance(attacker.Position, target.Position);
            if (distance >= MAxMeleeAttackRange)
            {
                logger.LogTrace("Target {Target} out of range ({Distance})", target.Guid, distance);
                activity?.AddEvent(new ActivityEvent("TargetOutOfRange"));
                return;
            }

            // For now, just attack without any additional logic
            target.OnHit(attacker, 10);
            attacker.MarkCombat();
        }
        else
        {
            // Cast spell
            if (packet.SpellId == null)
            {
                logger.LogWarning("SpellId is null");
                activity?.AddEvent(new ActivityEvent("SpellIdNull"));
                return;
            }

            ISpell? spell = attacker.Spells[packet.SpellId];
            if (spell == null)
            {
                logger.LogWarning("Spell not found");
                activity?.AddEvent(new ActivityEvent("SpellNotFound"));
                return;
            }

            if (spell.CooldownTimer > 0)
            {
                logger.LogWarning("Spell on cooldown, timer: {Timer}", spell.Metadata.Cooldown - spell.CooldownTimer);
                activity?.AddEvent(new ActivityEvent("SpellOnCooldown"));
                return;
            }

            if (!attacker.Spells.IsCasting && spell is {CooldownTimer: <= 0, Casting: false} &&
                context.QueueSpell(attacker, target, spell))
            {
                attacker.MarkCombat();
                // Send spell start cast packet
                if (spell.Metadata.CastTime > 0)
                {
                    context.BroadcastUnitStartCast(attacker, spell.Metadata.CastTime);
                }
            }
            else
            {
                // Send spell not ready packet
                connection.Send(SSpellNotReadyPacket.Create(packet.SpellId.Value, spell.CooldownTimer,
                    connection.CryptoSession.Encrypt));
            }
        }
    }

    private bool IsFacingTarget(ICharacter character, IUnit target)
    {
        Vector3 direction = target.Position - character.Position;
        direction.Normalize();

        // Orientation is in degrees (the Y rotation)
        float characterOrientation = character.Orientation.y;
        float radians = characterOrientation * Mathf.Deg2Rad;
        Vector3 forward = new(Mathf.Sin(radians), 0, Mathf.Cos(radians));

        float angle = Vector3.Angle(forward, direction);

        logger.LogTrace("Angle: {Angle}", angle);

        return angle < MaxAngle;
    }
}

