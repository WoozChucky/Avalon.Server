using System.Diagnostics.CodeAnalysis;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Loot;

/// <summary>One entry of a validated table.</summary>
public sealed record LootEntryView(
    int Sequence, ItemTemplateId? ItemTemplateId, LootTableId? ReferenceTableId, float Chance, int MinCount, int MaxCount);

/// <summary>Entries sharing a GroupId. A group always drops exactly one of them, picked by weight.</summary>
public sealed record LootGroupView(int GroupId, IReadOnlyList<LootEntryView> Entries);

/// <summary>
/// A validated table in roll order: ungrouped entries by Sequence, then groups by GroupId, each
/// group's entries by Sequence.
/// </summary>
public sealed record LootTableView(
    LootTableId Id, string Name, IReadOnlyList<LootEntryView> Ungrouped, IReadOnlyList<LootGroupView> Groups);

/// <summary>A table left out of the catalog, and why.</summary>
public sealed record LootTableRefusal(LootTableId Id, string Name, string Reason)
{
    public override string ToString() => $"loot table {Id.Value} '{Name}': {Reason}";
}

/// <summary>
/// Every loot table that passed validation, ready to roll. Built off the tick thread by the Loot
/// reload area and immutable afterwards, so the tick reads it without locking.
/// </summary>
/// <remarks>
/// A table that fails validation is left out with an error naming it, and every other table still
/// loads, so one bad row costs that table rather than the world server's startup or a whole reload.
/// A creature whose table was refused drops only its gold. A table that references a refused table
/// is refused too, because rolling it would silently skip part of what it holds.
/// </remarks>
public sealed class LootCatalog
{
    /// <summary>
    /// The largest count one entry may roll. Not in the design; it stops a mistyped count on an item
    /// that does not stack from putting that many objects on the ground from a single kill.
    /// </summary>
    public const int MaxEntryCount = 1000;

    private readonly Dictionary<int, LootTableView> _tables;

    public LootCatalog(IReadOnlyCollection<LootTable> tables, ILoggerFactory loggerFactory)
    {
        ILogger<LootCatalog> logger = loggerFactory.CreateLogger<LootCatalog>();
        Dictionary<int, LootTable> byId = tables.ToDictionary(t => t.Id.Value);
        List<LootTable> ordered = tables.OrderBy(t => t.Id.Value).ToList();
        Dictionary<int, string> refused = [];

        // 1. Each table's own entries.
        foreach (LootTable table in ordered)
        {
            if (FirstEntryProblem(table, byId) is { } reason)
                refused[table.Id.Value] = reason;
        }

        // 2. Cycles. Only tables whose entries are sound get here, so every reference resolves.
        foreach (LootTable table in ordered)
        {
            if (refused.ContainsKey(table.Id.Value))
                continue;

            if (FindCycle(table.Id.Value, byId) is { } cycle)
                refused[table.Id.Value] = "reference cycle " + string.Join(" -> ", cycle);
        }

        // 3. Anything that reaches a refused table, until nothing changes.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (LootTable table in ordered)
            {
                if (refused.ContainsKey(table.Id.Value))
                    continue;

                LootTableEntry? bad = table.Entries
                    .OrderBy(e => e.Sequence)
                    .FirstOrDefault(e => e.ReferenceTableId is { } target && refused.ContainsKey(target.Value));
                if (bad is null)
                    continue;

                refused[table.Id.Value] = $"entry {bad.Sequence} references refused table {bad.ReferenceTableId!.Value}";
                changed = true;
            }
        }

        _tables = ordered
            .Where(t => !refused.ContainsKey(t.Id.Value))
            .ToDictionary(t => t.Id.Value, ToView);

        EntryCount = _tables.Values.Sum(t => t.Ungrouped.Count + t.Groups.Sum(g => g.Entries.Count));

        Refused = refused
            .OrderBy(r => r.Key)
            .Select(r => new LootTableRefusal(new LootTableId(r.Key), byId[r.Key].Name, r.Value))
            .ToList();

