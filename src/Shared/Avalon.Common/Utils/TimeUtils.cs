using System.Diagnostics;

namespace Avalon.Common.Utils;

public static class TimeUtils
{
    private static readonly Stopwatch ApplicationStartTime = Stopwatch.StartNew();

    public static TimeSpan GetApplicationStartTime()
    {
        return ApplicationStartTime.Elapsed;
    }

    public static TimeSpan GetTimeMS()
    {
        return ApplicationStartTime.Elapsed;
    }

    public static TimeSpan GetMSTimeDiff(TimeSpan oldMSTime, TimeSpan newMSTime)
    {
        if (oldMSTime > newMSTime)
        {
            return oldMSTime - newMSTime;
        }
        else
        {
            return newMSTime - oldMSTime;
        }
    }

    public static uint GetMSTime()
    {
        return (uint)ApplicationStartTime.ElapsedMilliseconds;
    }

    public static uint GetMSTimeDiff(uint oldMSTime, uint newMSTime)
    {
        // getMSTime() have limited data range and this is case when it overflow in this tick
        if (oldMSTime > newMSTime)
        {
            return (0xFFFFFFFF - oldMSTime) + newMSTime;
        }
        else
        {
            return newMSTime - oldMSTime;
        }
    }

    public static uint GetMSTimeDiff(uint oldMSTime, TimeSpan newTime)
    {
        uint newMSTime = (uint)newTime.TotalMilliseconds;
        return GetMSTimeDiff(oldMSTime, newMSTime);
    }

    public static uint GetMSTimeDiffToNow(uint oldMSTime)
    {
        return GetMSTimeDiff(oldMSTime, GetMSTime());
    }

    public static TimeSpan GetMSTimeDiffToNow(TimeSpan oldMSTime)
    {
        return GetMSTimeDiff(oldMSTime, GetTimeMS());
    }

    public static long GetEpochTime()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
