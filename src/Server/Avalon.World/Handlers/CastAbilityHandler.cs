using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CAST_ABILITY)]
public class CastAbilityHandler(ILogger<CastAbilityHandler> logger, IWorld world, CombatConfig combatConfig)
    : WorldPacketHandler<CCastAbilityPacket>
{
    // Facing-cone half-angle in degrees. Mirrors CharacterAttackHandler.MaxAngle so the cast
    // pipeline applies the same facing tolerance as the (now-deprecated) attack pipeline.
    private const uint MaxFacingAngle = 65;

    public override void Execute(IWorldConnection connection, CCastAbilityPacket packet)
    {
        var attacker = connection.Character;
        if (attacker is null || attacker.IsDead)
        {
            logger.LogDebug("Dropped CMSG_CAST_ABILITY from dead/missing char");
            return;
        }

        // GCD check uses packet.AbilityId directly — no ability resolution required.
        // Logically: if the player is still inside the global cooldown window, reject before
        // we even bother validating ability ownership.
        double sinceLastCast = (DateTime.UtcNow - attacker.LastCastStartTime).TotalMilliseconds;
        if (sinceLastCast < combatConfig.GcdMs)
        {
            uint remaining = (uint)(combatConfig.GcdMs - sinceLastCast);
            connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, remaining,
                connection.CryptoSession.Encrypt));
            return;
        }

        IAbility? ability = attacker.Spells[packet.AbilityId];
        if (ability is null)
        {
            logger.LogDebug("Ability {AbilityId} not owned", packet.AbilityId);
            return;
        }

        if (ability.CooldownTimer > 0)
        {
            connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, ability.CooldownTimer,
                connection.CryptoSession.Encrypt));
            return;
        }

        AbilityMetadata meta = ability.Metadata;

        // Combat-state gating. Some abilities (e.g. mounts, regeneration buffs) only fire
        // out-of-combat; others (e.g. execute-style finishers) only fire in-combat.
        if (meta.Flags.HasFlag(AbilityFlags.RequiresOutOfCombat) && attacker.IsInCombat)
        {
            connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, 0,
                connection.CryptoSession.Encrypt));
            return;
        }
        if (meta.Flags.HasFlag(AbilityFlags.RequiresInCombat) && !attacker.IsInCombat)
        {
            connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, 0,
                connection.CryptoSession.Encrypt));
            return;
        }

        // Cost check against CurrentPower (the spendable pool). Power deduction itself happens
        // inside InstanceAbilityCastSystem.QueueAbility for the queued path; for the instant
        // path we mirror that here so the resource cost is paid before script execution.
        if (meta.Cost > 0 && (attacker.CurrentPower ?? 0) < meta.Cost)
        {
            connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, 0,
                connection.CryptoSession.Encrypt));
            return;
        }

        // Resolve the live simulation context for the player's current instance.
        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(attacker.InstanceId);
        if (context is null)
        {
            logger.LogWarning("Instance not found for character {CharacterId}", attacker.Guid);
            return;
        }

        // Resolve target if specified. Targetless abilities (AoE / self-buffs) skip range and
        // facing entirely — the script is responsible for finding affected units.
        IUnit? target = null;
        if (packet.TargetGuid is { } targetGuidRaw)
        {
            target = ResolveTarget(context, new ObjectGuid(targetGuidRaw));
            if (target is null)
            {
                logger.LogDebug("Target {TargetGuid} not found", targetGuidRaw);
                return;
            }

            // Facing check: silent drop matches existing CharacterAttackHandler behavior — the
            // client tracks orientation and shouldn't ever fire a cast it can't satisfy.
            if (!IsFacingTarget(attacker, target))
            {
                logger.LogTrace("Character {Name} is not facing target {Target}", attacker.Name, target.Guid);
                return;
            }

            float distance = Vector3.Distance(attacker.Position, target.Position);
            if (distance > (float)meta.Range)
            {
                connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, 0,
                    connection.CryptoSession.Encrypt));
                return;
            }
        }

        // Cast dispatch. Cast-time abilities go through the queue (movement-interruptible);
        // instant abilities run inline and immediately enter cooldown.
        if (meta.CastTime > 0)
        {
            if (context.QueueAbility(attacker, target, ability))
            {
                attacker.MarkCombat();
                context.BroadcastUnitStartCast(attacker, meta.CastTime);
            }
            // QueueAbility handles its own cost-failure path (returns false) — no extra packet
            // here; the cost check above already pre-validated, so a false return implies a
            // PowerType mismatch we can't recover from.
        }
        else
        {
            // Instant path: deduct cost here (the queued path defers to QueueAbility).
            if (meta.Cost > 0 &&
                attacker.PowerType is (PowerType.Mana or PowerType.Energy) &&
                attacker.CurrentPower.HasValue)
            {
                attacker.CurrentPower = attacker.CurrentPower.Value - meta.Cost;
            }

            context.RunInstantAbility(attacker, target, ability);
            attacker.MarkCombat();
        }

        // GCD anchor: stamps the start of this cast for the next GCD calculation.
        attacker.LastCastStartTime = DateTime.UtcNow;
    }

    private IUnit? ResolveTarget(ISimulationContext context, ObjectGuid targetGuid)
    {
        switch (targetGuid.Type)
        {
            case ObjectType.Creature:
                return context.Creatures.TryGetValue(targetGuid, out ICreature? creature) ? creature : null;
            case ObjectType.Character:
                return context.Characters.TryGetValue(targetGuid, out ICharacter? character) ? character : null;
            default:
                logger.LogWarning("Invalid target type {Type}", targetGuid.Type);
                return null;
        }
    }

    private bool IsFacingTarget(IUnit caster, IUnit target)
    {
        Vector3 direction = target.Position - caster.Position;
        direction.Normalize();

        // Orientation.y is the yaw in degrees (right-handed Y-up).
        float radians = caster.Orientation.y * Mathf.Deg2Rad;
        Vector3 forward = new(Mathf.Sin(radians), 0, Mathf.Cos(radians));

        float angle = Vector3.Angle(forward, direction);
        return angle < MaxFacingAngle;
    }
}
