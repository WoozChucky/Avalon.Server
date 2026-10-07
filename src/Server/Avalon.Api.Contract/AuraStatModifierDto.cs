namespace Avalon.Api.Contract;

/// <summary>One stat an aura modifies (auras): Flat adds the value, Percent scales by (1 + value / 100), times the stacks.</summary>
public sealed class AuraStatModifierDto
{
    public AuraStat Stat { get; set; }
    public float Value { get; set; }
    public AuraModifierKind Kind { get; set; }
}
