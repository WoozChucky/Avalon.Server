using System.Globalization;
using Avalon.LoadTest.Bots;
using Avalon.LoadTest.Ramp;
using Avalon.LoadTest.Runs;

namespace Avalon.LoadTest;

/// <summary>The tool's hand-parsed command line; each command adds its options here.</summary>
public static class CommandLine
{
    /// <summary>The homelab API, with the trailing slash that keeps its <c>/api</c> prefix when routes resolve.</summary>
    public const string DefaultApi = "https://avalon.nunolevezinho.xyz/api/";

    /// <summary>The homelab load-test world.</summary>
    public const ushort DefaultWorld = 4;

    /// <summary>The most accounts <c>provision</c> makes: the API's cap on load-test accounts at once.</summary>
    public const int MaxBots = 5000;

    /// <summary><c>provision --count N [--world W] [--api URL] [--run ABC]</c>.</summary>
    public static ProvisionOptions ParseProvision(string[] args)
    {
        int? count = null;
        ushort world = DefaultWorld;
        Uri api = ParseApi(DefaultApi);
        string? runId = null;
        foreach ((string option, string value) in Pairs(args))
        {
            switch (option)
            {
                case "--count":
                    count = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= MaxBots
                        ? n
                        : throw new CommandLineException($"--count takes a number of bots, 1 to {MaxBots}.");
                    break;
                case "--world": world = ParseWorld(value); break;
                case "--api": api = ParseApi(value); break;
                case "--run": runId = ParseRunId(value); break;
                default: throw new CommandLineException($"Unknown option {option}.");
            }
        }

        return new ProvisionOptions(count ?? throw new CommandLineException("provision needs --count."), world, api, runId);
    }

    /// <summary><c>cleanup [--run ABC]</c>.</summary>
    public static CleanupOptions ParseCleanup(string[] args)
    {
        string? runId = null;
        foreach ((string option, string value) in Pairs(args))
        {
            runId = option == "--run" ? ParseRunId(value) : throw new CommandLineException($"Unknown option {option}.");
        }

        return new CleanupOptions(runId);
    }

    /// <summary><c>check [--run ABC] [--dial HOST] [--bot N] [--behaviour idle|walker]</c>.</summary>
    public static CheckOptions ParseCheck(string[] args)
    {
        string? runId = null;
        string? dial = null;
        int bot = 0;
        BehaviourKind behaviour = BehaviourKind.Idle;
        foreach ((string option, string value) in Pairs(args))
        {
            switch (option)
            {
                case "--run": runId = ParseRunId(value); break;
                case "--dial": dial = ParseDial(value); break;
                case "--bot":
                    bot = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n < MaxBots
                        ? n
                        : throw new CommandLineException($"--bot takes a bot's index in the run, 0 to {MaxBots - 1}.");
                    break;
                case "--behaviour":
                    behaviour = value switch
                    {
                        "idle" => BehaviourKind.Idle,
                        "walker" => BehaviourKind.Walker,
                        _ => throw new CommandLineException("--behaviour takes idle or walker."),
                    };
                    break;
                default: throw new CommandLineException($"Unknown option {option}.");
            }
        }

        return new CheckOptions(runId, dial, bot, behaviour);
    }

    /// <summary>The homelab Prometheus.</summary>
    public const string DefaultPrometheus = "http://10.10.1.15:30090/";

    /// <summary>The homelab load-test world server's pod.</summary>
    public const string DefaultPod = "avalon-world-loadtest-0";

    /// <summary>
    /// <c>ramp [--run ABC] [--mix idle=60,walker=30,churner=10] [--start 50] [--step 50] [--hold 90s] [--max N]
    /// [--limit name=value]... [--dial HOST] [--prometheus URL] [--pod NAME] [--sign-in-concurrency 8]</c>.
    /// </summary>
    public static RampArguments ParseRamp(string[] args)
    {
        string? runId = null;
        string mix = Mix.Default;
        int start = 50;
        int step = 50;
        var hold = TimeSpan.FromSeconds(90);
        int? max = null;
        List<string> limits = [];
        string? dial = null;
        Uri prometheus = new(DefaultPrometheus);
        string pod = DefaultPod;
        int signIns = 8;
        foreach ((string option, string value) in Pairs(args))
        {
            switch (option)
            {
                case "--run": runId = ParseRunId(value); break;
                case "--mix":
                    _ = Mix.Parse(value);
                    mix = value;
                    break;
                case "--start": start = ParseCount(option, value, MaxBots); break;
                case "--step": step = ParseCount(option, value, MaxBots); break;
                case "--hold": hold = ParseHold(value); break;
                case "--max": max = ParseCount(option, value, MaxBots); break;
                case "--limit": limits.Add(value); break;
                case "--dial": dial = ParseDial(value); break;
                case "--prometheus":
                    prometheus = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                        ? uri
                        : throw new CommandLineException("--prometheus takes Prometheus's http or https origin.");
                    break;
                case "--pod":
                    // It goes into a query's label matcher: a pod name's characters only.
                    pod = value.Length is > 0 and <= 253 && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.')
                        ? value
                        : throw new CommandLineException("--pod takes a pod name: lower-case letters, digits, '-' and '.'.");
                    break;
                case "--sign-in-concurrency": signIns = ParseCount(option, value, 64); break;
                default: throw new CommandLineException($"Unknown option {option}.");
            }
        }

        return new RampArguments(runId, mix, start, step, hold, max, Limits.WithOverrides(limits), dial, prometheus, pod, signIns);
    }

