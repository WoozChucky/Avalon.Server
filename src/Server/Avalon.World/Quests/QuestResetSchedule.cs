using System.Diagnostics;
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
    /// <summary>
    /// The latest boundary of <paramref name="repeat" /> at or before <paramref name="utcNow" />, a UTC moment.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeat" /> has no boundary (Once or Repeatable).</exception>
    public static DateTime LastBoundary(QuestRepeat repeat, DateTime utcNow, QuestResetConfiguration options)
    {
        Debug.Assert(utcNow.Kind != DateTimeKind.Local, "Quest reset boundaries are on UTC, never local time.");
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

    /// <summary>
    /// The first daily boundary after <paramref name="utcNow" />, a UTC moment. Every weekly and monthly boundary is
    /// also a daily one, so it is the next boundary of any frequency, and the only one the reset clock needs.
    /// </summary>
    public static DateTime NextDailyBoundary(DateTime utcNow, QuestResetConfiguration options) =>
        LastBoundary(QuestRepeat.Daily, utcNow, options).AddDays(1);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
