using System.Globalization;
using System.Text;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Exporter;

/// <summary>
/// The generated block of <c>docs/item-modifiers.md</c> (#1179): the modifier catalog as <see cref="ItemModifierSeed" />
/// seeds it, between two markers in a hand-written page. <c>item-modifier-doc</c> writes it, and ItemModifierDocShould
/// fails when the committed block and the seed disagree. It lists the affix families (code, element, name, kind, stat and
/// the base kinds they roll on), the tier table (each line's range and weight, by tier), the implicit groups (their
/// ranges by tier and where they come from), every base by kind and line (its tier, level, base-stat ranges and implicit)
/// and the uniques with the bases they are made on.
/// </summary>
public static class ItemModifierDoc
{
    public const string DocsDirectory = "docs";
    public const string FileName = "item-modifiers.md";
    public const string Begin = "<!-- item-modifiers:begin -->";
    public const string End = "<!-- item-modifiers:end -->";

    private static readonly (string Name, ItemBaseKind[] Kinds)[] s_families =
    [
        ("plate", Kinds("Plate")),
        ("leather", Kinds("Leather")),
        ("cloth", Kinds("Cloth")),
    ];

    private static readonly (string Name, ItemBaseKind[] Kinds)[] s_slots =
    [
        ("helmets", Kinds("Head")),
        ("shoulders", Kinds("Shoulders")),
        ("chests", Kinds("Chest")),
        ("gloves", Kinds("Hands")),
        ("legs", Kinds("Legs")),
        ("boots", Kinds("Feet")),
    ];

    /// <summary>
    /// The page this block lives in: the repository's own docs/item-modifiers.md, whatever output root the exporter was
    /// given, since the page is hand-written and committed, not an artifact written somewhere else.
    /// </summary>
    public static string PagePath() => Path.Combine(RepositoryRoot.Find(), DocsDirectory, FileName);

    /// <summary>
    /// Null when the page can take the block: it exists and holds each marker once, in order. Otherwise why not, so the
    /// exporter stops before it writes anything.
    /// </summary>
    public static string? Readiness()
    {
        string path;
        try
        {
            path = PagePath();
        }
        catch (InvalidOperationException)
        {
            // The page is the repository's whatever --out names, so the root's own advice (pass --out) does not apply.
            return $"needs the repository's {DocsDirectory}/{FileName}, and no Avalon.sln is above {AppContext.BaseDirectory}: " +
                   "run the exporter from a checkout of the repository.";
        }

        if (!File.Exists(path))
            return $"needs {path}: the page is hand-written around its generated block.";

        try
        {
            _ = Splice(File.ReadAllText(path), string.Empty);
            return null;
        }
        catch (InvalidDataException ex)
        {
            return $"cannot write {path}: {ex.Message}";
        }
    }

    /// <summary>Writes the block into the page at <paramref name="path" />, which must exist (see <see cref="Readiness" />).</summary>
    public static void Write(string path)
    {
        string page = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Lf.Write(path, Splice(page, Render()));
        Console.WriteLine($"wrote the generated block of {path}");
    }

    /// <summary>
    /// <paramref name="page" /> with everything between its two markers replaced by <paramref name="block" />.
    /// </summary>
    /// <exception cref="InvalidDataException">The page lacks a marker, holds one twice, or has them out of order.</exception>
    public static string Splice(string page, string block)
    {
        int begin = page.IndexOf(Begin, StringComparison.Ordinal);
        int end = page.IndexOf(End, StringComparison.Ordinal);
        bool once = begin >= 0 && end >= 0
            && page.IndexOf(Begin, begin + Begin.Length, StringComparison.Ordinal) < 0
            && page.IndexOf(End, end + End.Length, StringComparison.Ordinal) < 0;
        if (!once || end < begin)
            throw new InvalidDataException($"The page needs {Begin} and then {End}, each once, around its generated block.");

        int from = begin + Begin.Length;
        return string.Concat(page.AsSpan(0, from), "\n", block, page.AsSpan(end));
    }

