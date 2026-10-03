namespace Avalon.Network.Packets.Auras;

/// <summary>The answer to a cancel (auras). Append-only; Unknown is never sent.</summary>
public enum AuraCancelResult : byte
{
    Unknown = 0,

    /// <summary>The named copy ended, or, when the cancel named no copy, every copy of the aura on the canceller.</summary>
    Ok = 1,

    /// <summary>
    /// The canceller holds no copy of the aura with that id, and, when the cancel named a copy, with that key.
    /// </summary>
    NotFound = 2,

    /// <summary>It is harmful: only helpful auras can be cancelled.</summary>
    NotCancellable = 3,

    /// <summary>The canceller is dead: nothing ended.</summary>
    Dead = 4,
}
