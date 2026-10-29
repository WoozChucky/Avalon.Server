using System.Globalization;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Balance.Core;

/// <summary>The combat formula, as the world server's own checks accept it.</summary>
public sealed record CombatData(CombatFormula Formula);

/// <summary>
/// The seed tables after overrides, validated by the server's own validators, so a row a /reload would refuse stops
/// the run naming it. Read-only once built.
/// </summary>
public sealed class BalanceData
{
    private readonly IReadOnlyDictionary<ulong, WornItem> _worn;

    private BalanceData(SeedTables tables, IReadOnlyDictionary<AuraId, AuraTemplate> auras,
        IReadOnlyDictionary<AbilityId, AbilityTemplate> abilities, IReadOnlyList<string> refusedAbilities, CombatData combat,
        ItemModifierCatalog itemModifiers, IReadOnlyDictionary<ulong, WornItem> worn, CreatureStatDeriver creatureStats)
    {
        Tables = tables;
        ItemModifiers = itemModifiers;
        _worn = worn;
        Auras = auras;
        Abilities = abilities;
        RefusedAbilities = refusedAbilities;
        Combat = combat;
        CreatureStats = creatureStats;
        HostileTemplates = tables.CreatureTemplates
            .Where(t => !t.Invulnerable && t.ScriptName is not null && CreatureKits.ByScript.ContainsKey(t.ScriptName))
            .OrderBy(t => t.Id.Value)
            .ToList();
    }

    public SeedTables Tables { get; }

    /// <summary>
    /// The item modifier catalog (#1179) the world would build from the same rows: every modifier with its group and stat
    /// lines, and each template's links. No weights are read: the simulator rolls no affix.
    /// </summary>
    public ItemModifierCatalog ItemModifiers { get; }

    /// <summary>The aura templates that passed the server's checks, by id, each with its stat modifiers attached.</summary>
    public IReadOnlyDictionary<AuraId, AuraTemplate> Auras { get; }

    /// <summary>The ability templates that passed the server's checks, by id.</summary>
    public IReadOnlyDictionary<AbilityId, AbilityTemplate> Abilities { get; }

    /// <summary>The templates left out, each with its reason; empty for any data <see cref="From" /> accepts.</summary>
    public IReadOnlyList<string> RefusedAbilities { get; }

    public CombatData Combat { get; }
    public CreatureStatDeriver CreatureStats { get; }

    /// <summary>Templates that fight: not invulnerable, and their script declares a kit.</summary>
    public IReadOnlyList<CreatureTemplate> HostileTemplates { get; }

    /// <exception cref="InvalidDataException">A row the server would refuse; the message names it.</exception>
    public static BalanceData From(SeedTables tables)
    {
        // Auras: each row with its modifiers, checked as the world's AuraCatalog checks it (the script names aside: the
        // simulator runs no script). Attaching the modifiers writes only the row given, a copy wherever a run reads it.
        var auras = new Dictionary<AuraId, AuraTemplate>();
        List<string> refusedAuras = [];
        foreach (AuraTemplate aura in tables.AuraTemplates.OrderBy(a => a.Id.Value))
        {
            aura.Modifiers = tables.AuraStatModifiers
                .Where(m => m.AuraId.Value == aura.Id.Value)
                .OrderBy(m => m.Stat)
                .ToList();
            if (AuraRules.Problem(aura) is { } reason)
                refusedAuras.Add($"aura {aura.Id.Value} '{aura.Name}': {reason}");
            else
                auras[aura.Id] = aura;
        }

        if (refusedAuras.Count > 0)
            throw new InvalidDataException("The aura catalog refused: " + string.Join("; ", refusedAuras));

        var abilities = new Dictionary<AbilityId, AbilityTemplate>();
        List<string> refused = [];
        // The abilities the talent tree grants (#1181) are the class abilities, held to the class rules.
        HashSet<AbilityId> granted = [.. tables.TalentNodeAbilities.Select(l => l.AbilityId)];
        foreach (AbilityTemplate template in tables.AbilityTemplates.OrderBy(t => t.Id.Value))
        {
            // As the world's AbilityCatalog: the row's own checks, then its aura link.
            if ((AbilityRules.Problem(template, granted.Contains(template.Id))
                 ?? AuraRules.LinkProblem(template.Affects, template.AuraId,
                     id => auras.TryGetValue(id, out AuraTemplate? aura) ? aura : null)) is { } reason)
            {
                refused.Add($"ability {template.Id.Value} '{template.Name}': {reason}");
                continue;
            }

            abilities[template.Id] = template;
        }

        if (refused.Count > 0)
            throw new InvalidDataException("The ability catalog refused: " + string.Join("; ", refused));

        CombatFormula formula = CombatDataRules.Build(tables.CombatFormulas);
        ItemModifierCatalog itemModifiers = ItemModifierCatalogOf(tables);
        IReadOnlyDictionary<ulong, ItemTemplate> bases = CheckItemTemplates(tables, itemModifiers);
        // Gear is fixed stats (B-R4): each template's one copy, made once here; Worn hands each row modifiers of its own.
        var worn = tables.ItemTemplates.ToDictionary(t => t.Id.Value,
            t => FixedGear.Of(t, NamedBase(t, bases), itemModifiers));
        CreatureTemplateRules.Validate(tables.CreatureTemplates);
        CreatureTemplateRules.ValidateRarities(tables.CreatureRarityModifiers);
        var creatureStats = new CreatureStatDeriver(tables.CreatureBaseStats, tables.CreatureRarityModifiers,
            NullLoggerFactory.Instance);

        return new BalanceData(tables, auras, abilities, refused, new CombatData(formula), itemModifiers, worn, creatureStats);
    }

