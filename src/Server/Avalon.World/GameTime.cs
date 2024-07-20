using System.Diagnostics;

namespace Avalon.World;

public static class GameTime
{
    private static readonly DateTime StartTime = GetEpochTime();
    private static DateTime gameTime = GetEpochTime();
    private static TimeSpan gameMSTime = TimeSpan.Zero;

    private static DateTime gameTimeSystemPoint = DateTime.MinValue;
    private static Stopwatch gameTimeSteadyPoint = Stopwatch.StartNew();
    
    private static TimeSpan DeltaTime = TimeSpan.Zero;

    public static TimeSpan GetDeltaTime()
    {
        return DeltaTime;
    }
    
    public static DateTime GetStartTime()
    {
        return StartTime;
    }

    public static DateTime GetGameTime()
    {
        return gameTime;
    }

    public static TimeSpan GetGameTimeMS()
    {
        return gameMSTime;
    }

    public static DateTime GetSystemTime()
    {
        return gameTimeSystemPoint;
    }

    public static TimeSpan Now()
    {
        return gameTimeSteadyPoint.Elapsed;
    }

    public static TimeSpan GetUptime()
    {
        return gameTime - StartTime;
    }

    public static void UpdateGameTimers(TimeSpan deltaTime)
    {
        DeltaTime = deltaTime;
        gameTime = GetEpochTime();
        gameMSTime = GetTimeMS();
        gameTimeSystemPoint = DateTime.UtcNow;
        gameTimeSteadyPoint.Restart();
    }

    private static DateTime GetEpochTime()
    {
        return DateTime.UtcNow;
    }

    private static TimeSpan GetTimeMS()
    {
        return TimeSpan.FromMilliseconds((DateTime.UtcNow - StartTime).TotalMilliseconds);
    }
}
