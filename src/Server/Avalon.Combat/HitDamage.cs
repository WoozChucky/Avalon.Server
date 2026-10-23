using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// A resolved damage hit (#1178). <paramref name="Total" /> is the whole hit after mitigation, floored, at least 1, or 0
/// when evaded. <paramref name="Type" /> is the part that dealt the most after mitigation, a tie going to the lower type;
/// <paramref name="Types" /> every part above 0 before mitigation. <paramref name="Absorbable" /> is the share of the
/// total a ward may take: everything but its blight.
/// </summary>
public readonly record struct HitDamage(uint Total, HitResult Result, DamageType Type, DamageTypeMask Types, uint Absorbable);
