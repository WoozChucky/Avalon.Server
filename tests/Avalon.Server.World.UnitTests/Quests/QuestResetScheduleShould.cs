using System.Globalization;
using Avalon.World.Configuration;
using Avalon.World.Quests;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// Daily, weekly and monthly quests come back at fixed UTC boundaries (#710). The schedule finds the latest boundary at
/// or before a moment, and the reset clock advances them once per crossing, however far the clock jumped.
/// </summary>
public class QuestResetScheduleShould
{
    // dailyTime "hh:mm", weekly, monthly, then now and the expected last boundary, ISO-8601 UTC.
    [Theory]
    [InlineData(QuestRepeat.Daily, "04:00", DayOfWeek.Monday, 1, "2026-01-01T03:59:59.9999999Z", "2025-12-31T04:00:00Z")]
    [InlineData(QuestRepeat.Daily, "04:00", DayOfWeek.Monday, 1, "2026-01-01T04:00:00Z", "2026-01-01T04:00:00Z")]
    [InlineData(QuestRepeat.Weekly, "04:00", DayOfWeek.Monday, 1, "2026-01-05T03:59:59Z", "2025-12-29T04:00:00Z")]
    [InlineData(QuestRepeat.Weekly, "04:00", DayOfWeek.Monday, 1, "2026-01-05T04:00:00Z", "2026-01-05T04:00:00Z")]
    [InlineData(QuestRepeat.Weekly, "04:00", DayOfWeek.Monday, 1, "2026-01-04T23:00:00Z", "2025-12-29T04:00:00Z")]
    [InlineData(QuestRepeat.Weekly, "00:00", DayOfWeek.Sunday, 1, "2026-01-04T00:00:00Z", "2026-01-04T00:00:00Z")]
    [InlineData(QuestRepeat.Monthly, "04:00", DayOfWeek.Monday, 1, "2026-01-01T03:59:59Z", "2025-12-01T04:00:00Z")]
    [InlineData(QuestRepeat.Monthly, "04:00", DayOfWeek.Monday, 1, "2026-03-01T04:00:00Z", "2026-03-01T04:00:00Z")]
    [InlineData(QuestRepeat.Monthly, "23:30", DayOfWeek.Monday, 28, "2026-03-01T00:00:00Z", "2026-02-28T23:30:00Z")]
    public void Find_the_last_boundary_at_or_before_now(QuestRepeat repeat, string dailyTime, DayOfWeek weekly,
        int monthly, string now, string expected)
    {
        var options = new QuestResetConfiguration
        {
            DailyResetTime = TimeSpan.Parse(dailyTime, CultureInfo.InvariantCulture),
            WeeklyResetDay = weekly,
            MonthlyResetDay = monthly,
        };

        DateTime last = QuestResetSchedule.LastBoundary(repeat, Utc(now), options);

        Assert.Equal(Utc(expected), last);
        Assert.Equal(DateTimeKind.Utc, last.Kind);
    }

    [Fact]
    public void Raise_the_generation_once_per_crossing_and_record_which_frequencies_rolled()
    {
        var clock = new QuestResetClock(new QuestResetConfiguration(), new ManualTimerClock()); // Thu 2026-01-01 00:00Z
        Assert.Equal((0, Utc("2026-01-01T04:00:00Z")), (clock.Generation, clock.NextBoundary));

        Assert.False(clock.Advance(Utc("2026-01-01T03:59:59Z")));
        Assert.True(clock.Advance(Utc("2026-01-01T04:00:00Z")));      // day 1: daily and monthly roll
        Assert.Equal((1, 1, 0, 1), (clock.Generation, clock.RolledAt(QuestRepeat.Daily),
            clock.RolledAt(QuestRepeat.Weekly), clock.RolledAt(QuestRepeat.Monthly)));
        Assert.Equal(Utc("2026-01-02T04:00:00Z"), clock.NextBoundary);
        Assert.False(clock.Advance(Utc("2026-01-01T12:00:00Z")));

        Assert.True(clock.Advance(Utc("2026-02-10T12:00:00Z")));      // one jump over many boundaries
        Assert.Equal((2, 2, 2, 2), (clock.Generation, clock.RolledAt(QuestRepeat.Daily),
            clock.RolledAt(QuestRepeat.Weekly), clock.RolledAt(QuestRepeat.Monthly)));
        Assert.Equal((Utc("2026-02-10T04:00:00Z"), Utc("2026-02-09T04:00:00Z"), Utc("2026-02-01T04:00:00Z")),
            (clock.LastBoundary(QuestRepeat.Daily), clock.LastBoundary(QuestRepeat.Weekly), clock.LastBoundary(QuestRepeat.Monthly)));

        DateTime boundary = clock.LastBoundary(QuestRepeat.Daily);
        Assert.False(clock.AvailableAgain(QuestRepeat.Daily, boundary));              // at the boundary counts as after
        Assert.True(clock.AvailableAgain(QuestRepeat.Daily, boundary.AddTicks(-1)));
        Assert.False(clock.AvailableAgain(QuestRepeat.Once, DateTime.MinValue));
        Assert.True(clock.AvailableAgain(QuestRepeat.Repeatable, boundary));
    }

    // Each row changes one setting from the defaults; the inclusive upper edges (and so the defaults) are accepted.
    [Theory]
    [InlineData("DailyResetTime", "-00:00:01", false)]
    [InlineData("DailyResetTime", "1.00:00:00", false)]
    [InlineData("DailyResetTime", "23:59:59", true)]
    [InlineData("WeeklyResetDay", "7", false)]
    [InlineData("MonthlyResetDay", "0", false)]
    [InlineData("MonthlyResetDay", "29", false)]
    [InlineData("MonthlyResetDay", "28", true)]
    public void Accept_a_reset_setting_only_inside_its_range(string setting, string value, bool accepted)
    {
        var options = new QuestResetConfiguration();
        switch (setting)
        {
            case "DailyResetTime": options.DailyResetTime = TimeSpan.Parse(value, CultureInfo.InvariantCulture); break;
            case "WeeklyResetDay": options.WeeklyResetDay = (DayOfWeek)int.Parse(value, CultureInfo.InvariantCulture); break;
            default: options.MonthlyResetDay = int.Parse(value, CultureInfo.InvariantCulture); break;
        }

        ValidateOptionsResult result = new QuestResetValidation().Validate(null, new GameConfiguration { QuestResets = options });

        Assert.Equal(accepted, result.Succeeded);
        if (!accepted)
            Assert.Contains($"Game:QuestResets:{setting}", result.FailureMessage, StringComparison.Ordinal);
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
