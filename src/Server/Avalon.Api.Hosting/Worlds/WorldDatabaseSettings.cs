using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalon.Domain.Auth;

namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// Reads Database:Worlds (#523): one entry per world, keyed by the id of its row in the auth Worlds
/// table, each with a World and a Characters connection string. The environment form is
/// Database__Worlds__2__World__ConnectionString. Refused, naming the setting and never its value,
/// when there is no world, an id is not a positive integer in canonical form, a world has only one
/// of its strings, or a string is blank. A process that reads only some of a world's databases
/// (<see cref="WorldDatabaseParts"/>, #794) needs only their strings and ignores the others.
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
        {
            return false;
        }

        id = new WorldId(value);
        return true;
    }

    /// <summary>Every world with both of its strings.</summary>
    public static IReadOnlyList<ConfiguredWorld> Parse(IConfiguration configuration) =>
        Parse(configuration, WorldDatabaseParts.Both);

    /// <summary>Every world with the strings of <paramref name="parts"/>.</summary>
    public static IReadOnlyList<ConfiguredWorld> Parse(IConfiguration configuration, WorldDatabaseParts parts) =>
        TryParse(configuration, parts, out IReadOnlyList<ConfiguredWorld> worlds, out string? refusal)
            ? worlds
            : throw new InvalidOperationException(refusal);

    public static bool TryParse(IConfiguration configuration, out IReadOnlyList<ConfiguredWorld> worlds,
        [NotNullWhen(false)] out string? refusal) =>
        TryParse(configuration, WorldDatabaseParts.Both, out worlds, out refusal);

    public static bool TryParse(IConfiguration configuration, WorldDatabaseParts parts,
        out IReadOnlyList<ConfiguredWorld> worlds, [NotNullWhen(false)] out string? refusal)
    {
        if (parts == WorldDatabaseParts.None)
            throw new ArgumentException("A process that reads no world database has no Database:Worlds to read.", nameof(parts));

        worlds = [];
        var entries = configuration.GetSection(Section).GetChildren().ToList();
        if (entries.Count == 0)
        {
            refusal = $"{Section} lists no world. The api needs at least one: set {Settings(parts, "<id>", ":")} " +
                      $"(environment: {Settings(parts, "<id>", "__")}), where <id> is the world's id in the auth Worlds table.";
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

            string? world = null;
            string? characters = null;
            if ((parts.HasFlag(WorldDatabaseParts.World) && !TryRead(entry, at, "World", parts, out world, out refusal))
                || (parts.HasFlag(WorldDatabaseParts.Characters) && !TryRead(entry, at, "Characters", parts, out characters, out refusal)))
            {
                return false;
            }

            parsed.Add(new ConfiguredWorld(id, world, characters));
        }

        worlds = parsed.OrderBy(w => w.Id.Value).ToList();
        refusal = null;
        return true;
    }

    /// <summary>The settings a world needs for <paramref name="parts"/>, joined with "and", in the given separator's form.</summary>
    private static string Settings(WorldDatabaseParts parts, string id, string separator) =>
        string.Join(" and ", new[] { WorldDatabaseParts.World, WorldDatabaseParts.Characters }
            .Where(part => parts.HasFlag(part))
            .Select(part => string.Join(separator, Section.Replace(":", separator, StringComparison.Ordinal), id, part,
                "ConnectionString")));

    private static bool TryRead(IConfigurationSection entry, string at, string database, WorldDatabaseParts parts,
        [NotNullWhen(true)] out string? value, [NotNullWhen(false)] out string? refusal)
    {
        string setting = $"{at}:{database}:ConnectionString";
        value = entry[$"{database}:ConnectionString"];
        if (value is null)
        {
            refusal = parts == WorldDatabaseParts.Both
                ? $"{setting} is missing: a world needs both its World and its Characters connection string."
                : $"{setting} is missing: this api reads each world's {database} database.";
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
