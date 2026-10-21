using Avalon.World.Configuration;

namespace Avalon.World.Quests;

/// <summary>
/// The fixed UTC reset boundaries of daily, weekly and monthly quests (#710). A daily boundary is every day at
/// <see cref="QuestResetConfiguration.DailyResetTime" />; a weekly one is the daily boundary on
/// <see cref="QuestResetConfiguration.WeeklyResetDay" />, and a monthly one the daily boundary on
/// <see cref="QuestResetConfiguration.MonthlyResetDay" />. All arithmetic is on UTC, never local time.
/// </summary>
public static class QuestResetSchedule
{
    /// <summary>The latest boundary of <paramref name="repeat" /> at or before <paramref name="utcNow" />.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeat" /> has no boundary (Once or Repeatable).</exception>
    public static DateTime LastBoundary(QuestRepeat repeat, DateTime utcNow, QuestResetConfiguration options)
    {
        DateTime today = utcNow.Date + options.DailyResetTime;
        DateTime daily = utcNow >= today ? today : today.AddDays(-1);  // the latest reset-time moment <= now
        switch (repeat)
        {
            case QuestRepeat.Daily:
                return Utc(daily);
            case QuestRepeat.Weekly:
                int back = ((int)daily.DayOfWeek - (int)options.WeeklyResetDay + 7) % 7;
                return Utc(daily.AddDays(-back));
            case QuestRepeat.Monthly:
                DateTime monthDay = new(daily.Year, daily.Month, options.MonthlyResetDay);
                DateTime month = monthDay + options.DailyResetTime;
                return Utc(month <= daily ? month : monthDay.AddMonths(-1) + options.DailyResetTime);
            default:
                throw new ArgumentOutOfRangeException(nameof(repeat), repeat, "Only daily, weekly and monthly quests reset.");
        }
    }

    /// <summary>The earliest boundary of <paramref name="repeat" /> after <paramref name="utcNow" />.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeat" /> has no boundary (Once or Repeatable).</exception>
    public static DateTime NextBoundary(QuestRepeat repeat, DateTime utcNow, QuestResetConfiguration options)
    {
        DateTime last = LastBoundary(repeat, utcNow, options);
        return repeat switch
        {
            QuestRepeat.Daily => last.AddDays(1),
            QuestRepeat.Weekly => last.AddDays(7),
            // The day is 1-28, so every month has it.
            _ => Utc(last.Date.AddMonths(1) + options.DailyResetTime),
        };
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
