using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Balance.Core;

/// <summary>The combat formula and every class's stat factors, as the world server's own checks accept them.</summary>
public sealed record CombatData(CombatFormula Formula, IReadOnlyDictionary<CharacterClass, ClassStatFactors> Factors);

/// <summary>
/// The seed tables after overrides, validated by the server's own validators, so a row a /reload would refuse stops
/// the run naming it. Read-only once built.
/// </summary>
public sealed class BalanceData
{
    private BalanceData(SeedTables tables, IReadOnlyDictionary<AbilityId, AbilityTemplate> abilities,
        IReadOnlyList<string> refusedAbilities, CombatData combat, CreatureStatDeriver creatureStats)
    {
        Tables = tables;
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
        var abilities = new Dictionary<AbilityId, AbilityTemplate>();
        List<string> refused = [];
        foreach (AbilityTemplate template in tables.AbilityTemplates.OrderBy(t => t.Id.Value))
        {
            if (AbilityRules.Problem(template) is { } reason)
            {
                refused.Add($"ability {template.Id.Value} '{template.Name}': {reason}");
                continue;
            }

            abilities[template.Id] = template;
        }

        if (refused.Count > 0)
            throw new InvalidDataException("The ability catalog refused: " + string.Join("; ", refused));

        (CombatFormula formula, IReadOnlyDictionary<CharacterClass, ClassStatFactors> factors) =
            CombatDataRules.Build(tables.CombatFormulas, tables.ClassStatFactors);
        CreatureTemplateRules.Validate(tables.CreatureTemplates);
        var creatureStats = new CreatureStatDeriver(tables.CreatureBaseStats, tables.CreatureRarityModifiers,
            NullLoggerFactory.Instance);

        return new BalanceData(tables, abilities, refused, new CombatData(formula, factors), creatureStats);
    }

    public DerivedCharacterStats CharacterStats(CharacterClass characterClass, ushort level, IEnumerable<ItemTemplate> worn)
    {
        ClassLevelStat row = Tables.ClassLevelStats.FirstOrDefault(r => r.Class == characterClass && r.Level == level)
            ?? throw new InvalidDataException($"ClassLevelStat {characterClass} level {level} is not seeded");
        return CharacterStatsCalculator.Calculate(row, worn.ToList(), Combat.Factors[characterClass]);
    }

    /// <summary>The class's starting abilities (CharacterCreateInfos.StartingSpells), as the catalog holds them.</summary>
    public IReadOnlyList<AbilityTemplate> KitOf(CharacterClass characterClass)
    {
        CharacterCreateInfo info = Tables.CharacterCreateInfos.FirstOrDefault(i => i.Class == characterClass)
            ?? throw new InvalidDataException($"CharacterCreateInfo {characterClass} is not seeded");
        return info.StartingSpells
            .Select(id => Abilities.TryGetValue(id, out AbilityTemplate? t)
                ? t
                : throw new InvalidDataException($"{characterClass} starts with ability {id.Value}, which the catalog does not hold"))
            .ToList();
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
