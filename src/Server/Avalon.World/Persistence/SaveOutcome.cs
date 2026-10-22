namespace Avalon.World.Persistence;

/// <summary>How a trade's paired save ended (#1175).</summary>
public enum SaveOutcome : byte
{
    /// <summary>The transaction committed.</summary>
    Committed,

    /// <summary>Definitely not committed: the write failed before its commit and rolled back.</summary>
    Refused,

    /// <summary>
    /// The commit itself failed, or succeeded with its acknowledgement lost: the write may have committed. The trade
    /// record settles it (<see cref="ICharacterSaver.TradeCommittedAsync" />).
    /// </summary>
    Unknown,
}
