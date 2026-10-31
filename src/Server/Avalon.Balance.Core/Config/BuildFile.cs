using System.Globalization;
using System.Text.RegularExpressions;
using Avalon.Common;

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

/// <summary>
/// One build in builds.json (B-R10, B-R16): a class, the talent nodes it takes in order (its allocation path), and the
/// priority list it casts from.
/// </summary>
public sealed class BuildEntry
{
    public CharacterClass Class { get; set; }

    public int[] Nodes { get; set; } = [];

    public RotationEntry[] Rotation { get; set; } = [];
}

/// <summary>A build, checked and compiled: its whole path held once, its rotation's conditions parsed.</summary>
public sealed record CompiledBuild(string Id, CharacterClass Class, IReadOnlyList<int> Nodes,
    IReadOnlyList<CompiledRotationEntry> Rotation)
{
    /// <summary>
    /// What the build holds at <paramref name="level" /> (B-R10): the first level + <paramref name="bonusPoints" /> nodes
    /// of its path, all of them when the path is shorter, as TalentPoints.Earned counts the points a level and the paying
    /// quests give. Every prefix of a path that compiled is valid. A fresh instance each call, with totals of its own.
    /// </summary>
    public HeldTalents HeldAt(ushort level, int bonusPoints, BalanceData data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bonusPoints);
        int earned = (int)Math.Min(Nodes.Count, (long)level + bonusPoints);
        return data.Talents.Hold(Class, [.. Nodes.Take(earned)]);
    }
}

/// <summary>balance/builds.json: build id to <see cref="BuildEntry" />, in the file's order.</summary>
public sealed partial class BuildFile : Dictionary<string, BuildEntry>
{
    public BuildFile()
        : base(StringComparer.Ordinal)
    {
    }

    /// <summary>
    /// Checks build <paramref name="id" /> against the seed's talent tree and compiles it (B-R16): its class must be
    /// playable, its whole path pass the tree's allocation rules (B-R11), and each rotation entry name an ability the
    /// whole path holds (the starter included) with conditions it knows.
    /// </summary>
    /// <exception cref="InvalidDataException">"build '{id}': ..." or "build '{id}' rotation entry {n}: ..." for the first problem.</exception>
    public CompiledBuild Compile(string id, BalanceData data)
    {
        if (!TryGetValue(id, out BuildEntry? build))
            throw new InvalidDataException($"builds: no build '{id}' (known: {string.Join(", ", Keys)})");
        if (build is null) throw new InvalidDataException($"build '{id}': the build is null");
        if (!CharacterClasses.IsPlayable(build.Class))
            throw new InvalidDataException($"build '{id}': class {build.Class} is not a playable class");
        if (build.Nodes is null) throw new InvalidDataException($"build '{id}': nodes is missing");
        if (build.Rotation is null || build.Rotation.Length == 0)
            throw new InvalidDataException($"build '{id}' has no rotation");

        HeldTalents whole;
        try
        {
            whole = data.Talents.Hold(build.Class, build.Nodes);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"build '{id}': {e.Message}", e);
        }

        var compiled = new List<CompiledRotationEntry>();
        for (int i = 0; i < build.Rotation.Length; i++)
        {
            RotationEntry entry = build.Rotation[i];
            string where = $"build '{id}' rotation entry {i + 1}";
            if (entry is null) throw new InvalidDataException($"{where}: the entry is null");
            if (entry.When is null || entry.When.Any(d => d is null || d.Values.Any(v => v is null)))
                throw new InvalidDataException($"{where}: when is missing or holds a null condition");
            if (!whole.Holds(entry.Ability))
                throw new InvalidDataException($"{where}: ability {entry.Ability} is never held by the build");

            var conditions = new List<Condition>();
            foreach ((string stat, string text) in entry.When.SelectMany(d => d))
            {
                if (!Condition.Stats.Contains(stat, StringComparer.Ordinal))
                    throw new InvalidDataException($"{where}: unknown condition '{stat}' (known: {string.Join(", ", Condition.Stats)})");
                Match match = ConditionText().Match(text);
                if (!match.Success)
                    throw new InvalidDataException($"{where}: '{text}' is not a comparison such as \">=2\"");
                conditions.Add(new Condition(stat, match.Groups["op"].Value,
                    double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture)));
            }

            compiled.Add(new CompiledRotationEntry(entry.Ability, conditions));
        }

        return new CompiledBuild(id, build.Class, Array.AsReadOnly([.. build.Nodes]), compiled);
    }

    [GeneratedRegex(@"^\s*(?<op>>=|<=|==|>|<)\s*(?<value>-?\d+(?:\.\d+)?)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConditionText();
}
