using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>An ability script whose constructor always throws: a script type that exists but cannot be built.</summary>
public sealed class ThrowingAbilityScript : AbilityScript
{
    public ThrowingAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
        : base(ability, caster, aim) =>
        throw new InvalidOperationException("This script cannot be built.");

    public override object State { get; set; } = null!;
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    public override void Prepare() { }
    protected override bool ShouldRun() => true;
}
