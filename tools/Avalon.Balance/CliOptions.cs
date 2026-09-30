using System.Globalization;
using Avalon.World.Public.Enums;

namespace Avalon.Balance;

public sealed record CliOptions(CharacterClass? Class, string? Scenario, int? Runs, int? Seed, string? OverridesPath,
    string? OutDir, bool Help)
{
    /// <exception cref="ArgumentException">An unknown option, a missing value, or a value that does not parse.</exception>
    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var o = new CliOptions(null, null, null, null, null, null, false);
        for (int i = 0; i < args.Count; i++)
        {
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

            o = args[i] switch
            {
                "--class" => o with { Class = ParseClass(Value()) },
                "--scenario" => o with { Scenario = Value() },
                "--runs" => o with { Runs = ParseInt("--runs", Value(), min: 1) },
                "--seed" => o with { Seed = ParseInt("--seed", Value(), min: int.MinValue) },
                "--overrides" => o with { OverridesPath = Value() },
                "--out" => o with { OutDir = Value() },
                "-h" or "--help" => o with { Help = true },
                _ => throw new ArgumentException($"Unknown option '{args[i]}'"),
            };
        }

        return o;
    }

    private static CharacterClass ParseClass(string text) =>
        Enum.TryParse(text, ignoreCase: true, out CharacterClass c) && Enum.IsDefined(c) && !int.TryParse(text, out _)
            ? c
            : throw new ArgumentException($"Unknown class '{text}' (known: {string.Join(", ", Enum.GetNames<CharacterClass>())})");

    private static int ParseInt(string option, string text, int min) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= min
            ? n
            : throw new ArgumentException($"{option} must be a whole number of at least {min}, not '{text}'");
}
