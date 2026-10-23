using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>The storage behind <see cref="DamageRanges" />: one inclusive range per <see cref="DamageType" />, inline.</summary>
[InlineArray(DamageTypes.Count)]
internal struct DamageRangeValues
{
    private (uint Min, uint Max) _element0;
}

/// <summary>
/// Added damage by type (#1179): one inclusive range per <see cref="DamageType" />, indexed by the type's number, each
/// rolled on its own. A weapon's local added damage, or every worn item's added damage to attacks or to spells. An
/// inline array rather than a managed one, so it is a value that never allocates; its equality is written by hand because
/// the offence block it sits in is compared after every stats refresh. Default adds nothing.
/// </summary>
public struct DamageRanges : IEquatable<DamageRanges>
{
    private DamageRangeValues _ranges;

    public readonly uint Min(DamageType type) => _ranges[Index(type)].Min;

    public readonly uint Max(DamageType type) => _ranges[Index(type)].Max;

    /// <summary>
    /// Adds <paramref name="min" />..<paramref name="max" /> to <paramref name="type" />'s range, each end held at
    /// <see cref="uint.MaxValue" />; a low end above the high end counts as the high end.
    /// </summary>
    public void Add(DamageType type, uint min, uint max)
    {
        int i = Index(type);
        (uint Min, uint Max) range = _ranges[i];
        _ranges[i] = (Sum(range.Min, Math.Min(min, max)), Sum(range.Max, max));
    }

    /// <summary>An item's "adds X to Y" line to <paramref name="type" />, each end below 0 counting as 0.</summary>
    internal void Add(DamageType type, in ItemLine line) =>
        Add(type, (uint)Math.Max(0, line.Min), (uint)Math.Max(0, line.Max));

    /// <summary>Both ends of <paramref name="type" />'s range increased by <paramref name="increasedPct" /> (<see cref="LocalScaling" />).</summary>
    internal void Increase(DamageType type, long increasedPct)
    {
        int i = Index(type);
        (uint Min, uint Max) range = _ranges[i];
        _ranges[i] = (LocalScaling.Increase(range.Min, increasedPct), LocalScaling.Increase(range.Max, increasedPct));
    }

    /// <summary>True when no type adds anything.</summary>
    public readonly bool IsEmpty
    {
        get
        {
            for (int i = 0; i < DamageTypes.Count; i++)
            {
                if (_ranges[i].Max > 0) return false;
            }

            return true;
        }
    }

    public readonly bool Equals(DamageRanges other)
    {
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            if (_ranges[i].Min != other._ranges[i].Min || _ranges[i].Max != other._ranges[i].Max) return false;
        }

        return true;
    }

    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is DamageRanges other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            hash.Add(_ranges[i].Min);
            hash.Add(_ranges[i].Max);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(DamageRanges left, DamageRanges right) => left.Equals(right);

    public static bool operator !=(DamageRanges left, DamageRanges right) => !left.Equals(right);

    private static uint Sum(uint a, uint b) => (uint)Math.Min((ulong)a + b, uint.MaxValue);

    private static int Index(DamageType type)
    {
        if ((uint)type >= DamageTypes.Count) ThrowOutOfRange(type);
        return (int)type;
    }

    // Kept out of line so the accessors stay small enough to inline (an inline throw boxes the enum argument).
    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOutOfRange(DamageType type) =>
        throw new ArgumentOutOfRangeException(nameof(type), type, null);
}
