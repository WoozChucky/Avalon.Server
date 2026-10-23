using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// What a unit's auras add to each stat (#1178): the flat points and the increased percentage, each modifier times the
/// stacks held, and the product of every More, each multiplying once per stack. Immutable once built; rebuilt only when
/// an aura is applied, stacked or removed.
/// </summary>
public sealed class AuraStatTotals
{
    private const int Slots = (int)AuraStat.CooldownRecovery + 1;

    private readonly float[] _flat = new float[Slots];
    private readonly float[] _increased = new float[Slots];

    // The product of every (1 + more / 100) held, minus 1, so a stat no More names holds 0 like the other two.
    private readonly double[] _more = new double[Slots];

    public static readonly AuraStatTotals Empty = new();

    private AuraStatTotals()
    {
    }

    /// <summary>No aura modifies anything: every fold returns what it was given.</summary>
    public bool IsEmpty { get; private set; } = true;

    public float Flat(AuraStat stat) => Index(stat) is { } i ? _flat[i] : 0f;

    public float Increased(AuraStat stat) => Index(stat) is { } i ? _increased[i] : 0f;

    /// <summary>The factor every More on <paramref name="stat" /> multiplies it by: 1 with none.</summary>
    public double More(AuraStat stat) => Index(stat) is { } i ? 1d + _more[i] : 1d;

    /// <summary>
    /// The totals of the auras a unit holds, in the list's order, each read through <paramref name="read" /> as its
    /// modifiers and the stacks held. The world server rebuilds a unit's totals whenever one of its auras changes, so
    /// the totals themselves are all this allocates when <paramref name="read" /> captures nothing: as a sequence of
    /// pairs built by a query, it cost an iterator, and an enumerator for each aura's modifiers, besides (#883).
    /// </summary>
    public static AuraStatTotals Of<TAura>(IReadOnlyList<TAura> auras,
        Func<TAura, (IReadOnlyList<AuraStatModifier> Modifiers, uint Stacks)> read)
    {
        var totals = new AuraStatTotals();
        for (int i = 0; i < auras.Count; i++)
        {
            (IReadOnlyList<AuraStatModifier> modifiers, uint stacks) = read(auras[i]);
            totals.Add(modifiers, stacks);
        }

        return totals;
    }

    private void Add(IReadOnlyList<AuraStatModifier> modifiers, uint stacks)
    {
        for (int n = 0; n < modifiers.Count; n++)
        {
            AuraStatModifier m = modifiers[n];

            // The catalog refuses a bad row; a bad value that slips through counts as nothing.
            if (Index(m.Stat) is not { } i || !float.IsFinite(m.Value))
                continue;

            uint held = Math.Max(1u, stacks);
            if (m.Kind == AuraModifierKind.Increased) _increased[i] += m.Value * held;
            else if (m.Kind == AuraModifierKind.Flat) _flat[i] += m.Value * held;
            else if (m.Kind == AuraModifierKind.More)
                _more[i] = ((1d + _more[i]) * Math.Pow(Math.Max(0d, 1d + m.Value / 100d), held)) - 1d;
            else continue;

            IsEmpty = false;
        }
    }

    private static int? Index(AuraStat stat) => (int)stat is >= 1 and < Slots ? (int)stat : null;
}
