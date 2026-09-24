using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

public class CreatureSpawnerShould
{
    /// <summary>
    /// The single assertion that would have caught the bug this whole change exists to fix: every
    /// creature used to spawn as level 1 with 100 health whatever its template said.
    /// </summary>
    [Fact]
    public void Give_A_Spawned_Creature_Its_Templates_Stats_Rather_Than_Hardcoded_Ones()
    {
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(42),
            Name = "Mother Bramble",
            MinLevel = 5,
            MaxLevel = 5,
            Rarity = CreatureRarity.Boss,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f,
            SpeedWalk = 2f,
            SpeedRun = 4f
        };

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.Equal(5, creature.Level);
        Assert.NotEqual(100u, creature.Health);
        Assert.Equal(848u, creature.Health);     // base 106 * 8.0 boss
        Assert.Equal(18u, creature.DamageMin);   // base 9 * 2.0
        Assert.Equal(28u, creature.DamageMax);   // base 14 * 2.0
        Assert.Equal(creature.Health, creature.CurrentHealth);
    }

    [Fact]
    public void Roll_A_Level_Inside_The_Templates_Range()
    {
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(43),
            Name = "Grey Fen Wolf",
            MinLevel = 2,
            MaxLevel = 5,
            Rarity = CreatureRarity.Normal,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };

        CreatureSpawner spawner = SpawnerOver(template);

        for (int i = 0; i < 200; i++)
        {
            ICreature creature = spawner.Spawn(template.Id);
            Assert.InRange(creature.Level, (ushort)2, (ushort)5);
        }
    }

    [Fact]
    public void Carry_The_Templates_Invulnerable_Flag_Onto_The_Spawned_Creature()
    {
        // The flag is what makes a town NPC unkillable, and CombatService reads it off the creature
        // rather than the template. If the spawner drops it, every NPC is killable and the guard in
        // ApplyDamageCore never fires.
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(44),
            Name = "Innkeeper",
            MinLevel = 2,
            MaxLevel = 2,
            Rarity = CreatureRarity.Normal,
            Invulnerable = true,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.True(creature.Invulnerable);
    }

    [Fact]
    public void Leave_A_Creature_Vulnerable_When_Its_Template_Says_Nothing()
    {
        // The default has to stay false, or adding the column would silently make every monster
        // in the game unkillable.
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(45),
            Name = "Thornback Boar",
            MinLevel = 2,
            MaxLevel = 2,
            Rarity = CreatureRarity.Normal,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.False(creature.Invulnerable);
    }

    /// <summary>
    /// <c>CreatureSpawner.LoadAsync</c> pulls templates from the repository into its own field, so the
    /// substitute returns the one template under test and the spawner is loaded before use. The base
    /// stats and rarity rows are the real seeded values for the levels these tests touch.
    /// </summary>
    private static CreatureSpawner SpawnerOver(CreatureTemplate template)
    {
        var repository = Substitute.For<ICreatureTemplateRepository>();
        repository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate> { template }));

        CreatureBaseStat[] baseStats =
        [
            new() { Level = 2, Health = 52,  DamageMin = 4, DamageMax = 7,  Experience = 25 },
            new() { Level = 3, Health = 66,  DamageMin = 5, DamageMax = 9,  Experience = 40 },
            new() { Level = 4, Health = 84,  DamageMin = 7, DamageMax = 11, Experience = 60 },
            new() { Level = 5, Health = 106, DamageMin = 9, DamageMax = 14, Experience = 85 },
        ];

        CreatureRarityModifier[] rarities =
        [
            new() { Rarity = CreatureRarity.Normal, HealthMultiplier = 1.0f, DamageMultiplier = 1.0f, ExperienceMultiplier = 1.0f },
            new() { Rarity = CreatureRarity.Boss,   HealthMultiplier = 8.0f, DamageMultiplier = 2.0f, ExperienceMultiplier = 15.0f },
        ];

        var deriver = new CreatureStatDeriver(baseStats, rarities, NullLoggerFactory.Instance);
        var spawner = new CreatureSpawner(
            NullLoggerFactory.Instance, repository, new Lazy<CreatureStatDeriver>(deriver));

        spawner.LoadAsync().GetAwaiter().GetResult();
        return spawner;
    }
}