    /// <summary>A hold: <c>90s</c>, <c>2m</c> or plain seconds; at least the settle and a 20 s judged window.</summary>
    private static TimeSpan ParseHold(string value)
    {
        (string number, int scale) = value.EndsWith('m') ? (value[..^1], 60) : value.EndsWith('s') ? (value[..^1], 1) : (value, 1);
        TimeSpan minimum = RampRunner.Settle + TimeSpan.FromSeconds(20);
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n <= 3600 &&
            TimeSpan.FromSeconds(n * scale) is var hold && hold >= minimum && hold <= TimeSpan.FromHours(1)
                ? hold
                : throw new CommandLineException(
                    $"--hold takes a duration such as 90s or 2m, {minimum.TotalSeconds:0} s to 1 h (a {RampRunner.Settle.TotalSeconds:0} s settle, then the judged window).");
    }

    private static int ParseCount(string option, string value, int max) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= max
            ? n
            : throw new CommandLineException($"{option} takes a whole number, 1 to {max}.");

    /// <summary>A host name or address to dial instead of the join reply's host; TLS still names the reply's server.</summary>
    public static string ParseDial(string value) =>
        Uri.CheckHostName(value) != UriHostNameType.Unknown
            ? value
            : throw new CommandLineException("--dial takes a host name or an IP address.");

    /// <summary>An https API origin, given its trailing slash: the game routes refuse plain http.</summary>
    public static Uri ParseApi(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? origin) || origin.Scheme != Uri.UriSchemeHttps)
            throw new CommandLineException("--api takes the API's https origin: the game routes refuse plain http.");

        // The routes are resolved against it, which keeps a path prefix (an ingress's /api) only behind a slash.
        return origin.AbsolutePath.EndsWith('/') ? origin : new Uri(origin.AbsoluteUri + "/");
    }

    /// <summary>A world id, 1 to 65535.</summary>
    public static ushort ParseWorld(string value) =>
        ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort id) && id > 0
            ? id
            : throw new CommandLineException("--world takes a world id, 1 to 65535.");

    /// <summary>A run id: three ASCII letters, upper-cased.</summary>
    public static string ParseRunId(string value) =>
        value.Length == 3 && value.All(char.IsAsciiLetter)
            ? value.ToUpperInvariant()
            : throw new CommandLineException("--run takes a run id, three letters.");

    /// <summary>The arguments as option and value pairs; an option without a value is refused.</summary>
    private static IEnumerable<(string Option, string Value)> Pairs(string[] args)
    {
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length) throw new CommandLineException($"{args[i]} needs a value.");
            yield return (args[i], args[i + 1]);
        }
    }
}

/// <summary>The options of <c>provision</c>.</summary>
/// <param name="RunId">The first run's id, or null for the tool to pick one no kept run file lists.</param>
public sealed record ProvisionOptions(int Count, ushort World, Uri Api, string? RunId);

/// <summary>The options of <c>cleanup</c>.</summary>
/// <param name="RunId">The run to delete, or null for the only one kept.</param>
public sealed record CleanupOptions(string? RunId);

/// <summary>The options of <c>check</c>.</summary>
/// <param name="RunId">The run, or null for the only one kept.</param>
/// <param name="Dial">The host to dial instead of the join reply's, or null.</param>
/// <param name="Bot">The bot's index in the run.</param>
/// <param name="Behaviour">What the bot does in the world for the check: idle or walker.</param>
public sealed record CheckOptions(string? RunId, string? Dial, int Bot, BehaviourKind Behaviour);

/// <summary>The options of <c>ramp</c> as given, before the run is known.</summary>
/// <param name="RunId">The run, or null for the only one kept.</param>
/// <param name="Max">The most bots, or null for the run's size.</param>
public sealed record RampArguments(
    string? RunId, string Mix, int Start, int Step, TimeSpan Hold, int? Max, IReadOnlyList<Limit> Limits, string? Dial,
    Uri Prometheus, string Pod, int SignInConcurrency)
{
    /// <summary>
    /// The options against <paramref name="run"/>, loaded for its bots (<see cref="RunFile.Load"/>, so it lists at
    /// least one): <c>--max</c> defaults to the run's size and cannot pass it.
    /// </summary>
    public RampOptions For(RunFile run)
    {
        int max = Max ?? run.Bots.Count;
        if (max > run.Bots.Count)
            throw new CommandLineException($"Run {run.RunId} has {run.Bots.Count} bots: --max takes at most that.");

        return new RampOptions(Mix, Math.Min(Start, max), Step, Hold, max, Limits, Dial, Prometheus, Pod, SignInConcurrency);
    }
}

/// <summary>A command line the tool cannot run; the usage follows.</summary>
public sealed class CommandLineException(string message) : Exception(message);
