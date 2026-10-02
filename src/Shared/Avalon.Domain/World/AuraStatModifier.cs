using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One stat an aura modifies, keyed by (AuraId, Stat).</summary>
public class AuraStatModifier
{
    public AuraId AuraId { get; set; } = null!;
    public AuraStat Stat { get; set; }
    public float Value { get; set; }
    public AuraModifierKind Kind { get; set; }
}
