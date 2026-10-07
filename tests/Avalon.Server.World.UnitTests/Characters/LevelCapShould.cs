using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Public.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #735: the maximum level is the highest level with a CharacterLevelExperiences row. A character there gains no
/// experience from any source; an award that levels a character into it stops there and the rest is discarded, so it
/// enters the maximum level with none. Nothing is logged for simply being at the cap.
/// </summary>
public class LevelCapShould
{
    private static readonly CharacterLevelExperience[] Levels =
    [
        new() { Level = 1, Experience = 100 },
        new() { Level = 2, Experience = 200 },
        new() { Level = 3, Experience = 300 },
    ];

    /// <summary>Not stubbed with Returns: the substitute remembers what Grant sets, so the level it reads back rises.</summary>
    private static ICharacter Character(ushort level, ulong experience)
    {
        ICharacter character = Substitute.For<ICharacter>();
        character.Level = level;
        character.Experience = experience;
        return character;
    }

    private static List<CharacterLevelExperience> SeededLevels()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        return context.CharacterLevelExperiences.AsNoTracking().ToList();
    }

    [Fact]
    public async Task Let_no_character_gain_experience_at_the_highest_level_with_a_row()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);

        Assert.True(ExperienceAward.CanGainExperience(Character(2, 0), data));
        Assert.False(ExperienceAward.CanGainExperience(Character(3, 0), data));
        Assert.False(ExperienceAward.CanGainExperience(Character(1, 0), await TestStaticData.LoadAsync(levels: [])));
    }

    [Fact]
    public async Task Stop_at_the_maximum_level_with_no_experience_when_one_award_crosses_several_levels()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);
        ICharacter character = Character(1, 0);

        ExperienceAward.Grant(character, 10_000, data, parties: null, new TestLog());

        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(0ul, character.Experience);
        Assert.Equal(300ul, character.RequiredExperience);
    }

    [Fact]
    public async Task Discard_the_rest_of_an_award_that_levels_into_the_maximum_level()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);
        ICharacter character = Character(2, 150);

        ExperienceAward.Grant(character, 100, data, parties: null, new TestLog());   // 250 covers 200; 50 left over

        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(0ul, character.Experience);
    }

    [Theory]
    [InlineData(0ul)]
    [InlineData(250ul)]
    [InlineData(300ul)]
    public async Task Gain_nothing_and_log_nothing_at_the_maximum_level(ulong experience)
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);
        ICharacter character = Character(3, experience);
        var log = new TestLog();

        ExperienceAward.Grant(character, 5_000, data, parties: null, log);

        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(experience, character.Experience);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }

    /// <summary>Unchanged: a level with no row at all (none exists today; the branch is kept as it was) still warns and gets nothing.</summary>
    [Fact]
    public async Task Still_warn_and_award_nothing_at_a_level_with_no_row()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);
        ICharacter character = Character(4, 0);
        var log = new TestLog();

        ExperienceAward.Grant(character, 50, data, parties: null, log);

        Assert.Equal((ushort)4, character.Level);
        Assert.Equal(0ul, character.Experience);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
    }

    /// <summary>The shipped seed: a character two levels below its highest row, handed far more than it needs.</summary>
    [Fact]
    public async Task Cap_the_seeded_table_at_its_highest_level()
    {
        List<CharacterLevelExperience> seeded = SeededLevels();
        ushort max = seeded.Max(l => l.Level);
        StaticData data = await TestStaticData.LoadAsync(levels: seeded);
        ICharacter character = Character((ushort)(max - 2), 0);

        ExperienceAward.Grant(character, 1_000_000, data, parties: null, new TestLog());

        Assert.False(ExperienceAward.CanGainExperience(character, data));
        Assert.Equal(max, character.Level);
        Assert.Equal(0ul, character.Experience);
        Assert.Equal(seeded.Single(l => l.Level == max).Experience, character.RequiredExperience);
    }

    /// <summary>The shipped seed: one level below the highest, an award that crosses into it loses what is left over.</summary>
    [Fact]
    public async Task Discard_the_rest_of_an_award_that_crosses_into_the_seeded_maximum()
    {
        List<CharacterLevelExperience> seeded = SeededLevels();
        ushort max = seeded.Max(l => l.Level);
        ulong below = seeded.Single(l => l.Level == max - 1).Experience;
        StaticData data = await TestStaticData.LoadAsync(levels: seeded);
        ICharacter character = Character((ushort)(max - 1), below - 300);

        ExperienceAward.Grant(character, 1_000, data, parties: null, new TestLog());   // 700 past the requirement

        Assert.Equal(max, character.Level);
        Assert.Equal(0ul, character.Experience);
    }
}
