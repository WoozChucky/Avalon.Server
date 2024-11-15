using System.Diagnostics;

namespace Avalon.Common.Utils;

public static class TimeUtils
{
    private static readonly Stopwatch s_applicationStartTime = Stopwatch.StartNew();

    public static TimeSpan GetApplicationStartTime() => s_applicationStartTime.Elapsed;

    public static TimeSpan GetTimeMs() => s_applicationStartTime.Elapsed;

    public static TimeSpan GetMsTimeDiff(TimeSpan oldMsTime, TimeSpan newMsTime)
    {
        if (oldMsTime > newMsTime)
        {
            return oldMsTime - newMsTime;
        }

        return newMsTime - oldMsTime;
    }

    public static uint GetMsTime() => (uint)s_applicationStartTime.ElapsedMilliseconds;

    public static uint GetMsTimeDiff(uint oldMsTime, uint newMsTime)
    {
        // getMSTime() have limited data range and this is case when it overflow in this tick
        if (oldMsTime > newMsTime)
        {
            return 0xFFFFFFFF - oldMsTime + newMsTime;
        }

        return newMsTime - oldMsTime;
    }

    public static uint GetMsTimeDiff(uint oldMsTime, TimeSpan newTime)
    {
        uint newMsTime = (uint)newTime.TotalMilliseconds;
        return GetMsTimeDiff(oldMsTime, newMsTime);
    }

    public static uint GetMsTimeDiffToNow(uint oldMsTime) => GetMsTimeDiff(oldMsTime, GetMsTime());

    public static TimeSpan GetMsTimeDiffToNow(TimeSpan oldMsTime) => GetMsTimeDiff(oldMsTime, GetTimeMs());

    public static long GetEpochTime() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
