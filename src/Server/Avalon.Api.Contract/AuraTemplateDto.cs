namespace Avalon.Api.Contract;

/// <summary>An aura template (auras): a timed effect abilities, items and scripts put on units.</summary>
public sealed class AuraTemplateDto
{
    public uint Id { get; set; }
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

    public string? ScriptName { get; set; }
    public List<AuraStatModifierDto> Modifiers { get; set; } = [];

    /// <summary>The row's version, its modifiers included: a lowercase hex SHA-256, also sent as the ETag. An edit sends it back as If-Match.</summary>
    public string Version { get; set; } = "";

    /// <summary>Whether this world's templates can be edited.</summary>
    public bool Editable { get; set; }
}
