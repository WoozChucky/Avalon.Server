using Avalon.Domain.World;
using Avalon.World.Creatures;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

public class CreatureStatDeriverShould
{
    private static readonly CreatureBaseStat[] BaseStats =
    [
        new() { Level = 1, Health = 40,  DamageMin = 3, DamageMax = 5,  Experience = 15 },
        new() { Level = 2, Health = 52,  DamageMin = 4, DamageMax = 7,  Experience = 25 },
        new() { Level = 5, Health = 106, DamageMin = 9, DamageMax = 14, Experience = 85 },
    ];

    private static readonly CreatureRarityModifier[] Rarities =
    [
        new() { Rarity = CreatureRarity.Normal, HealthMultiplier = 1.0f, DamageMultiplier = 1.0f, ExperienceMultiplier = 1.0f },
        new() { Rarity = CreatureRarity.Boss,   HealthMultiplier = 8.0f, DamageMultiplier = 2.0f, ExperienceMultiplier = 15.0f },
    ];

    private static CreatureStatDeriver NewDeriver() =>
        new(BaseStats, Rarities, NullLoggerFactory.Instance);

    private static ICreatureMetadata Template(
        CreatureRarity rarity = CreatureRarity.Normal,
        float health = 1f,
        float damage = 1f,
        float experience = 1f,
        uint? exp = null)
    {
        var template = Substitute.For<ICreatureMetadata>();
        template.Rarity.Returns(rarity);
        template.HealthModifier.Returns(health);
        template.DamageModifier.Returns(damage);
        template.ExperienceModifier.Returns(experience);
        template.Experience.Returns(exp);
        return template;
    }

    [Fact]
    public void Take_Base_Stats_Straight_Through_For_A_Plain_Normal_Creature()
    {
        DerivedCreatureStats stats = NewDeriver().Derive(Template(), level: 1);

        Assert.Equal(1, stats.Level);
        Assert.Equal(40u, stats.Health);
        Assert.Equal(3u, stats.DamageMin);
        Assert.Equal(5u, stats.DamageMax);
        Assert.Equal(15u, stats.Experience);
    }

    [Fact]
    public void Apply_The_Templates_Own_Modifiers()
    {
        DerivedCreatureStats stats = NewDeriver()
            .Derive(Template(health: 1.5f, damage: 2f, experience: 3f), level: 2);

        Assert.Equal(78u, stats.Health);       // 52 * 1.5
        Assert.Equal(8u, stats.DamageMin);     // 4 * 2
        Assert.Equal(14u, stats.DamageMax);    // 7 * 2
        Assert.Equal(75u, stats.Experience);   // 25 * 3
    }

    [Fact]
    public void Apply_The_Rarity_Multipliers_On_Top_Of_The_Template_Modifiers()
    {
        DerivedCreatureStats stats = NewDeriver()
            .Derive(Template(rarity: CreatureRarity.Boss, health: 1.5f), level: 5);

        Assert.Equal(1272u, stats.Health);     // 106 * 1.5 * 8.0
        Assert.Equal(18u, stats.DamageMin);    // 9 * 1.0 * 2.0
        Assert.Equal(1275u, stats.Experience); // 85 * 1.0 * 15.0
    }

    /// <summary>
    /// The reason <c>Experience</c> is nullable rather than using 0 as "unset": a creature deliberately
    /// worth nothing has to stay expressible.
    /// </summary>
    [Fact]
    public void Honour_An_Authored_Experience_Of_Zero_Rather_Than_Deriving()
    {
        DerivedCreatureStats stats = NewDeriver()
            .Derive(Template(rarity: CreatureRarity.Boss, experience: 99f, exp: 0u), level: 5);

        Assert.Equal(0u, stats.Experience);
    }

    [Fact]
    public void Let_An_Authored_Experience_Replace_The_Whole_Derivation()
    {
        DerivedCreatureStats stats = NewDeriver()
            .Derive(Template(rarity: CreatureRarity.Boss, experience: 99f, exp: 7u), level: 5);

        Assert.Equal(7u, stats.Experience);
    }

    /// <summary>
    /// A creature that fails to spawn breaks instance creation for everyone entering the map, so an
    /// uncovered level falls back to the highest seeded row rather than throwing.
    /// </summary>
    [Fact]
    public void Fall_Back_To_The_Highest_Seeded_Level_When_A_Level_Has_No_Row()
    {
        DerivedCreatureStats stats = NewDeriver().Derive(Template(), level: 9);

        Assert.Equal(106u, stats.Health);  // level 5 is the highest seeded row
        Assert.Equal(9, stats.Level);      // but the creature keeps the level it rolled
    }

    [Fact]
    public void Never_Produce_A_Damage_Range_Where_Min_Exceeds_Max()
    {
        DerivedCreatureStats stats = NewDeriver().Derive(Template(damage: 0.01f), level: 1);

        Assert.True(stats.DamageMin <= stats.DamageMax,
            $"min {stats.DamageMin} exceeded max {stats.DamageMax} after rounding");
    }

    [Fact]
    public void Never_Produce_Zero_Health()
    {
        DerivedCreatureStats stats = NewDeriver().Derive(Template(health: 0.001f), level: 1);

        Assert.True(stats.Health >= 1u, "a creature with 0 health is dead on arrival");
    }

    /// <summary>
    /// An empty base-stats table means every creature in the game would spawn with no stats at all, so
    /// it fails loudly at construction rather than silently producing zeroes forever.
    /// </summary>
    [Fact]
    public void Refuse_To_Be_Built_With_No_Base_Stats_At_All()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CreatureStatDeriver([], Rarities, NullLoggerFactory.Instance));
    }
}
