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

    /// <summary>The total dealt or healed before scaling, split over the ticks.</summary>
    public float PeriodicBase { get; set; }

    public AbilityScalingStat ScalingStat { get; set; }
    public float ScalingCoefficient { get; set; }

    /// <summary>
    /// What one roll of the caster's base damage (a creature's natural range, a character's main hand) adds to the total,
    /// rolled once when the aura is applied; 0 for none.
    /// </summary>
    public float BaseDamageCoefficient { get; set; }

    public AuraStacking Stacking { get; set; }
    public uint MaxStacks { get; set; }

    /// <summary>The aura script, by class name; blank or null for none.</summary>
    public string? ScriptName { get; set; }

    /// <summary>Every stat the aura modifies, at most one entry per stat; replaces the stored list.</summary>
    public List<AuraStatModifierDto> Modifiers { get; set; } = [];
}
