namespace Avalon.Api.Contract;

/// <summary>The body of an aura template edit (auras): the read shape without its id, version and computed fields; the modifier list replaces the stored one.</summary>
public sealed class UpdateAuraTemplateRequest
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public AuraKind Kind { get; set; }

    /// <summary>In milliseconds.</summary>
    public uint DurationMs { get; set; }

    /// <summary>In milliseconds; 0 for an aura that never ticks.</summary>
    public uint TickIntervalMs { get; set; }

    public AuraPeriodicKind PeriodicKind { get; set; }

    /// <summary>The flat part of the total dealt or healed before scaling, split over the ticks.</summary>
    public float PeriodicBase { get; set; }

    /// <summary>
    /// Percent of its source's base roll added to <see cref="PeriodicBase"/> (#1178): a weapon roll for an Attack, the
    /// spell base for a Spell or a Heal. Finite and 0 or more; 0 adds none.
    /// </summary>
    public float PeriodicEffectiveness { get; set; }

    public AuraStacking Stacking { get; set; }
    public uint MaxStacks { get; set; }

    /// <summary>The states it puts its holder in (#770); only a Harmful aura may grant any.</summary>
    public CrowdControl Control { get; set; }

    /// <summary>Percent, 0 to 100: the chance it ends each time its holder takes damage.</summary>
    public float BreakOnDamagePct { get; set; }

    /// <summary>Percent, 0 to 100: the chance it ends on its own, rolled once for every second it is held.</summary>
    public float BreakPerSecondPct { get; set; }

    /// <summary>The aura script, by class name; blank or null for none.</summary>
    public string? ScriptName { get; set; }

    /// <summary>Every stat the aura modifies, at most one entry per stat; replaces the stored list.</summary>
    public List<AuraStatModifierDto> Modifiers { get; set; } = [];
}
