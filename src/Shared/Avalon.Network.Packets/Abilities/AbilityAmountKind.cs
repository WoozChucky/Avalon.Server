namespace Avalon.Network.Packets.Abilities;

/// <summary>
/// What an ability's advertised amount is (#669). Append-only. <see cref="None" /> means the ability has no
/// direct per-hit amount the server can state, not an amount of 0: a client shows no damage or healing line.
/// </summary>
public enum AbilityAmountKind : byte
{
    None = 0,

    /// <summary>Damage dealt to each hostile unit hit.</summary>
    Damage = 1,

    /// <summary>Health restored to each ally healed.</summary>
    Healing = 2,
}
