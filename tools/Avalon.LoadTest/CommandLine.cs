using System.Globalization;

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
/// <param name="RunId">The first run's id, or null for the API to choose one.</param>
public sealed record ProvisionOptions(int Count, ushort World, Uri Api, string? RunId);

/// <summary>The options of <c>cleanup</c>.</summary>
/// <param name="RunId">The run to delete, or null for the only one kept.</param>
public sealed record CleanupOptions(string? RunId);

/// <summary>A command line the tool cannot run; the usage follows.</summary>
public sealed class CommandLineException(string message) : Exception(message);
