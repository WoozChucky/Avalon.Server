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

    /// <summary>
    /// Percent of its source's base roll a tick-giving aura adds to <see cref="PeriodicBase" /> (#1178): a weapon roll
    /// for an Attack, the spell base for a Spell or a Heal. Finite and 0 or more; 0 adds none.
    /// </summary>
    public float PeriodicEffectiveness { get; set; }

    public AuraStacking Stacking { get; set; }

    /// <summary>The most stacks a Stack aura reaches; 1 for every other.</summary>
    public uint MaxStacks { get; set; } = 1;

    /// <summary>The states it puts its holder in; None for most auras. Only a Harmful aura may grant any.</summary>
    public CrowdControl Control { get; set; }

    /// <summary>
    /// Percent, 0 to 100: the chance it ends each time its holder takes damage (a hit, or a tick that dealt more than
    /// 0).
    /// </summary>
    public float BreakOnDamagePct { get; set; }

    /// <summary>Percent, 0 to 100: the chance it ends on its own, rolled once for every second it is held.</summary>
    public float BreakPerSecondPct { get; set; }

    /// <summary>The AuraScript run for it, by class name, or null.</summary>
    public string? ScriptName { get; set; }

    /// <summary>Its stat modifiers, at most one per stat, each multiplied by the stacks held.</summary>
    public List<AuraStatModifier> Modifiers { get; set; } = [];
}
