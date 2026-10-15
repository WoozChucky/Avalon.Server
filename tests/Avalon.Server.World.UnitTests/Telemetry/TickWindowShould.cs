using Avalon.World.Telemetry;

namespace Avalon.Server.World.UnitTests.Telemetry;

/// <summary>
/// The windows the ping's tick time and <c>world.tick.rate</c> are taken over (#1016), fed made-up timestamps: each
/// tick a start and an end, at <c>frequency</c> timestamps a second. A window holds the ticks that ended in it, closes
/// at the first tick that ends a second or more after it opened, and the next opens where it closed.
/// </summary>
public class TickWindowShould
{
    [Theory]
    // Open until a second has passed: nothing measured, so nothing reported.
    [InlineData(1_000, new long[] { 0, 10, 500, 510, 998, 999 }, new int[0], 0u, 0.0)]
    // Closed by the tick that ends a second after the first began: the mean of its three ticks, 10 ms, three a second.
    // The clock does not start at 0: the first window opens at the first tick, not at timestamp 0.
    [InlineData(1_000, new long[] { 5_000, 5_010, 5_500, 5_510, 5_990, 6_000 }, new[] { 2 }, 10_000u, 3.0)]
    // The next window opens where the last closed and holds none of its ticks: 4 and 6 ms, a mean of 5, over the
    // 1.25 s from the last close to its own, 1.6 a second.
    [InlineData(1_000, new long[] { 0, 10, 990, 1_000, 1_500, 1_504, 2_244, 2_250 }, new[] { 1, 3 }, 5_000u, 1.6)]
    // A tick longer than a second closes a window on its own.
    [InlineData(1_000, new long[] { 0, 1_500 }, new[] { 0 }, 1_500_000u, 1 / 1.5)]
    // A mean under half a microsecond still reads 1: 0 is kept for "not reported".
    [InlineData(10_000_000, new long[] { 0, 1, 9_999_999, 10_000_000 }, new[] { 1 }, 1u, 2.0)]
    public void Report_the_mean_tick_time_and_rate_of_the_last_closed_window(long frequency, long[] ticks,
        int[] closedBy, uint meanMicros, double ticksPerSecond)
    {
        var window = new TickWindow(frequency);
        var closed = new List<int>();

        for (int tick = 0; tick < ticks.Length / 2; tick++)
        {
            if (window.Add(ticks[2 * tick], ticks[2 * tick + 1]))
                closed.Add(tick);
        }

        Assert.Equal(closedBy, closed);
        Assert.Equal(meanMicros, window.MeanMicros);
        Assert.Equal(ticksPerSecond, window.TicksPerSecond, 9);
    }
}
