using Avalon.Network.Packets.Combat;

namespace Avalon.World.Combat;

/// <summary>
/// How a hit or an aura's tick went (#1178), as the instance reports it: its <see cref="Result" />, its
/// <see cref="Type" /> (the part that dealt the most), <see cref="Types" /> (every part above 0 before mitigation) and
/// what the target's ward took of it. A heal carries no type. World-side, passed <c>in</c>.
/// </summary>
public readonly record struct HitReport(HitResult Result, DamageType Type, DamageTypeMask Types, uint WardAbsorbed)
{
    /// <summary>Outside a hit.</summary>
    public static HitReport None => default;
}