    /// <summary>
    /// The modifier catalog, built as the world's read builds it: each modifier given its group and its stat lines in index
    /// order, then the shared <see cref="ItemModifierCatalog" />. A row it would log and leave out stops the run. Only the
    /// rows given are written, each a copy wherever a run reads it.
    /// </summary>
    private static ItemModifierCatalog ItemModifierCatalogOf(SeedTables tables)
    {
        var groups = tables.ItemModifierGroups.ToDictionary(g => g.Id);
        ILookup<int, ItemModifierStat> lines = tables.ItemModifierStats.ToLookup(l => l.ModifierId);
        foreach (ItemModifier modifier in tables.ItemModifiers)
        {
            // A modifier whose group is not seeded keeps none, which the catalog refuses by name.
            modifier.Group = groups.GetValueOrDefault(modifier.GroupId)!;
            modifier.Stats = [.. lines[modifier.Id].OrderBy(l => l.Index)];
        }

        // The ranges a copy rolls within (simulator side: no world rule holds a line's range, but the world's roll throws on
        // one that runs backwards), so an override cannot make a line the world could not roll.
        List<string> badLines = [.. tables.ItemModifierStats
            .OrderBy(l => l.ModifierId).ThenBy(l => l.Index)
            .Select(l => LineProblem(l) is { } problem ? $"modifier {l.ModifierId} line {l.Index}: {problem}" : null)
            .OfType<string>()];
        if (badLines.Count > 0)
            throw new InvalidDataException("The item modifier catalog refused: " + string.Join("; ", badLines));

        var refusals = new RefusalLog();
        var catalog = new ItemModifierCatalog(tables.ItemModifiers, tables.ItemTemplateModifiers, refusals);
        return refusals.Errors.Count == 0
            ? catalog
            : throw new InvalidDataException("The item modifier catalog refused: " + string.Join("; ", refusals.Errors));
    }

    /// <summary>
    /// Why a modifier line's ranges cannot be rolled, or null when they can: each runs from its minimum up to its maximum,
    /// and the second range is set whole or not at all.
    /// </summary>
    private static string? LineProblem(ItemModifierStat line) => line switch
    {
        { Min: var min, Max: var max } when min > max => $"its range {min}-{max} runs past its maximum",
        { Min2: null, Max2: not null } or { Min2: not null, Max2: null } =>
            $"its second range {line.Min2?.ToString(CultureInfo.InvariantCulture) ?? "null"}-{line.Max2?.ToString(CultureInfo.InvariantCulture) ?? "null"} is set at one end only",
        { Min2: { } min2, Max2: { } max2 } when min2 > max2 => $"its second range {min2}-{max2} runs past its maximum",
        _ => null,
    };

    /// <summary>
    /// Each item template given its base-stat rows in stat order, then held to the rules the world's items area holds it
    /// to (ItemsPatch.Build), in its order: every template that is not unique first, its links against its rarity, its
    /// base stats against its kind; then each unique, the same way (one that links nothing aside), and its named base
    /// among the templates the first pass kept. A template the world would leave out stops the run, every one named.
    /// </summary>
    /// <returns>The templates that are not unique, by id: the bases a unique may name.</returns>
    private static IReadOnlyDictionary<ulong, ItemTemplate> CheckItemTemplates(SeedTables tables, ItemModifierCatalog modifiers)
    {
        ILookup<ulong, ItemTemplateBaseStat> baseStats = tables.ItemTemplateBaseStats.ToLookup(r => r.TemplateId.Value);
        foreach (ItemTemplate template in tables.ItemTemplates)
            template.BaseStats = [.. baseStats[template.Id.Value].OrderBy(r => r.Stat)];

        var refused = new SortedDictionary<ulong, string>();
        void Judge(ItemTemplate template, string? reason)
        {
            if (reason is not null)
                refused[template.Id.Value] = $"item {template.Id.Value} '{template.Name}': {reason}";
        }

        foreach (ItemTemplate template in tables.ItemTemplates.Where(t => t.Rarity != ItemRarity.Unique))
        {
            Judge(template, ItemModifierCatalog.RarityRefusal(template.Rarity, template.BaseKind, modifiers.LinkedTo(template.Id))
                            ?? ItemBaseStatRules.Problem(template)
                            ?? ItemBaseStatRules.NamedBaseProblem(template, null));
        }

        var bases = tables.ItemTemplates
            .Where(t => t.Rarity != ItemRarity.Unique && !refused.ContainsKey(t.Id.Value))
            .ToDictionary(t => t.Id.Value);
        foreach (ItemTemplate unique in tables.ItemTemplates.Where(t => t.Rarity == ItemRarity.Unique))
        {
            IReadOnlyList<ItemModifier> linked = modifiers.LinkedTo(unique.Id);
            Judge(unique, (linked.Count == 0 ? null : ItemModifierCatalog.RarityRefusal(unique.Rarity, unique.BaseKind, linked))
                          ?? ItemBaseStatRules.Problem(unique)
                          ?? ItemBaseStatRules.NamedBaseProblem(unique, NamedBase(unique, bases)));
        }

        return refused.Count == 0
            ? bases
            : throw new InvalidDataException("The item templates refused: " + string.Join("; ", refused.Values));
    }

