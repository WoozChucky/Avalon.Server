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
    public string? ScriptName { get; set; }
    public List<AuraStatModifierDto> Modifiers { get; set; } = [];

    /// <summary>The row's version, its modifiers included: a lowercase hex SHA-256, also sent as the ETag. An edit sends it back as If-Match.</summary>
    public string Version { get; set; } = "";

    /// <summary>Whether this world's templates can be edited.</summary>
    public bool Editable { get; set; }
}
