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
/// A cone from the caster along its facing (Movement) or toward the aim point (Cursor, falling back to
/// facing when the point is on the caster) (#164). Resolves once, when it fires. Walls do not clip it.
/// The caster's position is read when it fires, never when the script is built.
/// </summary>
public sealed class ConeAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
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
        AbilityMetadata meta = Ability.Metadata;
        Vector3 origin = Caster.Position;
        Position = origin;

        Vector3 direction = meta.AimMode == AbilityAimMode.Movement ? Aim.Facing : Aim.DirectionFrom(origin);

        arena.BroadcastAbilityFired(Caster, Ability, origin, direction, centre: null);
        AbilityEffect.ApplyToAll(arena, Caster, Ability, arena.Hits.InCone(origin, direction, meta.Reach, meta.ArcDegrees));

        State = SpellState.Finished;
    }
}
