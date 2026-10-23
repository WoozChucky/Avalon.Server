using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>The damage types as numbers (#1178): how many there are and each one's mask bit.</summary>
public static class DamageTypes
{
    public const int Count = 5;

    public static DamageTypeMask MaskOf(DamageType type) => (DamageTypeMask)(1 << (int)type);
}

/// <summary>The storage behind <see cref="DamageParts" />: one amount per <see cref="DamageType" />, inline.</summary>
[InlineArray(DamageTypes.Count)]
internal struct DamagePartValues
{
    private double _element0;
}

/// <summary>
/// A hit's damage split by type (#1178), one amount per <see cref="DamageType" />, indexed by the type's number. An
/// inline array rather than a managed one, so it is a value that never allocates; each part is mitigated on its own.
/// </summary>
public struct DamageParts
{
    private DamagePartValues _parts;

    /// <summary>A hit of one type.</summary>
    public static DamageParts Of(DamageType type, double amount)
    {
        DamageParts parts = default;
        parts.Set(type, amount);
        return parts;
    }

    public readonly double Get(DamageType type)
    {
        if ((uint)type >= DamageTypes.Count) ThrowOutOfRange(type);
        return _parts[(int)type];
    }

    public void Set(DamageType type, double amount)
    {
        if ((uint)type >= DamageTypes.Count) ThrowOutOfRange(type);
        _parts[(int)type] = amount;
    }

    public readonly double Total
    {
        get
        {
            double sum = 0d;
            for (int i = 0; i < DamageTypes.Count; i++) sum += _parts[i];
            return sum;
        }
    }

    /// <summary>Every type with a part above 0.</summary>
    public readonly DamageTypeMask Mask
    {
        get
        {
            DamageTypeMask mask = DamageTypeMask.None;
            for (int i = 0; i < DamageTypes.Count; i++)
            {
                if (_parts[i] > 0d) mask |= (DamageTypeMask)(1 << i);
            }

            return mask;
        }
    }

    /// <summary>The type with the largest part, a tie going to the lower type; <paramref name="fallback" /> when no part is above 0.</summary>
    public readonly DamageType Primary(DamageType fallback)
    {
        DamageType primary = fallback;
        double largest = 0d;
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            double part = _parts[i];
            if (part > largest)
            {
                largest = part;
                primary = (DamageType)i;
            }
        }

        return primary;
    }

    // Kept out of line so Get and Set stay small enough to inline (an inline throw boxes the enum argument).
    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOutOfRange(DamageType type) =>
        throw new ArgumentOutOfRangeException(nameof(type), type, null);
}
