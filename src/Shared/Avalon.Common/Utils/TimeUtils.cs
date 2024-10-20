using System.Diagnostics;

namespace Avalon.Common.Utils;

public static class TimeUtils
{
    private static readonly Stopwatch s_applicationStartTime = Stopwatch.StartNew();

    public static TimeSpan GetApplicationStartTime() => s_applicationStartTime.Elapsed;

    public static TimeSpan GetTimeMS() => s_applicationStartTime.Elapsed;

    public static TimeSpan GetMSTimeDiff(TimeSpan oldMSTime, TimeSpan newMSTime)
    {
        if (oldMSTime > newMSTime)
        {
            return oldMSTime - newMSTime;
        }

        return newMSTime - oldMSTime;
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
        uint newMSTime = (uint)newTime.TotalMilliseconds;
        return GetMsTimeDiff(oldMsTime, newMSTime);
    }

    public static uint GetMsTimeDiffToNow(uint oldMsTime) => GetMsTimeDiff(oldMsTime, GetMsTime());

    public static TimeSpan GetMsTimeDiffToNow(TimeSpan oldMsTime) => GetMSTimeDiff(oldMsTime, GetTimeMS());

    public static long GetEpochTime() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
