namespace Avalon.World.Configuration;

/// <summary>
/// Game:QuestResets (#710): when daily, weekly and monthly quests come back. Every moment is UTC and the same for every
/// character; the world reads it once at startup, and an invalid value stops the start (<see cref="QuestResetValidation" />).
/// </summary>
public sealed class QuestResetConfiguration
{
    /// <summary>The time of day, UTC, at which every reset happens: at least 00:00:00 and below 1.00:00:00.</summary>
    public TimeSpan DailyResetTime { get; set; } = TimeSpan.FromHours(4);

    /// <summary>The day of the week on which weekly quests come back, at <see cref="DailyResetTime" />.</summary>
    public DayOfWeek WeeklyResetDay { get; set; } = DayOfWeek.Monday;

    /// <summary>The day of the month (1-28, so every month has it) on which monthly quests come back, at <see cref="DailyResetTime" />.</summary>
    public int MonthlyResetDay { get; set; } = 1;

    /// <summary>Null when every setting is in range, else a message naming the first one that is not.</summary>
    public string? Problem()
    {
        if (DailyResetTime < TimeSpan.Zero || DailyResetTime >= TimeSpan.FromDays(1))
            return $"Game:QuestResets:DailyResetTime must be at least 00:00:00 and below 1.00:00:00 (was {DailyResetTime})";

        if (!Enum.IsDefined(WeeklyResetDay))
            return $"Game:QuestResets:WeeklyResetDay must be a day of the week (was {(int)WeeklyResetDay})";

        if (MonthlyResetDay is < 1 or > 28)
            return $"Game:QuestResets:MonthlyResetDay must be 1-28 so every month has the day (was {MonthlyResetDay})";

        return null;
    }
}
