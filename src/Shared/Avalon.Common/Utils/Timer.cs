namespace Avalon.Common.Utils;

public static class Timer
{
    public static TimeSpan ToTimeSpan(this long milliseconds)
    {
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public static long CurrentTimeMillis()
    {
        return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
    }

    public static long GetDiff(long oldMs, long newMs)
    {
        // CurrentTimeMillis() has a limited data range and this is case when it overflow in this tick
        if (oldMs > newMs)
        {
            return (0xFFFFFFFF - oldMs) + newMs;
        }

        return newMs - oldMs;
    }
}