    /// <summary>A unique's named base among <paramref name="bases" />, as the world's items area finds it; null for any other template.</summary>
    private static ItemTemplate? NamedBase(ItemTemplate template, IReadOnlyDictionary<ulong, ItemTemplate> bases) =>
        template.Rarity == ItemRarity.Unique && template.BaseTemplateId is { } id ? bases.GetValueOrDefault(id.Value) : null;

    /// <param name="auras">What the character's auras add, folded in as the server's stats refresh folds them; none when null.</param>
    /// <param name="worn">The fixed copies worn (<see cref="Worn" />), read as the server's stats refresh reads a character's items.</param>
    public DerivedCharacterStats CharacterStats(CharacterClass characterClass, ushort level, IReadOnlyList<WornItem> worn,
        AuraStatTotals? auras = null)
    {
        ClassLevelStat row = Tables.ClassLevelStats.FirstOrDefault(r => r.Class == characterClass && r.Level == level)
            ?? throw new InvalidDataException($"ClassLevelStat {characterClass} level {level} is not seeded");
        return CharacterStatsCalculator.Calculate(row, worn, Combat.Formula, auras);
    }

    /// <summary>
    /// The abilities a new character of the class holds (#1181): its starter alone, which its start on the talent tree
    /// links, as the catalog holds it. Every other ability is a node it must walk to.
    /// </summary>
    public IReadOnlyList<AbilityTemplate> KitOf(CharacterClass characterClass)
    {
        TalentClassStart start = Tables.TalentClassStarts.FirstOrDefault(s => s.Class == characterClass)
            ?? throw new InvalidDataException($"The talent tree has no start for {characterClass}");
        TalentNodeAbility link = Tables.TalentNodeAbilities.FirstOrDefault(l => l.NodeId == start.NodeId)
            ?? throw new InvalidDataException($"{characterClass}'s start {start.NodeId} links no ability");
        return Abilities.TryGetValue(link.AbilityId, out AbilityTemplate? starter)
            ? [starter]
            : throw new InvalidDataException(
                $"{characterClass} starts with ability {link.AbilityId.Value}, which the catalog does not hold");
    }

    /// <summary>
    /// The copy of item <paramref name="id" /> the simulator wears (B-R4, <see cref="FixedGear" />): every base stat and
    /// modifier value at the midpoint of its range. Made once, in <see cref="From" />; each call hands out modifiers of
    /// its own, so no caller can change what another row wears.
    /// </summary>
    public WornItem Worn(ulong id) =>
        _worn.TryGetValue(id, out WornItem copy)
            ? copy with { Modifiers = copy.Modifiers?.ConvertAll(m => m.CopyAs(m.Id, m.ItemInstanceId, m.Ordinal, m.Imprinted)) }
            : throw new InvalidDataException($"Item {id} is not seeded");

    public ItemTemplate Item(ulong id) =>
        Tables.ItemTemplates.FirstOrDefault(t => t.Id.Value == id)
        ?? throw new InvalidDataException($"Item {id} is not seeded");

    public CreatureTemplate Creature(ulong id) =>
        Tables.CreatureTemplates.FirstOrDefault(t => t.Id.Value == id)
        ?? throw new InvalidDataException($"CreatureTemplate {id} is not seeded");

    public IReadOnlyList<CreatureTemplate> HostileOfRarity(CreatureRarity rarity) =>
        HostileTemplates.Where(t => t.Rarity == rarity).ToList();

    /// <summary>Experience a character of <paramref name="level" /> needs for the next level, or null past the table.</summary>
    public ulong? RequiredExperience(ushort level) =>
        Tables.CharacterLevelExperiences.FirstOrDefault(e => e.Level == level)?.Experience;
}
