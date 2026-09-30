using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Balance.Data;

/// <summary>
/// The seed tables after overrides, validated by the server's own validators, so a row a /reload would refuse stops
/// the run naming it. Read-only once built.
/// </summary>
public sealed class BalanceData
{
    private BalanceData(SeedTables tables, AbilityCatalog abilities, CombatPatch combat, CreatureStatDeriver creatureStats)
    {
        Tables = tables;
        Abilities = abilities;
        Combat = combat;
        CreatureStats = creatureStats;
        HostileTemplates = tables.CreatureTemplates
            .Where(t => !t.Invulnerable && CreatureKits.For(t.ScriptName) is not null)
            .OrderBy(t => t.Id.Value)
            .ToList();
    }

    public SeedTables Tables { get; }
    public AbilityCatalog Abilities { get; }
    public CombatPatch Combat { get; }
    public CreatureStatDeriver CreatureStats { get; }

    /// <summary>Templates that fight: not invulnerable, and their script declares a kit.</summary>
    public IReadOnlyList<CreatureTemplate> HostileTemplates { get; }

    /// <exception cref="InvalidDataException">A row the server would refuse; the message names it.</exception>
    public static BalanceData From(SeedTables tables)
    {
        var abilities = new AbilityCatalog(tables.AbilityTemplates, NullLoggerFactory.Instance);
        if (abilities.Refused.Count > 0)
            throw new InvalidDataException("The ability catalog refused: " + string.Join("; ", abilities.Refused));

        CombatPatch combat = CombatPatch.Build(tables.CombatFormulas, tables.ClassStatFactors);
        CreaturesPatch.Validate(tables.CreatureTemplates);
        var creatureStats = new CreatureStatDeriver(tables.CreatureBaseStats, tables.CreatureRarityModifiers,
            NullLoggerFactory.Instance);

        return new BalanceData(tables, abilities, combat, creatureStats);
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
            .Select(id => Abilities.TryGet(id, out AbilityTemplate? t)
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
