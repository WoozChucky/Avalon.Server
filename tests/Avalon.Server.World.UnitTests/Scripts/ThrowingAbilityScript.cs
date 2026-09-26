using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
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

/// <summary>An ability script that builds, and whose Prepare throws (#530).</summary>
public sealed class PrepareThrowingAbilityScript : AbilityScript
{
    public PrepareThrowingAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
        : base(ability, caster, aim)
    {
    }

    public override object State { get; set; } = SpellState.Executing;
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    public override void Prepare() => throw new InvalidOperationException("This script cannot be prepared.");
    protected override bool ShouldRun() => true;
}

/// <summary>
/// A projectile-typed world object that stands still and throws on its <see cref="ThrowOnUpdate" />th
/// Update (#530): late enough for a state broadcast to have shown it, so its removal can be seen.
/// </summary>
public sealed class UpdateThrowingAbilityScript : AbilityScript
{
    public const int ThrowOnUpdate = 10;

    public UpdateThrowingAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
        : base(ability, caster, aim)
    {
    }

    private int _updates;

    public override object State { get; set; } = SpellState.Executing;
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }

    /// <summary>How many times Update has run, the throwing one included.</summary>
    public int Updates => _updates;

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());
        Position = Caster.Position;
        _dirtyFields |= GameEntityFields.Position;
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (++_updates >= ThrowOnUpdate)
        {
            throw new InvalidOperationException("This script cannot be updated.");
        }
    }

    protected override bool ShouldRun() => true;
}
