namespace Avalon.Network.Packets.Auras;

/// <summary>The answer to a cancel (auras). Append-only; Unknown is never sent.</summary>
public enum AuraCancelResult : byte
{
    Unknown = 0,

    /// <summary>Every copy of the aura on the canceller ended.</summary>
    Ok = 1,

    /// <summary>The canceller holds no such aura.</summary>
    NotFound = 2,

    /// <summary>It is harmful: only helpful auras can be cancelled.</summary>
    NotCancellable = 3,

    Dead = 4,
}
