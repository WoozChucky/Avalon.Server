using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalon.Domain.Auth;

namespace Avalon.Api.Worlds;

/// <summary>
/// Reads Database:Worlds (#523): one entry per world, keyed by the id of its row in the auth Worlds
/// table, each with a World and a Characters connection string. The environment form is
/// Database__Worlds__2__World__ConnectionString. Refused, naming the setting and never its value,
/// when there is no world, an id is not a positive integer in canonical form, a world has only one
/// of its strings, or a string is blank.
/// </summary>
public static class WorldDatabaseSettings
{
    public const string Section = "Database:Worlds";

    /// <summary>
    /// Reads a world id in canonical form only: ASCII digits, no sign, no leading zero, no
    /// whitespace, 1 to 65535. "01" and "1" are different configuration keys and different routes,
    /// so accepting both would let two spellings name one world. The one parse of a world id, for
    /// the configuration keys and for the route alike.
    /// </summary>
    public static bool TryParseWorldId(string? text, [NotNullWhen(true)] out WorldId? id)
    {
        id = null;
        if (text is null
            || !ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ushort value)
            || value == 0
            || !string.Equals(text, value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return false;

        id = new WorldId(value);
        return true;
    }

    public static IReadOnlyList<ConfiguredWorld> Parse(IConfiguration configuration) =>
        TryParse(configuration, out IReadOnlyList<ConfiguredWorld> worlds, out string? refusal)
            ? worlds
            : throw new InvalidOperationException(refusal);

    public static bool TryParse(IConfiguration configuration, out IReadOnlyList<ConfiguredWorld> worlds,
        [NotNullWhen(false)] out string? refusal)
    {
        worlds = [];
        List<IConfigurationSection> entries = configuration.GetSection(Section).GetChildren().ToList();
        if (entries.Count == 0)
        {
            refusal =
                $"{Section} lists no world. The api needs at least one: set {Section}:<id>:World:ConnectionString " +
                $"and {Section}:<id>:Characters:ConnectionString (environment: Database__Worlds__<id>__World__ConnectionString " +
                "and Database__Worlds__<id>__Characters__ConnectionString), where <id> is the world's id in the auth Worlds table.";
            return false;
        }

        List<ConfiguredWorld> parsed = [];
        foreach (IConfigurationSection entry in entries)
        {
            string at = $"{Section}:{entry.Key}";
            if (!TryParseWorldId(entry.Key, out WorldId? id))
            {
                refusal = $"{at}: a world id is a positive integer up to 65535 with no leading zeros or sign, " +
                          "the id of the world's row in the auth Worlds table.";
                return false;
            }

            if (!TryRead(entry, at, "World", out string? world, out refusal)
                || !TryRead(entry, at, "Characters", out string? characters, out refusal))
                return false;

            parsed.Add(new ConfiguredWorld(id, world, characters));
        }

        worlds = parsed.OrderBy(w => w.Id.Value).ToList();
        refusal = null;
        return true;
    }

    private static bool TryRead(IConfigurationSection entry, string at, string database,
        [NotNullWhen(true)] out string? value, [NotNullWhen(false)] out string? refusal)
    {
        string setting = $"{at}:{database}:ConnectionString";
        value = entry[$"{database}:ConnectionString"];
        if (value is null)
        {
            refusal = $"{setting} is missing: a world needs both its World and its Characters connection string.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            value = null;
            refusal = $"{setting} is blank.";
            return false;
        }

        refusal = null;
        return true;
    }
}
