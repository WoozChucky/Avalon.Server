using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// Records every build and Prepare, and finishes in Prepare, like a circle or a cone. Its constructor
/// matches the production call site in InstanceAbilityCastSystem. The statics are shared, so every
/// test class that reads them runs in the non-parallel RecordingAbilityScript collection.
/// </summary>
public sealed class RecordingAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
    : AbilityScript(ability, caster, aim)
{
    public static readonly List<(IUnit Caster, AbilityAim Aim, IAbilityArena Arena)> Prepared = [];
    public static RecordingAbilityScript? LastBuilt;

    public override object State { get; set; } = SpellState.Executing;
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }

    public override void Prepare()
    {
        LastBuilt = this;
        Guid = new ObjectGuid(ObjectType.Spell, IObject.GenerateId());
        Prepared.Add((Caster, Aim, arena));
        State = SpellState.Finished;
    }

    protected override bool ShouldRun() => true;
}
