using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.World;
using Avalon.World.Abilities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// A cast aims at a direction or a ground point, never at a unit (#164): the ability's shape decides
/// who it affects. <c>TargetGuid</c> is ignored, and there is no range or facing check. Every refusal
/// is answered with exactly one SAbilityNotReadyPacket naming its reason (#512); the only silent path
/// is a connection with no character.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_CAST_ABILITY)]
public class CastAbilityHandler(ILogger<CastAbilityHandler> logger, IWorld world, CombatConfig combatConfig)
    : WorldPacketHandler<CCastAbilityPacket>
{
    public override void Execute(IWorldConnection connection, CCastAbilityPacket packet)
    {
        var caster = connection.Character;
        if (caster is null)
        {
            logger.LogDebug("Dropped CMSG_CAST_ABILITY from a connection with no character");
            return;
        }

        if (caster.IsDead)
        {
            Refuse(connection, packet, CastRejectReason.Dead);
            return;
        }

        // #521 item 4: one cast at a time.
        if (caster.Spells.IsCasting)
        {
            Refuse(connection, packet, CastRejectReason.AlreadyCasting);
            return;
        }

        double sinceLastCast = (DateTime.UtcNow - caster.LastCastStartTime).TotalMilliseconds;
        if (sinceLastCast < combatConfig.GcdMs)
        {
            // Rounded up: a sub-millisecond remainder must not read as 0 ("ready") on the wire.
            Refuse(connection, packet, CastRejectReason.Gcd, (uint)Math.Ceiling(combatConfig.GcdMs - sinceLastCast));
            return;
        }

        IAbility? ability = caster.Spells[packet.AbilityId];
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
            Refuse(connection, packet, CastRejectReason.Cooldown, (uint)Math.Ceiling(ability.CooldownTimer * 1000.0));
            return;
        }

        AbilityMetadata meta = ability.Metadata;

        if (meta.Flags.HasFlag(AbilityFlags.RequiresOutOfCombat) && caster.IsInCombat)
        {
            Refuse(connection, packet, CastRejectReason.RequiresOutOfCombat);
            return;
        }

        if (meta.Flags.HasFlag(AbilityFlags.RequiresInCombat) && !caster.IsInCombat)
        {
            Refuse(connection, packet, CastRejectReason.RequiresInCombat);
            return;
        }

        // Captured now, at cast start, for both paths: a queued cast fires with the aim it started with.
        Vector3 facing = AbilityAim.FacingFromYaw(caster.Orientation.y);
        Vector3? point = null;
        if (meta.AimMode == AbilityAimMode.Cursor)
        {
            if (!TryReadAimPoint(packet.GroundPos, out Vector3 groundPoint))
            {
                logger.LogDebug("Cast reject NoAimPoint ability={AbilityId}", packet.AbilityId);
                Refuse(connection, packet, CastRejectReason.NoAimPoint);
                return;
            }

            point = groundPoint;
        }

        var aim = new AbilityAim(facing, point);

        switch (AbilityCost.Check(caster, meta))
        {
            case CostCheck.NotEnoughPower:
                Refuse(connection, packet, CastRejectReason.NotEnoughPower);
                return;
            case CostCheck.WrongPowerType:
                logger.LogWarning("Cast reject WrongPowerType ability={AbilityId} powerType={PowerType}",
                    packet.AbilityId, caster.PowerType);
                Refuse(connection, packet, CastRejectReason.InternalError);
                return;
        }

        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(caster.InstanceId);
        if (context is null)
        {
            logger.LogWarning("Instance not found for character {CharacterId}", caster.Guid);
            Refuse(connection, packet, CastRejectReason.InternalError);
            return;
        }

        bool accepted = meta.CastTime > 0
            ? context.QueueAbility(caster, aim, ability)
            : context.RunInstantAbility(caster, aim, ability);

        if (!accepted)
        {
            // The cost was checked above, so this is a script that is missing or cannot be built:
            // nothing the player can fix. No global cooldown is started.
            logger.LogWarning("Cast reject by the cast system ability={AbilityId} caster={Name}", packet.AbilityId, caster.Name);
            Refuse(connection, packet, CastRejectReason.InternalError);
            return;
        }

        caster.MarkCombat();
        if (meta.CastTime > 0)
        {
            context.BroadcastUnitStartCast(caster, ability);
        }

        // GCD anchor: stamps the start of this cast for the next GCD calculation.
        caster.LastCastStartTime = DateTime.UtcNow;
    }

    /// <summary>A present point whose three components are finite. Its height is kept but no shape reads it.</summary>
    private static bool TryReadAimPoint(Vector3Dto? dto, out Vector3 point)
    {
        point = default;
        if (dto is null || !float.IsFinite(dto.X) || !float.IsFinite(dto.Y) || !float.IsFinite(dto.Z))
        {
            return false;
        }

        point = new Vector3(dto.X, dto.Y, dto.Z);
        return true;
    }

    /// <summary>
    /// Answers a refused cast. <paramref name="cooldownMs" /> is the time left, for Gcd and Cooldown
    /// only; every other reason sends 0.
    /// </summary>
    private static void Refuse(IWorldConnection connection, CCastAbilityPacket packet, CastRejectReason reason,
        uint cooldownMs = 0u) =>
        connection.Send(SAbilityNotReadyPacket.Create(packet.AbilityId, reason, cooldownMs,
            connection.CryptoSession.Encrypt));
}
