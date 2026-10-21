namespace Avalon.World.Quests;

/// <summary>
/// Whether, and when, a character may do a quest again once it has turned it in (#710). World-side only, never on the
/// wire. Daily, weekly and monthly quests come back at the fixed UTC boundaries of <see cref="QuestResetSchedule" />.
/// </summary>
public enum QuestRepeat : byte
{
    /// <summary>Done once, never offered again.</summary>
    Once = 0,

    /// <summary>Offered again as soon as it is turned in.</summary>
    Repeatable = 1,

    /// <summary>Offered again after the next daily reset.</summary>
    Daily = 2,

    /// <summary>Offered again after the next weekly reset.</summary>
    Weekly = 3,

    /// <summary>Offered again after the next monthly reset.</summary>
    Monthly = 4,
}
