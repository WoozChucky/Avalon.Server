using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>The damage types as numbers (#1178): how many there are, each one's mask bit, which are elements.</summary>
public static class DamageTypes
{
    public const int Count = 5;

    public static DamageTypeMask MaskOf(DamageType type) => (DamageTypeMask)(1 << (int)type);

    /// <summary>Fire, cold and lightning, and blight: anything but physical.</summary>
    public static bool IsElemental(DamageType type) => type != DamageType.Physical;
}

/// <summary>
/// A hit's damage split by type (#1178), one amount per <see cref="DamageType" />. Five plain fields rather than
/// an array, so it is a value that never allocates; each part is mitigated on its own.
/// </summary>
public struct DamageParts
{
    private double _physical;
    private double _fire;
    private double _cold;
    private double _lightning;
    private double _blight;

    /// <summary>A hit of one type.</summary>
    public static DamageParts Of(DamageType type, double amount)
    {
        DamageParts parts = default;
        parts.Set(type, amount);
        return parts;
    }

    public readonly double Get(DamageType type) => type switch
    {
        DamageType.Physical => _physical,
        DamageType.Fire => _fire,
        DamageType.Cold => _cold,
        DamageType.Lightning => _lightning,
        DamageType.Blight => _blight,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public void Set(DamageType type, double amount)
    {
        switch (type)
        {
            case DamageType.Physical: _physical = amount; break;
            case DamageType.Fire: _fire = amount; break;
            case DamageType.Cold: _cold = amount; break;
            case DamageType.Lightning: _lightning = amount; break;
            case DamageType.Blight: _blight = amount; break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    public readonly double Total => _physical + _fire + _cold + _lightning + _blight;

    /// <summary>Every type with a part above 0.</summary>
    public readonly DamageTypeMask Mask =>
        (_physical > 0d ? DamageTypeMask.Physical : DamageTypeMask.None)
        | (_fire > 0d ? DamageTypeMask.Fire : DamageTypeMask.None)
        | (_cold > 0d ? DamageTypeMask.Cold : DamageTypeMask.None)
        | (_lightning > 0d ? DamageTypeMask.Lightning : DamageTypeMask.None)
        | (_blight > 0d ? DamageTypeMask.Blight : DamageTypeMask.None);

    /// <summary>The type with the largest part, a tie going to the lower type; <paramref name="fallback" /> when no part is above 0.</summary>
    public readonly DamageType Primary(DamageType fallback)
    {
        DamageType primary = fallback;
        double largest = 0d;
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            double part = Get((DamageType)i);
            if (part > largest)
            {
                largest = part;
                primary = (DamageType)i;
            }
        }

        return primary;
    }
}
