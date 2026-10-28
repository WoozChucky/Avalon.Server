using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Scripts.Abilities;

/// <summary>
/// A cone from the caster along the aim's facing (Movement: toward the cursor when the cast sent one, else the
/// caster's yaw, resolved at cast start, #716) or toward the aim point (Cursor, falling back to facing when the
/// point is on the caster) (#164). Resolves once, when it fires. Walls do not clip it.
/// It resolves from where the caster stood when the cast started (#648), through
/// <c>AbilityFootprint.Resolve</c>, the footprint the cast's start broadcast carried. Pooled per instance
/// (<see cref="AreaAbilityScript" />, #1238).
/// </summary>
public sealed class ConeAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
    : AreaAbilityScript(ability, caster, aim, arena)
{
    private protected override void Resolve()
    {
        IAbilityArena arena = Arena;
        Vector3 origin = Origin;
        AbilityFootprint footprint = AbilityFootprint.Resolve(AbilityShape.Cone, Ability.Metadata, Aim, origin,
            arena.GetNavigatorForPosition(origin))!.Value;
        Position = origin;

        arena.BroadcastAbilityFired(Caster, Ability, footprint);
        AbilityEffect.ApplyToAll(arena, Caster, Ability,
            arena.Hits.InCone(origin, footprint.Direction!.Value, footprint.Reach, footprint.ArcDegrees),
            Ability.Metadata.MaxTargets);
    }
}
