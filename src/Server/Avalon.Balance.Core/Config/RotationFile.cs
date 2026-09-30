using System.Globalization;
using System.Text.RegularExpressions;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

public sealed class RotationEntry
{
    public uint Ability { get; set; }

    public Dictionary<string, string>[] When { get; set; } = [];
}

public sealed record Condition(string Stat, string Op, double Value)
{
    public static readonly string[] Stats = ["targetsAlive", "healthPct", "power", "powerPct"];

    public bool Holds(double actual) => Op switch
    {
        ">=" => actual >= Value,
        "<=" => actual <= Value,
        ">" => actual > Value,
        "<" => actual < Value,
        _ => actual == Value,
    };
}

public sealed record CompiledRotationEntry(uint AbilityId, IReadOnlyList<Condition> When);

public sealed partial class RotationFile : Dictionary<CharacterClass, RotationEntry[]>
{
    /// <exception cref="InvalidDataException">The class has no rotation, or an entry names an ability or condition it cannot use.</exception>
    public IReadOnlyList<CompiledRotationEntry> Compile(CharacterClass characterClass, BalanceData data)
    {
        if (!TryGetValue(characterClass, out RotationEntry[]? entries) || entries.Length == 0)
            throw new InvalidDataException($"rotations: {characterClass} has no rotation");

        HashSet<uint> kit = data.KitOf(characterClass).Select(a => a.Id.Value).ToHashSet();
        var compiled = new List<CompiledRotationEntry>();
        for (int i = 0; i < entries.Length; i++)
        {
            string where = $"{characterClass} rotation entry {i + 1}";
            if (!kit.Contains(entries[i].Ability))
                throw new InvalidDataException($"{where}: ability {entries[i].Ability} is not in the class's kit");

            var conditions = new List<Condition>();
            foreach ((string stat, string text) in entries[i].When.SelectMany(d => d))
            {
                if (!Condition.Stats.Contains(stat, StringComparer.Ordinal))
                    throw new InvalidDataException($"{where}: unknown condition '{stat}' (known: {string.Join(", ", Condition.Stats)})");
                Match match = ConditionText().Match(text);
                if (!match.Success)
                    throw new InvalidDataException($"{where}: '{text}' is not a comparison such as \">=2\"");
                conditions.Add(new Condition(stat, match.Groups[1].Value,
                    double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
            }

            compiled.Add(new CompiledRotationEntry(entries[i].Ability, conditions));
        }

        return compiled;
    }

    [GeneratedRegex(@"^\s*(>=|<=|==|>|<)\s*(-?\d+(?:\.\d+)?)\s*$")]
    private static partial Regex ConditionText();
}
