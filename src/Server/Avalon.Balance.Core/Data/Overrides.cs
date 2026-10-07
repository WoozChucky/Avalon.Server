using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

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
    internal sealed record Table(
        Type RowType,
        string[] KeyColumns,
        Func<SeedTables, string, object?> Find,
        Func<SeedTables, IEnumerable<(string Key, object Row)>> Rows,
        bool Keyless = false)
    {
        /// <summary>The columns an override may write: public, settable, not the row's key, and of a type Apply converts.</summary>
        public IEnumerable<PropertyInfo> Columns() => RowType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0
                && p.SetMethod is { IsPublic: true }
                && !KeyColumns.Contains(p.Name, StringComparer.Ordinal)
                && Supports(p.PropertyType));
    }

    internal static readonly Dictionary<string, Table> Tables = new(StringComparer.Ordinal)
    {
        ["Ability"] = new(typeof(AbilityTemplate), ["Id"],
            (t, k) => t.AbilityTemplates.FirstOrDefault(a => Text(a.Id.Value) == k),
            t => t.AbilityTemplates.Select(a => (Text(a.Id.Value), (object)a))),
        ["ClassLevelStat"] = new(typeof(ClassLevelStat), ["Class", "Level"],
            (t, k) => k.Split('.') is [var c, var l] && Enum.TryParse(c, false, out CharacterClass cls)
                ? t.ClassLevelStats.FirstOrDefault(r => r.Class == cls && Text(r.Level) == l)
                : null,
            t => t.ClassLevelStats.Select(r => ($"{r.Class}.{Text(r.Level)}", (object)r))),
        ["ClassStatFactors"] = new(typeof(ClassStatFactors), ["Class"],
            (t, k) => Enum.TryParse(k, false, out CharacterClass cls) ? t.ClassStatFactors.FirstOrDefault(r => r.Class == cls) : null,
            t => t.ClassStatFactors.Select(r => (r.Class.ToString(), (object)r))),
        ["CombatFormula"] = new(typeof(CombatFormula), ["Id"], (t, _) => t.CombatFormulas.SingleOrDefault(),
            t => t.CombatFormulas.Take(1).Select(r => ("", (object)r)), Keyless: true),
        ["CreatureBaseStats"] = new(typeof(CreatureBaseStat), ["Level"],
            (t, k) => t.CreatureBaseStats.FirstOrDefault(r => Text(r.Level) == k),
            t => t.CreatureBaseStats.Select(r => (Text(r.Level), (object)r))),
        ["CreatureRarityModifiers"] = new(typeof(CreatureRarityModifier), ["Rarity"],
            (t, k) => Enum.TryParse(k, false, out CreatureRarity r) ? t.CreatureRarityModifiers.FirstOrDefault(m => m.Rarity == r) : null,
            t => t.CreatureRarityModifiers.Select(m => (m.Rarity.ToString(), (object)m))),
        ["CreatureTemplate"] = new(typeof(CreatureTemplate), ["Id"],
            (t, k) => t.CreatureTemplates.FirstOrDefault(c => Text(c.Id.Value) == k),
            t => t.CreatureTemplates.Select(c => (Text(c.Id.Value), (object)c))),
        ["Item"] = new(typeof(ItemTemplate), ["Id"],
            (t, k) => t.ItemTemplates.FirstOrDefault(i => Text(i.Id.Value) == k),
            t => t.ItemTemplates.Select(i => (Text(i.Id.Value), (object)i))),
        // An aura's Modifiers list is not a column an override can write (Supports refuses a list): its modifiers are
        // their own table.
        ["Aura"] = new(typeof(AuraTemplate), ["Id"],
            (t, k) => t.AuraTemplates.FirstOrDefault(a => Text(a.Id.Value) == k),
            t => t.AuraTemplates.Select(a => (Text(a.Id.Value), (object)a))),
        ["AuraStatModifier"] = new(typeof(AuraStatModifier), ["AuraId", "Stat"],
            (t, k) => k.Split('.') is [var aura, var s] && Enum.TryParse(s, false, out AuraStat stat)
                ? t.AuraStatModifiers.FirstOrDefault(m => Text(m.AuraId.Value) == aura && m.Stat == stat)
                : null,
            t => t.AuraStatModifiers.Select(m => ($"{Text(m.AuraId.Value)}.{m.Stat}", (object)m))),
    };

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
            PropertyInfo column = table.Columns().FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.Ordinal))
                ?? throw Refusal(table, parts[0], key, columnName);

            object? value = Convert(entry.Value, column, key);
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

    /// <summary>Why a column is not in <see cref="Table.Columns" />: the message only diagnoses; Columns() decides.</summary>
    private static InvalidDataException Refusal(Table table, string tableName, string key, string columnName)
    {
        if (table.KeyColumns.Contains(columnName, StringComparer.Ordinal))
            return new InvalidDataException($"Override '{key}': '{columnName}' is the row's key");

        PropertyInfo? property = table.RowType.GetProperty(columnName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
            return new InvalidDataException($"Override '{key}': {tableName} has no column '{columnName}'");
        if (property.SetMethod is not { IsPublic: true })
            return new InvalidDataException($"Override '{key}': {tableName}.{columnName} is computed and cannot be overridden");

        return new InvalidDataException($"Override '{key}': a {(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).Name} column cannot be overridden");
    }

    private static object? Convert(JsonElement json, PropertyInfo column, string key)
    {
        Type type = column.PropertyType;
        Type? underlying = Nullable.GetUnderlyingType(type);
        Type target = underlying ?? type;

        if (json.ValueKind == JsonValueKind.Null)
            return AcceptsNull(column) ? null : throw new InvalidDataException($"Override '{key}': the column cannot be null");

        try
        {
            if (target.IsEnum)
            {
                return json.ValueKind == JsonValueKind.String
                    ? Enum.Parse(target, json.GetString()!, ignoreCase: false)
                    : Enum.ToObject(target, json.GetInt64());
            }

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

    /// <summary>
    /// A nullable value type, or a reference type declared nullable (a <c>string?</c> such as ItemTemplate.UseScript), takes
    /// null; a non-nullable string such as a Name does not.
    /// </summary>
    private static bool AcceptsNull(PropertyInfo column) =>
        Nullable.GetUnderlyingType(column.PropertyType) is not null
        || (!column.PropertyType.IsValueType && new NullabilityInfoContext().Create(column).WriteState == NullabilityState.Nullable);

    /// <summary>The column types Convert can write.</summary>
    internal static bool Supports(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        return target.IsEnum || target == typeof(bool) || target == typeof(string) || target == typeof(float)
            || target == typeof(double) || target == typeof(byte) || target == typeof(short) || target == typeof(ushort)
            || target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong);
    }

    private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string Show(object? value) => value switch
    {
        null => "null",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
