using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Scripts.Abilities;

/// <summary>
/// A circle on the caster, or on the aim point clamped to Reach and pulled back from any wall between
/// (#164). Resolves once, when it fires: every qualifying unit whose body overlaps the radius, nearest
/// the centre first. It resolves from where the caster stood when the cast started (#648), through
/// <c>AbilityFootprint.Resolve</c>, the footprint the cast's start broadcast carried.
/// </summary>
public sealed class CircleAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
    : AbilityScript(ability, caster, aim)
{
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    public override object State { get; set; } = SpellState.Executing;

    protected override bool ShouldRun() => true;

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.Spell, IObject.GenerateId());
        Vector3 origin = Origin;
        AbilityFootprint footprint = AbilityFootprint.Resolve(AbilityShape.Circle, Ability.Metadata, Aim, origin,
            arena.GetNavigatorForPosition(origin))!.Value;
        Vector3 centre = footprint.Centre!.Value;
        Position = centre;

        arena.BroadcastAbilityFired(Caster, Ability, footprint);
        AbilityEffect.ApplyToAll(arena, Caster, Ability, arena.Hits.InCircle(centre, footprint.Radius));

        State = SpellState.Finished;
    }
}