    /// <summary>The block, from the seed: pure, so the guard test compares it with the committed page.</summary>
    public static string Render()
    {
        ItemModifierGroup[] groups = ItemModifierSeed.Groups();
        ItemModifier[] modifiers = ItemModifierSeed.Modifiers();
        ILookup<int, ItemModifierStat> stats = ItemModifierSeed.Stats().ToLookup(s => s.ModifierId);
        ILookup<int, ItemModifierWeight> weights = ItemModifierSeed.Weights().ToLookup(w => w.ModifierId);
        ILookup<int, ItemModifier> rows = modifiers.ToLookup(m => m.GroupId);
        ItemTemplateModifier[] links = ItemModifierSeed.TemplateLinks();
        Dictionary<ulong, ItemTemplate> templates = ItemBaseSeed.Equipment().ToDictionary(t => t.Id.Value);

        ItemModifierGroup[] affixes = [.. groups.Where(g => g.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix)];
        // The tiers any affix has, weakest first: they count down, so T9 is the first column.
        byte[] tiers = [.. affixes.SelectMany(g => rows[g.Id]).Select(m => m.Tier!.Value).Distinct().OrderDescending()];

        var text = new StringBuilder();
        text.Append("<!-- Generated by tools/Avalon.Exporter -- item-modifier-doc from ItemModifierSeed. Do not edit by hand. -->\n\n");

        text.Append("### Families\n\n");
        text.Append("| Code | Element | Name | Kind | Stat | Rolls on |\n");
        text.Append("| --- | --- | --- | --- | --- | --- |\n");
        foreach (ItemModifierGroup group in affixes)
        {
            ItemModifier first = rows[group.Id].First();
            ItemBaseKind[] kinds = [.. weights[first.Id].Where(w => w.Weight > 0).Select(w => w.BaseKind).Order()];
            text.Append(CultureInfo.InvariantCulture,
                $"| {group.Code} | {group.Element} | {group.Name} | {group.Kind} | {stats[first.Id].Single().Stat} | {RollsOn(kinds)} |\n");
        }

        text.Append("\n### Tiers\n\n");
        text.Append("Each cell is the tier's range, then its weight on every base kind the family rolls on. An \"adds\" line reads ");
        text.Append("as its low end, then \"to\" and its high end. Tiers count down: T9 is the weakest.\n\n");
        text.Append("| Code | Element |");
        foreach (byte tier in tiers)
            text.Append(CultureInfo.InvariantCulture, $" T{tier} |");
        text.Append("\n| --- | --- |");
        foreach (byte _ in tiers)
            text.Append(" --- |");
        text.Append("\n| Item level | |");
        foreach (byte tier in tiers)
        {
            ushort? level = modifiers.FirstOrDefault(m => m.Tier == tier)?.RequiredItemLevel;
            text.Append(CultureInfo.InvariantCulture, $" {level} |");
        }

        text.Append('\n');
        foreach (ItemModifierGroup group in affixes)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Code} | {group.Element} |");
            foreach (byte tier in tiers)
            {
                ItemModifier? row = rows[group.Id].FirstOrDefault(m => m.Tier == tier);
                if (row is null)
                {
                    text.Append(" - |");
                    continue;
                }

                string weight = string.Join(", ", weights[row.Id].Select(w => w.Weight).Distinct().Order());
                text.Append(CultureInfo.InvariantCulture, $" {Lines(stats[row.Id], withStat: false)} · {weight} |");
            }

