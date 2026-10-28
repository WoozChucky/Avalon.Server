using Avalon.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Abilities;

/// <summary>
/// An ability the talent tree granted (#1181), tagged with the ability catalog generation it was built from. World-side
/// only, never on <see cref="Public.Abilities.IAbility" />: a rebuild parks a leaving ability, and takes one back, only
/// when it was built from the generation the rebuild reads
/// (<see cref="CharacterAbilityContainer.FinishRebuild" />), so a re-allocation never undoes a reload or a live edit.
/// </summary>
/// <remarks>
/// A subclass, not a member of <see cref="GameAbility" />: a cast's copy (<see cref="GameAbility.CloneForCast" />) is a
/// plain <see cref="GameAbility" />, so a cast allocates no more than before.
/// <para>
/// Part D: the capstones a character holds shape it (<see cref="Reshape" />). Its metadata is its unshaped
/// <see cref="Base" /> or a shaped copy of it, and it keeps the last two shaped copies it used, so taking and refunding a
/// capstone, or switching a fork's sides, allocates nothing once warm.
/// </para>
/// </remarks>
internal sealed class TreeAbility : GameAbility
{
    /// <summary>
    /// The shaped copies kept: the most shapes besides its own the talent catalog lets the capstones on one ability give.
    /// </summary>
    internal const int KeptShapes = 2;

    // The last two shaped copies, the one used last first, each with the shaping that built it.
    private AbilityMetadata? _newer, _older;
    private AbilityShaping _newerShaping, _olderShaping;

    public required AbilityCatalog BuiltFrom { get; init; }

    /// <summary>The template's metadata, unshaped, from <see cref="BuiltFrom" />: every shape is built from it.</summary>
    public required AbilityMetadata Base { get; init; }

    /// <summary>The modifiers its metadata is shaped by; none until a capstone shapes it.</summary>
    public AbilityShaping Shaping { get; private set; }

    /// <summary>
    /// Shapes the ability by <paramref name="shaping" />: its metadata becomes <see cref="Base" /> when there is none,
    /// else one of the last two shaped copies built with that shaping, else a new one, which drops the older of the two.
    /// </summary>
    /// <returns>True when the metadata changed.</returns>
    internal bool Reshape(in AbilityShaping shaping)
    {
        AbilityMetadata next;
        if (shaping.IsNone)
        {
            next = Base;
        }
        else if (_newer is not null && _newerShaping == shaping)
        {
            next = _newer;
        }
        else if (_older is not null && _olderShaping == shaping)
        {
            // Used last now: the other is the one a third shape drops.
            next = _older;
            (_older, _newer) = (_newer, _older);
            (_olderShaping, _newerShaping) = (_newerShaping, _olderShaping);
        }
        else
        {
            next = shaping.Apply(Base);
            (_older, _olderShaping) = (_newer, _newerShaping);
            (_newer, _newerShaping) = (next, shaping);
        }

        Shaping = shaping;
        if (ReferenceEquals(next, Metadata))
            return false;

        SetMetadata(next);
        return true;
    }
}