        foreach (LootTableRefusal refusal in Refused)
        {
            logger.LogError("Refused loot table {TableId} '{TableName}': {Reason}. Creatures using it drop only gold",
                refusal.Id.Value, refusal.Name, refusal.Reason);
        }

        logger.LogInformation("Loaded {TableCount} loot tables with {EntryCount} entries; refused {RefusedCount}",
            TableCount, EntryCount, Refused.Count);
    }

    public int TableCount => _tables.Count;

    public int EntryCount { get; }

    public IReadOnlyList<LootTableRefusal> Refused { get; }

    public bool TryGet(LootTableId id, [NotNullWhen(true)] out LootTableView? table) =>
        _tables.TryGetValue(id.Value, out table);

    /// <summary>For the game master's reload reply.</summary>
    public string Describe()
    {
        string counts = $"{TableCount} tables, {EntryCount} entries";
        return Refused.Count == 0
            ? counts
            : $"{counts}, {Refused.Count} refused ({string.Join("; ", Refused)})";
    }

    private static string? FirstEntryProblem(LootTable table, IReadOnlyDictionary<int, LootTable> byId)
    {
        foreach (LootTableEntry e in table.Entries.OrderBy(e => e.Sequence))
        {
            bool hasItem = e.ItemTemplateId is not null;
            bool hasReference = e.ReferenceTableId is not null;

            if (hasItem && hasReference)
                return $"entry {e.Sequence} names both an item and a table";
            if (!hasItem && !hasReference)
                return $"entry {e.Sequence} names neither an item nor a table";
            if (hasReference && !byId.ContainsKey(e.ReferenceTableId!.Value))
                return $"entry {e.Sequence} references missing table {e.ReferenceTableId.Value}";
            if (e.MinCount < 1)
                return $"entry {e.Sequence} has MinCount {e.MinCount}; it must be at least 1";
            if (e.MinCount > e.MaxCount)
                return $"entry {e.Sequence} has MinCount {e.MinCount} above MaxCount {e.MaxCount}";
            if (e.MaxCount > MaxEntryCount)
                return $"entry {e.Sequence} has MaxCount {e.MaxCount}; it must be at most {MaxEntryCount}";
            if (float.IsNaN(e.Chance) || e.Chance < 0f || e.Chance > 100f)
                return FormattableString.Invariant($"entry {e.Sequence} has Chance {e.Chance}; it must be between 0 and 100");
        }

        return null;
    }

    /// <summary>The path back to <paramref name="start"/> through references, or null if there is none.</summary>
    private static List<int>? FindCycle(int start, IReadOnlyDictionary<int, LootTable> byId)
    {
        var path = new List<int> { start };
        var visited = new HashSet<int>();
        return Walk(start) ? path : null;

        bool Walk(int current)
        {
            foreach (int next in ReferencesOf(current))
            {
                if (next == start)
                {
                    path.Add(next);
                    return true;
                }

                if (!visited.Add(next))
                    continue;

                path.Add(next);
                if (Walk(next))
                    return true;
                path.RemoveAt(path.Count - 1);
            }

            return false;
        }

        IEnumerable<int> ReferencesOf(int id) => byId.TryGetValue(id, out LootTable? table)
            ? table.Entries.Where(e => e.ReferenceTableId is not null)
                .Select(e => e.ReferenceTableId!.Value).Distinct().OrderBy(v => v)
            : [];
    }

    private static LootTableView ToView(LootTable table)
    {
        var entries = table.Entries
            .OrderBy(e => e.Sequence)
            .Select(e => (e.GroupId, View: new LootEntryView(
                e.Sequence, e.ItemTemplateId, e.ReferenceTableId, e.Chance, e.MinCount, e.MaxCount)))
            .ToList();

        return new LootTableView(
            table.Id,
            table.Name,
            entries.Where(e => e.GroupId is null).Select(e => e.View).ToList(),
            entries.Where(e => e.GroupId is not null)
                .GroupBy(e => e.GroupId!.Value)
                .OrderBy(g => g.Key)
                .Select(g => new LootGroupView(g.Key, g.Select(e => e.View).ToList()))
                .ToList());
    }
}
