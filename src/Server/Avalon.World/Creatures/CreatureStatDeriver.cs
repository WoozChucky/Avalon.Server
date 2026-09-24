using Avalon.Domain.World;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Creatures;

/// <summary>Stats a creature spawns with, after level, template modifiers and rarity are applied.</summary>
public readonly record struct DerivedCreatureStats(
    ushort Level,
    uint Health,
    uint DamageMin,
    uint DamageMax,
    uint Experience);

/// <summary>
/// Turns a creature template plus a level into the stats it spawns with. Pure, and separate from
/// <c>CreatureSpawner</c> so a later effect that changes a creature's level — a shrine, an aura — can
/// re-derive without going through a spawn.
/// </summary>
public class CreatureStatDeriver
{
    private readonly IReadOnlyDictionary<ushort, CreatureBaseStat> _baseStats;
    private readonly IReadOnlyDictionary<CreatureRarity, CreatureRarityModifier> _rarities;
    private readonly CreatureBaseStat _highestSeeded;
    private readonly ILogger<CreatureStatDeriver> _logger;

    public CreatureStatDeriver(
        IEnumerable<CreatureBaseStat> baseStats,
        IEnumerable<CreatureRarityModifier> rarityModifiers,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CreatureStatDeriver>();
        _baseStats = baseStats.ToDictionary(stat => stat.Level);
        _rarities = rarityModifiers.ToDictionary(modifier => modifier.Rarity);

        if (_baseStats.Count == 0)
        {
            throw new InvalidOperationException(
                "CreatureBaseStats is empty; every creature would spawn with no stats at all.");
        }

        _highestSeeded = _baseStats.Values.MaxBy(stat => stat.Level)!;
    }

    public DerivedCreatureStats Derive(ICreatureMetadata template, ushort level)
    {
        CreatureBaseStat baseStat = ResolveBaseStat(template, level);
        CreatureRarityModifier rarity = ResolveRarity(template);

        uint health = Scale(baseStat.Health, template.HealthModifier, rarity.HealthMultiplier);
        uint damageMin = Scale(baseStat.DamageMin, template.DamageModifier, rarity.DamageMultiplier);
        uint damageMax = Scale(baseStat.DamageMax, template.DamageModifier, rarity.DamageMultiplier);

        // An authored value replaces the entire derivation, modifiers and rarity included: a written
        // number means that number. Null, not 0, is what means "derive" — see CreatureTemplate.
        uint experience = template.Experience
                          ?? Scale(baseStat.Experience, template.ExperienceModifier, rarity.ExperienceMultiplier);

        // Both floors exist for the same reason: a creature with 0 health is dead on arrival, and one
        // with 0 damage can never kill anything, so a fight against it silently never ends. Neither is
        // reachable from the seeded data — they come from a small-but-positive modifier rounding down —
        // which is exactly why they would be hard to notice.
        uint flooredMin = Math.Max(1u, damageMin);

        return new DerivedCreatureStats(
            level,
            Math.Max(1u, health),
            flooredMin,
            Math.Max(flooredMin, damageMax),
            experience);
    }

    private CreatureBaseStat ResolveBaseStat(ICreatureMetadata template, ushort level)
    {
        if (_baseStats.TryGetValue(level, out CreatureBaseStat? stat))
        {
            return stat;
        }

        // Spawning is inside instance construction, so throwing here would stop every player entering
        // the map. A creature that is slightly wrong beats a map nobody can load.
        _logger.LogWarning(
            "No CreatureBaseStats row for level {Level} (creature template {TemplateId}); falling back to level {Fallback}",
            level, template.Id, _highestSeeded.Level);

        return _highestSeeded;
    }

    private CreatureRarityModifier ResolveRarity(ICreatureMetadata template)
    {
        if (_rarities.TryGetValue(template.Rarity, out CreatureRarityModifier? modifier))
        {
            return modifier;
        }

        _logger.LogWarning(
            "No CreatureRarityModifiers row for {Rarity} (creature template {TemplateId}); treating it as unscaled",
            template.Rarity, template.Id);

        return new CreatureRarityModifier
        {
            Rarity = template.Rarity,
            HealthMultiplier = 1f,
            DamageMultiplier = 1f,
            ExperienceMultiplier = 1f
        };
    }

    private static uint Scale(uint value, float templateModifier, float rarityMultiplier)
    {
        // A modifier of 0 in seed data would otherwise silently zero the stat; treat it as unset.
        float template = templateModifier <= 0f ? 1f : templateModifier;
        float rarity = rarityMultiplier <= 0f ? 1f : rarityMultiplier;

        return (uint)Math.Round(value * template * rarity, MidpointRounding.AwayFromZero);
    }
}
