using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Data;

public sealed record AppliedOverride(string Key, string Seed, string Value);

public sealed record OverrideReport(IReadOnlyList<AppliedOverride> Applied, IReadOnlyList<string> Stale)
{
    public static readonly OverrideReport None = new([], []);
}

/// <summary>
/// balance/overrides.json: a flat map "Table.key.Column": value. Every entry is resolved before any is written, and
/// an unknown table, key or column stops the run naming it.
/// </summary>
public static class Overrides
{
    private sealed record Table(Type RowType, string[] KeyColumns, Func<SeedTables, string, object?> Find, bool Keyless = false);

    private static readonly Dictionary<string, Table> Tables = new(StringComparer.Ordinal)
    {
        ["Ability"] = new(typeof(AbilityTemplate), ["Id"],
            (t, k) => t.AbilityTemplates.FirstOrDefault(a => Text(a.Id.Value) == k)),
        ["ClassLevelStat"] = new(typeof(ClassLevelStat), ["Class", "Level"],
            (t, k) => k.Split('.') is [var c, var l] && Enum.TryParse(c, false, out CharacterClass cls)
                ? t.ClassLevelStats.FirstOrDefault(r => r.Class == cls && Text(r.Level) == l)
                : null),
        ["ClassStatFactors"] = new(typeof(ClassStatFactors), ["Class"],
            (t, k) => Enum.TryParse(k, false, out CharacterClass cls) ? t.ClassStatFactors.FirstOrDefault(r => r.Class == cls) : null),
        ["CombatFormula"] = new(typeof(CombatFormula), ["Id"], (t, _) => t.CombatFormulas.SingleOrDefault(), Keyless: true),
        ["CreatureBaseStats"] = new(typeof(CreatureBaseStat), ["Level"],
            (t, k) => t.CreatureBaseStats.FirstOrDefault(r => Text(r.Level) == k)),
        ["CreatureRarityModifiers"] = new(typeof(CreatureRarityModifier), ["Rarity"],
            (t, k) => Enum.TryParse(k, false, out CreatureRarity r) ? t.CreatureRarityModifiers.FirstOrDefault(m => m.Rarity == r) : null),
        ["CreatureTemplate"] = new(typeof(CreatureTemplate), ["Id"],
            (t, k) => t.CreatureTemplates.FirstOrDefault(c => Text(c.Id.Value) == k)),
        ["Item"] = new(typeof(ItemTemplate), ["Id"],
            (t, k) => t.ItemTemplates.FirstOrDefault(i => Text(i.Id.Value) == k)),
    };

    public static OverrideReport ApplyFile(SeedTables tables, string path, bool required)
    {
        if (!File.Exists(path))
        {
            return required ? throw new FileNotFoundException($"Overrides file '{path}' not found", path) : OverrideReport.None;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return Apply(tables, document.RootElement);
    }

    public static OverrideReport Apply(SeedTables tables, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("overrides must be a JSON object of \"Table.key.Column\": value");

        var writes = new List<(object Row, PropertyInfo Column, object? Value, string Key, object? Seed)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty entry in root.EnumerateObject())
        {
            string key = entry.Name;
            if (!seen.Add(key))
                throw new InvalidDataException($"Override '{key}' is given twice");

            string[] parts = key.Split('.');
            if (parts.Length < 2)
                throw new InvalidDataException($"Override '{key}' is not Table.key.Column");

            if (!Tables.TryGetValue(parts[0], out Table? table))
                throw new InvalidDataException($"Override '{key}': unknown table '{parts[0]}' (known: {string.Join(", ", Tables.Keys)})");

            string rowKey = string.Join('.', parts[1..^1]);
            if (table.Keyless && rowKey.Length > 0)
                throw new InvalidDataException($"Override '{key}': {parts[0]} has one row and takes no key");

            object row = table.Find(tables, rowKey)
                ?? throw new InvalidDataException($"Override '{key}': no {parts[0]} row '{rowKey}'");

            string columnName = parts[^1];
            if (table.KeyColumns.Contains(columnName, StringComparer.Ordinal))
                throw new InvalidDataException($"Override '{key}': '{columnName}' is the row's key");

            PropertyInfo column = table.RowType.GetProperty(columnName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidDataException($"Override '{key}': {parts[0]} has no column '{columnName}'");

            if (column.SetMethod is not { IsPublic: true })
                throw new InvalidDataException($"Override '{key}': {parts[0]}.{columnName} is computed and cannot be overridden");

            object? value = Convert(entry.Value, column.PropertyType, key);
            writes.Add((row, column, value, key, column.GetValue(row)));
        }

        var applied = new List<AppliedOverride>();
        var stale = new List<string>();
        foreach ((object row, PropertyInfo column, object? value, string key, object? seed) in writes)
        {
            if (Equals(seed, value))
            {
                stale.Add(key);
                continue;
            }

            column.SetValue(row, value);
            applied.Add(new AppliedOverride(key, Show(seed), Show(value)));
        }

        return new OverrideReport(applied, stale);
    }

    private static object? Convert(JsonElement json, Type type, string key)
    {
        Type? underlying = Nullable.GetUnderlyingType(type);
        Type target = underlying ?? type;

        if (json.ValueKind == JsonValueKind.Null)
            return underlying is not null ? null : throw new InvalidDataException($"Override '{key}': the column cannot be null");

        try
        {
            if (target.IsEnum)
                return json.ValueKind == JsonValueKind.String
                    ? Enum.Parse(target, json.GetString()!, ignoreCase: false)
                    : Enum.ToObject(target, json.GetInt64());
            if (target == typeof(bool)) return json.GetBoolean();
            if (target == typeof(string)) return json.GetString();
            if (target == typeof(float)) return json.GetSingle();
            if (target == typeof(double)) return json.GetDouble();
            if (target == typeof(byte) || target == typeof(short) || target == typeof(ushort) || target == typeof(int)
                || target == typeof(uint) || target == typeof(long) || target == typeof(ulong))
            {
                decimal number = json.GetDecimal();
                if (number != decimal.Truncate(number))
                    throw new OverflowException();
                return System.Convert.ChangeType(number, target, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException($"Override '{key}': {json.GetRawText()} does not fit a {target.Name} column", e);
        }

        throw new InvalidDataException($"Override '{key}': a {target.Name} column cannot be overridden");
    }

    private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Show(object? value) => value switch
    {
        null => "null",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
