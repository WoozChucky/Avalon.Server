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
/// </remarks>
internal sealed class TreeAbility : GameAbility
{
    public required AbilityCatalog BuiltFrom { get; init; }
}
