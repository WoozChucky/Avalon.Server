using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// A unit's resistance to each element (#1178), in percent: positive takes that share of the element away, negative
/// adds to it. The cap, the ceiling and the floor are applied where a hit meets them (<see cref="HitResolver" />).
/// Physical damage meets armour instead, so it has none here.
/// </summary>
public readonly record struct Resistances(float Fire, float Cold, float Lightning, float Blight)
{
    /// <summary>The resistance to <paramref name="type" />; 0 for physical.</summary>
    public float Of(DamageType type) => type switch
    {
        DamageType.Fire => Fire,
        DamageType.Cold => Cold,
        DamageType.Lightning => Lightning,
        DamageType.Blight => Blight,
        _ => 0f,
    };
}

/// <summary>
/// What an attacker takes off each of its target's resistances (#1178), in percentage points, after the target's cap.
/// No data source sets it yet: the hook a later stat fills.
/// </summary>
public readonly record struct Penetration(float Fire, float Cold, float Lightning, float Blight)
{
    /// <summary>The penetration of <paramref name="type" />; 0 for physical.</summary>
    public float Of(DamageType type) => type switch
    {
        DamageType.Fire => Fire,
        DamageType.Cold => Cold,
        DamageType.Lightning => Lightning,
        DamageType.Blight => Blight,
        _ => 0f,
    };
}