            text.Append('\n');
        }

        text.Append("\n### Implicits\n\n");
        text.Append("Each implicit group gives one stat; a base links the row of its own tier. Its ranges are the source ");
        text.Append("family's at the same tier, or its own.\n\n");
        text.Append("| Group | Stat | Source |");
        foreach (byte tier in tiers)
            text.Append(CultureInfo.InvariantCulture, $" T{tier} |");
        text.Append("\n| --- | --- | --- |");
        foreach (byte _ in tiers)
            text.Append(" --- |");
        text.Append('\n');
        foreach (ItemModifierGroup group in groups.Where(g => g.Kind == ItemModifierKind.Implicit))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"| {group.Id} | {stats[rows[group.Id].First().Id].Single().Stat} | {ItemModifierSeed.ImplicitSource(group.Id) ?? "own"} |");
            foreach (byte tier in tiers)
            {
                ItemModifier? row = rows[group.Id].FirstOrDefault(m => m.Tier == tier);
                text.Append(row is null ? " - |" : $" {Lines(stats[row.Id], withStat: false)} |");
            }

            text.Append('\n');
        }

        // Every base by kind and line, as the world builds its lines (ItemsPatch.LinesOf) over the seeded catalog, so a
        // line has one definition.
        ItemsPatch items = SeededItems(templates.Values, groups, modifiers, stats, weights, links);
        ILookup<ulong, ItemTemplateBaseStat> baseStats = ItemBaseSeed.BaseStats().ToLookup(s => s.TemplateId.Value);
        text.Append("\n### Bases\n\n");
        text.Append("Each kind's lines, by their first tier's id. A base's base stats are the ranges every copy rolls; its ");
        text.Append("implicit is its line's group at the base's tier. A trinket base has no tier: only a unique is made on it.\n\n");
        text.Append("| Kind | Template | Name | Tier | Level | Base stats | Implicit |\n");
        text.Append("| --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (ItemBaseKind kind in Enum.GetValues<ItemBaseKind>().Order())
        {
            foreach (ItemTemplate template in items.LinesOf(kind).SelectMany(line => line))
            {
                ItemModifier @implicit = items.Modifiers.LinkedTo(template.Id)[0];
                string tier = kind == ItemBaseKind.Trinket ? "-" : $"T{@implicit.Tier}";
                string ranges = string.Join(", ", baseStats[template.Id.Value].OrderBy(s => s.Stat)
                    .Select(s => $"{s.Stat} {Span(s.Min, s.Max)}")) is { Length: > 0 } listed ? listed : "none";
                text.Append(CultureInfo.InvariantCulture,
                    $"| {kind} | {template.Id.Value} | {template.Name} | {tier} | {template.RequiredLevel} | {ranges} | {Lines(stats[@implicit.Id], withStat: true)} |\n");
            }
        }

        text.Append("\n### Uniques\n\n");
        text.Append("A unique's copy rolls its base's base stats and implicit, then its own lines.\n\n");
        text.Append("| Template | Unique | Kind | Base | Lines |\n");
        text.Append("| --- | --- | --- | --- | --- |\n");
        foreach (ItemModifierGroup group in groups.Where(g => g.Kind == ItemModifierKind.Unique))
        {
            ItemModifier row = rows[group.Id].Single();
            ItemTemplate template = templates[links.Single(l => l.ModifierId == row.Id).TemplateId.Value];
            ItemTemplate @base = templates[template.BaseTemplateId!.Value];
            text.Append(CultureInfo.InvariantCulture,
                $"| {template.Id.Value} | {group.Name} | {template.BaseKind} | {@base.Id.Value} {@base.Name} | {Lines(stats[row.Id], withStat: true)} |\n");
        }

        return text.ToString();
    }

    /// <summary>The seeded templates over the seeded catalog, each modifier given its group, stat lines and weights.</summary>
    private static ItemsPatch SeededItems(IReadOnlyCollection<ItemTemplate> templates, ItemModifierGroup[] groups,
        ItemModifier[] modifiers, ILookup<int, ItemModifierStat> stats, ILookup<int, ItemModifierWeight> weights,
        ItemTemplateModifier[] links)
    {
        Dictionary<int, ItemModifierGroup> byId = groups.ToDictionary(g => g.Id);
        foreach (ItemModifier modifier in modifiers)
        {
            modifier.Group = byId[modifier.GroupId];
            modifier.Stats = [.. stats[modifier.Id]];
            modifier.Weights = [.. weights[modifier.Id]];
        }

        return new ItemsPatch(templates, new ItemModifierCatalog(modifiers, links, NullLogger.Instance));
    }

    private static string Lines(IEnumerable<ItemModifierStat> lines, bool withStat) => string.Join(", ", lines
        .OrderBy(line => line.Index)
        .Select(line => (withStat ? $"{line.Stat} " : "") + Range(line)));

    private static string Range(ItemModifierStat line) => line.Min2 is { } min2 && line.Max2 is { } max2
        ? $"{Span(line.Min, line.Max)} to {Span(min2, max2)}"
        : Span(line.Min, line.Max);

    private static string Span(int min, int max) =>
        min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min.ToString(CultureInfo.InvariantCulture)}-{max.ToString(CultureInfo.InvariantCulture)}";

    // The kinds, with whole sets named: every armour kind as "armour", else a whole family ("plate") or a whole slot
    // across the families ("gloves"); the rest by name.
    private static string RollsOn(ItemBaseKind[] kinds)
    {
        var left = new HashSet<ItemBaseKind>(kinds);
        List<string> names = [];
        ItemBaseKind[] armour = [.. s_families.SelectMany(f => f.Kinds)];
        if (armour.All(left.Contains))
        {
            names.Add("armour");
            left.ExceptWith(armour);
        }

        foreach ((string name, ItemBaseKind[] set) in s_families.Concat(s_slots))
        {
            if (set.All(left.Contains))
            {
                names.Add(name);
                left.ExceptWith(set);
            }
        }

        names.AddRange(left.Order().Select(kind => kind.ToString()));
        return string.Join(", ", names);
    }

    private static ItemBaseKind[] Kinds(string part) =>
        [.. Enum.GetValues<ItemBaseKind>().Where(kind => kind.ToString().Contains(part, StringComparison.Ordinal))];
}
