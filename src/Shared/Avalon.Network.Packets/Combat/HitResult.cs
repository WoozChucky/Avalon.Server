namespace Avalon.Network.Packets.Combat;

/// <summary>
/// How a hit went (#506), carried by the damage packets. Flags: a hit can crit and be blocked at once. A
/// dodged hit deals 0 and is never also a crit or a block. Append-only.
/// </summary>
[Flags]
public enum HitResult
{
    None = 0,
    Crit = 1,
    Dodged = 2,
    Blocked = 4,
}
