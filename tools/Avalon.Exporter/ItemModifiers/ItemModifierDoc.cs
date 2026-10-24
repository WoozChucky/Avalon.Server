using System.Globalization;
using System.Text;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;

namespace Avalon.Exporter;

/// <summary>
/// The generated block of <c>docs/item-modifiers.md</c> (#1179): the modifier catalog as <see cref="ItemModifierSeed" />
/// seeds it, between two markers in a hand-written page. <c>item-modifier-doc</c> writes it, and ItemModifierDocShould
/// fails when the committed block and the seed disagree. It lists the affix families (code, element, name, kind, stat and
/// the base kinds they roll on), the tier table (each line's range and weight, by tier), the implicits per base and the
/// uniques.
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
        catch (InvalidOperationException ex)
        {
            return ex.Message;
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
        Dictionary<int, ulong> templateOf = ItemModifierSeed.TemplateLinks().ToDictionary(l => l.ModifierId, l => l.TemplateId.Value);
        Dictionary<ulong, ItemTemplate> templates = ItemBaseSeed.Equipment().ToDictionary(t => t.Id.Value);

        ItemModifierGroup[] affixes = [.. groups.Where(g => g.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix)];
        int tiers = affixes.Max(g => rows[g.Id].Count());

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
        text.Append("as the range of its low end, then \"to\" and the range of its high end. A dash marks a tier the family does not have.\n\n");
        text.Append("| Code | Element |");
        for (int tier = 1; tier <= tiers; tier++)
            text.Append(CultureInfo.InvariantCulture, $" T{tier} |");
        text.Append("\n| --- | --- |");
        for (int tier = 1; tier <= tiers; tier++)
            text.Append(" --- |");
        text.Append("\n| Item level | |");
        for (int tier = 1; tier <= tiers; tier++)
        {
            ushort? level = modifiers.FirstOrDefault(m => m.Tier == tier)?.RequiredItemLevel;
            text.Append(CultureInfo.InvariantCulture, $" {level} |");
        }

        text.Append('\n');
        foreach (ItemModifierGroup group in affixes)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {group.Code} | {group.Element} |");
            for (int tier = 1; tier <= tiers; tier++)
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
        text.Append("| Template | Base | Kind | Lines |\n");
        text.Append("| --- | --- | --- | --- |\n");
        foreach (ItemModifierGroup group in groups.Where(g => g.Kind == ItemModifierKind.Implicit))
        {
            ItemModifier row = rows[group.Id].Single();
            ItemTemplate template = templates[templateOf[row.Id]];
            text.Append(CultureInfo.InvariantCulture,
                $"| {template.Id.Value} | {group.Name} | {template.BaseKind} | {Lines(stats[row.Id], withStat: true)} |\n");
        }

        text.Append("\n### Uniques\n\n");
        text.Append("| Template | Unique | Kind | Lines |\n");
        text.Append("| --- | --- | --- | --- |\n");
        foreach (ItemModifierGroup group in groups.Where(g => g.Kind == ItemModifierKind.Unique))
        {
            ItemModifier row = rows[group.Id].Single();
            ItemTemplate template = templates[templateOf[row.Id]];
            text.Append(CultureInfo.InvariantCulture,
                $"| {template.Id.Value} | {group.Name} | {template.BaseKind} | {Lines(stats[row.Id], withStat: true)} |\n");
        }

        return text.ToString();
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
