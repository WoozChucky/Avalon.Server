using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;

namespace Avalon.Domain.World;

/// <summary>
/// A timed effect on a unit: periodic damage or healing, stat modifiers, or both, with an optional script. Reference
/// data; the world's AuraCatalog validates it on load and on /reload auras.
/// </summary>
public class AuraTemplate : IDbEntity<AuraId>
{
    [Key]
    public AuraId Id { get; set; } = null!;

    public string Name { get; set; } = "";

    /// <summary>The client's icon key.</summary>
    public string Icon { get; set; } = "";

    public AuraKind Kind { get; set; }

    /// <summary>Milliseconds, above 0.</summary>
    public uint DurationMs { get; set; }

    /// <summary>Milliseconds between ticks, at most the duration; 0 for an aura that never ticks.</summary>
    public uint TickIntervalMs { get; set; }

    public AuraPeriodicKind PeriodicKind { get; set; }

    /// <summary>
    /// The total a tick-giving aura deals or heals before scaling, split evenly over its ticks: with ScalingCoefficient
    /// times the caster's ScalingStat and BaseDamageCoefficient times a roll of its base damage, taken once, when the
    /// aura is applied.
    /// </summary>
    public float PeriodicBase { get; set; }

    public ScalingStat ScalingStat { get; set; }

    public float ScalingCoefficient { get; set; }

    /// <summary>
    /// What one roll of the caster's base damage adds to PeriodicBase, as on abilities: a creature's natural
    /// DamageMin..DamageMax, a character's main-hand range, none without a weapon. Rolled once, when the aura is
    /// applied; 0 draws no roll.
    /// </summary>
    public float BaseDamageCoefficient { get; set; }

    public AuraStacking Stacking { get; set; }

    /// <summary>The most stacks a Stack aura reaches; 1 for every other.</summary>
    public uint MaxStacks { get; set; } = 1;

    /// <summary>The AuraScript run for it, by class name, or null.</summary>
    public string? ScriptName { get; set; }

    /// <summary>Its stat modifiers, at most one per stat, each multiplied by the stacks held.</summary>
    public List<AuraStatModifier> Modifiers { get; set; } = [];
}
