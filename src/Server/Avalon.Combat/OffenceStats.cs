using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>The storage behind <see cref="OffenceStats" />: one value per <see cref="OffenceStat" />, inline.</summary>
[InlineArray(OffenceStats.Count)]
internal struct OffenceStatValues
{
    private float _element0;
}

/// <summary>
/// What a unit's offence adds to its damage, healing and speeds (#1178): per <see cref="OffenceStat" />, the increased
/// percentage points and the product of every more, plus the crit multiplier bonus and the three speeds as raw totals,
/// before their caps, and the added damage every attack and every spell rolls (#1179). Inline arrays rather than managed
/// ones, so it is a value that never allocates; its equality is written by hand because the ability amounts' flush
/// compares it, inside <see cref="AttackerCombat" />, after every stats refresh.
/// </summary>
public struct OffenceStats : IEquatable<OffenceStats>
{
    public const int Count = 14;

    private OffenceStatValues _increased;

    // The product of every (1 + more / 100) added, minus 1, so a key no more names holds 0 and default is "none".
    private OffenceStatValues _more;

    /// <summary>Percentage points over <see cref="CombatFormula.CritMultiplier" />: 50 makes a 1.5 crit a 2.0 one.</summary>
    public float CritMultiplierBonus { readonly get; set; }

    public float AttackSpeedPct { readonly get; set; }

    public float CastSpeedPct { readonly get; set; }

    /// <summary>The raw total, before <see cref="CombatFormula.CooldownRecoveryCap" />, as the two speeds are.</summary>
    public float CooldownRecoveryPct { readonly get; set; }

    /// <summary>Added damage by type every Attack rolls after its weapon's own (#1179): the sum of every worn item's lines.</summary>
    public DamageRanges AddedToAttacks { readonly get; set; }

    /// <summary>Added damage by type every Spell rolls after its spell base (#1179): the sum of every worn item's lines.</summary>
    public DamageRanges AddedToSpells { readonly get; set; }

    /// <summary>The increased percentage points of <paramref name="stat" />.</summary>
    public readonly float Increased(OffenceStat stat) => _increased[Index(stat)];

    /// <summary>The factor every more on <paramref name="stat" /> multiplies it by: 1 with none.</summary>
    public readonly double More(OffenceStat stat) => 1d + _more[Index(stat)];

    /// <summary>Adds <paramref name="points" /> of increased to <paramref name="stat" />; a value that is not finite adds none.</summary>
    public void AddIncreased(OffenceStat stat, float points)
    {
        int i = Index(stat);
        if (float.IsFinite(points)) _increased[i] += points;
    }

    /// <summary>
    /// Multiplies <paramref name="stat" /> by <c>1 + pct / 100</c>, at most down to nothing; a value that is not finite
    /// multiplies by 1.
    /// </summary>
    public void AddMore(OffenceStat stat, float pct)
    {
        if (float.IsFinite(pct)) MultiplyMore(stat, 1d + pct / 100d);
    }

    /// <summary>Multiplies <paramref name="stat" />'s more product by <paramref name="factor" />, never below 0.</summary>
    internal void MultiplyMore(OffenceStat stat, double factor)
    {
        int i = Index(stat);
        if (double.IsFinite(factor)) _more[i] = (float)(((1d + _more[i]) * Math.Max(0d, factor)) - 1d);
    }

    /// <summary>The offence key a gear stat adds to: one of the thirteen Increased… keys, else null.</summary>
    public static OffenceStat? KeyOf(StatType type) =>
        type is >= StatType.IncreasedDamage and <= StatType.IncreasedHealing
            ? (OffenceStat)(type - StatType.IncreasedDamage)
            : null;

    /// <summary>The offence key an aura stat modifies: one of the thirteen Increased… keys, else null.</summary>
    public static OffenceStat? KeyOf(AuraStat stat) =>
        stat is >= AuraStat.IncreasedDamage and <= AuraStat.IncreasedHealing
            ? (OffenceStat)(stat - AuraStat.IncreasedDamage)
            : null;

    public readonly bool Equals(OffenceStats other)
    {
        if (!CritMultiplierBonus.Equals(other.CritMultiplierBonus) || !AttackSpeedPct.Equals(other.AttackSpeedPct)
            || !CastSpeedPct.Equals(other.CastSpeedPct) || !CooldownRecoveryPct.Equals(other.CooldownRecoveryPct)
            || !AddedToAttacks.Equals(other.AddedToAttacks) || !AddedToSpells.Equals(other.AddedToSpells))
        {
            return false;
        }

        for (int i = 0; i < Count; i++)
        {
            if (!_increased[i].Equals(other._increased[i]) || !_more[i].Equals(other._more[i])) return false;
        }

        return true;
    }

    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is OffenceStats other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CritMultiplierBonus);
        hash.Add(AttackSpeedPct);
        hash.Add(CastSpeedPct);
        hash.Add(CooldownRecoveryPct);
        hash.Add(AddedToAttacks.GetHashCode());
        hash.Add(AddedToSpells.GetHashCode());
        for (int i = 0; i < Count; i++)
        {
            hash.Add(_increased[i]);
            hash.Add(_more[i]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(OffenceStats left, OffenceStats right) => left.Equals(right);

    public static bool operator !=(OffenceStats left, OffenceStats right) => !left.Equals(right);

    private static int Index(OffenceStat stat)
    {
        if ((uint)stat >= Count) ThrowOutOfRange(stat);
        return (int)stat;
    }

    // Kept out of line so the accessors stay small enough to inline (an inline throw boxes the enum argument).
    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOutOfRange(OffenceStat stat) =>
        throw new ArgumentOutOfRangeException(nameof(stat), stat, null);
}
