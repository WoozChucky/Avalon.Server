using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;

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
    /// The flat part of the total a tick-giving aura deals or heals (#1178), before its source's roll joins it and the
    /// whole is scaled; split evenly over its ticks, taken once, when the aura is applied. Finite and 0 or more.
    /// </summary>
    public float PeriodicBase { get; set; }

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
