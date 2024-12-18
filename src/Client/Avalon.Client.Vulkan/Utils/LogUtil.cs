// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Utils;

public enum VerbosityLevels
{
    None = 0,
    Debug = 4
}

#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.
public static class log
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.
{
    public const int dt = 12;
    public const int nt = -12;
    public static VerbosityLevels Verbosity = VerbosityLevels.Debug;
    public static long TotalElapsed;
    public static long LastElapsed;
    public static long FirstTick;
    public static long LastTick;

    public static Dictionary<string, long> Laps = new();

    public static void RestartTimer()
    {
        if (Verbosity == VerbosityLevels.None)
        {
            return;
        }

        FirstTick = DateTime.Now.Ticks;
        LastTick = FirstTick;
        TotalElapsed = 0;
        LastElapsed = 0;
        Console.WriteLine($"{"Memory",8} | {"Total",dt + 2} | {"Last",dt} | {"Lap",dt} | {"Name",nt} | Message");
        Console.WriteLine("--------------------------------------------------------------------------------------");
    }

    public static void StartLap(string lap) => Laps[lap] = DateTime.Now.Ticks;

    //public static void debug(string msg)
    //{
    //    debug("", msg);
    //}

    public static void d(string lap, string msg, bool skipTimer = false)
    {
        if (Verbosity == VerbosityLevels.None)
        {
            return;
        }

        string memory = $"{Process.GetCurrentProcess().WorkingSet64 / 1_000_000: #,##0}MB";
        if (!skipTimer)
        {
            long now = DateTime.Now.Ticks;
            if (!Laps.ContainsKey(lap))
            {
                Laps[lap] = now;
            }

            string lapTicks = t2ms(now - Laps[lap]);
            Console.WriteLine(
                $"{memory,8} | {t2ms(now - FirstTick),dt + 2} | {t2ms(now - LastTick),dt} | {lapTicks,dt} | {lap,nt} | {msg}");
            LastTick = now;
        }
        else
        {
            Console.WriteLine(
                $"{memory,8} | {string.Empty,dt + 2} | {string.Empty,dt} | {string.Empty,dt} | {string.Empty,nt} | {msg}");
        }
    }

    public static string t2ms(long tick) => $"{tick / 10_000d:#,##0.000}ms";


    public static void error(string info, Exception ex, bool skipWrite = false)
    {
        StringBuilder sb = new();

        sb.AppendLine($"{info}");
        sb.AppendLine($"{ex.Message}");
        sb.AppendLine($"{ex.InnerException?.Message}");
        sb.AppendLine($"{ex.StackTrace}");

        Console.WriteLine("*******************************");
        Console.WriteLine("*******************************");
        Console.WriteLine("*******************************");
        Console.WriteLine("");
        Console.WriteLine("Error Occured!!!!!");
        Console.WriteLine($"Tell Howie this happend:\n{info}");
        Console.WriteLine("");
        Console.WriteLine(sb.ToString());
        Console.WriteLine("");
        Console.WriteLine("");

        if (!skipWrite)
        {
            string logFile = $"cloudy6_fatality_{DateTime.Now:yyyy-MM-ddtHHmmss}.log";
            string logDir = Path.GetTempPath();
            string logPath = Path.Combine(logDir, logFile);
            try
            {
                using (StreamWriter sw = new(logPath))
                {
                    sw.Write(sb.ToString());
                    sw.Close();
                }

                Console.WriteLine($"Log written to:\n{logPath}");
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"Sorry, could not write log file to {logPath}\n{ex2.Message}");
            }
        }

        Console.WriteLine("Press Any Key To Close...");
        Console.ReadLine();
    }
}
