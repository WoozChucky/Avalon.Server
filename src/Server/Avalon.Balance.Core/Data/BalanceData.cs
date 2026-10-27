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
    private BalanceData(SeedTables tables, IReadOnlyDictionary<AuraId, AuraTemplate> auras,
        IReadOnlyDictionary<AbilityId, AbilityTemplate> abilities, IReadOnlyList<string> refusedAbilities, CombatData combat,
        CreatureStatDeriver creatureStats)
    {
        Tables = tables;
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
        CreatureTemplateRules.Validate(tables.CreatureTemplates);
        CreatureTemplateRules.ValidateRarities(tables.CreatureRarityModifiers);
        var creatureStats = new CreatureStatDeriver(tables.CreatureBaseStats, tables.CreatureRarityModifiers,
            NullLoggerFactory.Instance);

        return new BalanceData(tables, auras, abilities, refused, new CombatData(formula), creatureStats);
    }

    /// <param name="auras">What the character's auras add, folded in as the server's stats refresh folds them; none when null.</param>
    public DerivedCharacterStats CharacterStats(CharacterClass characterClass, ushort level, IEnumerable<ItemTemplate> worn,
        AuraStatTotals? auras = null)
    {
        ClassLevelStat row = Tables.ClassLevelStats.FirstOrDefault(r => r.Class == characterClass && r.Level == level)
            ?? throw new InvalidDataException($"ClassLevelStat {characterClass} level {level} is not seeded");
        // The simulator is paused (#1183): its gear is templates alone, each a copy with no base stats and no modifiers.
        List<WornItem> items = [.. worn.Select(t => new WornItem(t, default, null, t.DamageType))];
        return CharacterStatsCalculator.Calculate(row, items, Combat.Formula, auras);
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
