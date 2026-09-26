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
using Avalon.Network.Packets.State;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CAST_ABILITY)]
public class CastAbilityHandler(ILogger<CastAbilityHandler> logger, IWorld world, CombatConfig combatConfig)
    : WorldPacketHandler<CCastAbilityPacket>
{
    // Every refusal is answered with exactly one SAbilityNotReadyPacket naming its reason (#512), so
    // the player can be told why the cast did not happen. The only silent path is a connection with
    // no character, which has nobody to tell.
    public override void Execute(IWorldConnection connection, CCastAbilityPacket packet)
    {
        var attacker = connection.Character;
        if (attacker is null)
        {
            logger.LogDebug("Dropped CMSG_CAST_ABILITY from a connection with no character");
            return;
        }

        if (attacker.IsDead)
        {
            logger.LogDebug("Dropped CMSG_CAST_ABILITY from dead char");
            Refuse(connection, packet, CastRejectReason.Dead);
            return;
        }

        // GCD check uses packet.AbilityId directly — no ability resolution required.
        // Logically: if the player is still inside the global cooldown window, reject before
        // we even bother validating ability ownership.
        double sinceLastCast = (DateTime.UtcNow - attacker.LastCastStartTime).TotalMilliseconds;
        if (sinceLastCast < combatConfig.GcdMs)
        {
            // Rounded up: a sub-millisecond remainder must not read as 0 ("ready") on the wire.
            uint remaining = (uint)Math.Ceiling(combatConfig.GcdMs - sinceLastCast);
            logger.LogDebug("Cast reject GCD ability={AbilityId} remainingMs={Remaining}", packet.AbilityId, remaining);
            Refuse(connection, packet, CastRejectReason.Gcd, remaining);
            return;
        }

        IAbility? ability = attacker.Spells[packet.AbilityId];
        if (ability is null)
        {
            logger.LogInformation("Cast reject NotOwned ability={AbilityId}", packet.AbilityId);
            Refuse(connection, packet, CastRejectReason.NotOwned);
            return;
        }

        if (ability.CooldownTimer > 0)
        {
            // CooldownTimer is float seconds; the wire field is uint milliseconds, rounded up so a
            // sub-millisecond remainder still carries at least 1.
            uint cooldownMs = (uint)Math.Ceiling(ability.CooldownTimer * 1000.0);
            logger.LogDebug("Cast reject Cooldown ability={AbilityId} remainingMs={Remaining}", packet.AbilityId, cooldownMs);
            Refuse(connection, packet, CastRejectReason.Cooldown, cooldownMs);
            return;
        }

        AbilityMetadata meta = ability.Metadata;

        // Combat-state gating. Some abilities (e.g. mounts, regeneration buffs) only fire
        // out-of-combat; others (e.g. execute-style finishers) only fire in-combat.
        if (meta.Flags.HasFlag(AbilityFlags.RequiresOutOfCombat) && attacker.IsInCombat)
        {
            logger.LogInformation("Cast reject RequiresOutOfCombat ability={AbilityId}", packet.AbilityId);
            Refuse(connection, packet, CastRejectReason.RequiresOutOfCombat);
            return;
        }
        if (meta.Flags.HasFlag(AbilityFlags.RequiresInCombat) && !attacker.IsInCombat)
        {
            logger.LogInformation("Cast reject RequiresInCombat ability={AbilityId}", packet.AbilityId);
            Refuse(connection, packet, CastRejectReason.RequiresInCombat);
            return;
        }

        // Cost check against CurrentPower (the spendable pool). Power deduction itself happens
        // inside InstanceAbilityCastSystem.QueueAbility for the queued path; for the instant
        // path we mirror that here so the resource cost is paid before script execution.
        if (meta.Cost > 0 && (attacker.CurrentPower ?? 0) < meta.Cost)
        {
            logger.LogDebug("Cast reject Cost ability={AbilityId} need={Cost} have={Have}",
                packet.AbilityId, meta.Cost, attacker.CurrentPower ?? 0);
            Refuse(connection, packet, CastRejectReason.NotEnoughPower);
            return;
        }

        // Resolve the live simulation context for the player's current instance.
        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(attacker.InstanceId);
        if (context is null)
        {
            logger.LogWarning("Instance not found for character {CharacterId}", attacker.Guid);
            Refuse(connection, packet, CastRejectReason.InternalError);
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
                logger.LogInformation("Cast reject TargetNotFound ability={AbilityId} target={TargetGuid}",
                    packet.AbilityId, targetGuidRaw);
                Refuse(connection, packet, CastRejectReason.TargetNotFound);
                return;
            }

            // Facing check against CombatConfig.MaxFacingAngleDeg, the cone the client is sent on
            // every AbilityInfo.FacingAngle (#513).
            if (!IsFacingTarget(attacker, target))
            {
                logger.LogInformation("Cast reject Facing ability={AbilityId} caster={Name} target={Target}",
                    packet.AbilityId, attacker.Name, target.Guid);
                Refuse(connection, packet, CastRejectReason.NotFacing);
                return;
            }

            float distance = Vector3.Distance(attacker.Position, target.Position);
            if (distance > (float)meta.Range)
            {
                logger.LogDebug("Cast reject Range ability={AbilityId} dist={Distance} max={Range}",
                    packet.AbilityId, distance, meta.Range);
                Refuse(connection, packet, CastRejectReason.OutOfRange);
                return;
            }
        }

        // Cast dispatch. Cast-time abilities go through the queue (movement-interruptible);
        // instant abilities run inline and immediately enter cooldown.
        if (meta.CastTime > 0)
        {
            // The cost check above already pre-validated, so a false return implies a PowerType
            // mismatch the player cannot fix: answered as InternalError, and no GCD is started.
            if (!context.QueueAbility(attacker, target, ability))
            {
                logger.LogWarning("Cast reject QueueAbility ability={AbilityId} caster={Name}",
                    packet.AbilityId, attacker.Name);
                Refuse(connection, packet, CastRejectReason.InternalError);
                return;
            }

            attacker.MarkCombat();
            context.BroadcastUnitStartCast(attacker, meta.CastTime);
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

    /// <summary>
    /// Answers a refused cast. <paramref name="cooldownMs" /> is the time left, for Gcd and Cooldown
    /// only; every other reason sends 0.
    /// </summary>
    private static void Refuse(IWorldConnection connection, CCastAbilityPacket packet, CastRejectReason reason,
        uint cooldownMs = 0u) =>
        connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, reason, cooldownMs,
            connection.CryptoSession.Encrypt));

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

    /// <summary>
    /// Strictly inside the cone: the angle between the caster's facing and the direction to the
    /// target must be less than <see cref="CombatConfig.MaxFacingAngleDeg" />; exactly equal is refused.
    /// </summary>
    private bool IsFacingTarget(IUnit caster, IUnit target)
    {
        Vector3 direction = target.Position - caster.Position;
        direction.Normalize();

        // Orientation.y is the yaw in degrees (right-handed Y-up).
        float radians = caster.Orientation.y * Mathf.Deg2Rad;
        Vector3 forward = new(Mathf.Sin(radians), 0, Mathf.Cos(radians));

        float angle = Vector3.Angle(forward, direction);
        return angle < combatConfig.MaxFacingAngleDeg;
    }
}
