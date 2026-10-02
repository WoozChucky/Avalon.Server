using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// What a unit's auras add to each stat: the flat points and the percentage, each modifier times the stacks held.
/// Immutable once built; rebuilt only when an aura is applied, stacked or removed.
/// </summary>
public sealed class AuraStatTotals
{
    private const int Slots = (int)AuraStat.MaxPower + 1;

    private readonly float[] _flat = new float[Slots];
    private readonly float[] _percent = new float[Slots];

    public static readonly AuraStatTotals Empty = new();

    private AuraStatTotals()
    {
    }

    /// <summary>No aura modifies anything: every fold returns what it was given.</summary>
    public bool IsEmpty { get; private set; } = true;

    public float Flat(AuraStat stat) => Index(stat) is { } i ? _flat[i] : 0f;

    public float Percent(AuraStat stat) => Index(stat) is { } i ? _percent[i] : 0f;

    public static AuraStatTotals Of(IEnumerable<(IReadOnlyList<AuraStatModifier> Modifiers, uint Stacks)> auras)
    {
        var totals = new AuraStatTotals();
        foreach ((IReadOnlyList<AuraStatModifier> modifiers, uint stacks) in auras)
        {
            foreach (AuraStatModifier m in modifiers)
            {
                // The catalog refuses a bad row; a bad value that slips through counts as nothing.
                if (Index(m.Stat) is not { } i || !float.IsFinite(m.Value))
                    continue;

                float value = m.Value * Math.Max(1u, stacks);
                if (m.Kind == AuraModifierKind.Percent) totals._percent[i] += value;
                else if (m.Kind == AuraModifierKind.Flat) totals._flat[i] += value;
                else continue;

                totals.IsEmpty = false;
            }
        }

        return totals;
    }

    private static int? Index(AuraStat stat) => (int)stat is >= 1 and < Slots ? (int)stat : null;
}
