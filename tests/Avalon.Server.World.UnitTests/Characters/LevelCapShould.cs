using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Public.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #735: the maximum level is the highest level with a CharacterLevelExperiences row. A character there keeps earning
/// up to that level's threshold and no further; it never reaches a level with no row, and nothing is logged for simply
/// being at the cap.
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

    [Fact]
    public async Task Stop_at_the_last_levels_threshold_when_one_award_crosses_the_cap()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);
        ICharacter character = Character(1, 0);

        ExperienceAward.Grant(character, 10_000, data, parties: null, new TestLog());

        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(300ul, character.Experience);
        Assert.Equal(300ul, character.RequiredExperience);
    }

    [Fact]
    public async Task Keep_earning_at_the_cap_up_to_the_threshold()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);
        ICharacter character = Character(3, 250);

        ExperienceAward.Grant(character, 30, data, parties: null, new TestLog());
        Assert.Equal(280ul, character.Experience);

        ExperienceAward.Grant(character, 100, data, parties: null, new TestLog());
        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(300ul, character.Experience);
    }

    [Fact]
    public async Task Add_nothing_and_log_nothing_once_at_the_threshold()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: Levels);
        ICharacter character = Character(3, 300);
        var log = new TestLog();

        ExperienceAward.Grant(character, 5_000, data, parties: null, log);

        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(300ul, character.Experience);
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

    [Fact]
    public async Task Cap_the_seeded_table_at_level_fifteen()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<CharacterLevelExperience> seeded = context.CharacterLevelExperiences.AsNoTracking().ToList();
        StaticData data = await TestStaticData.LoadAsync(levels: seeded);
        ICharacter character = Character(13, 0);

        ExperienceAward.Grant(character, 1_000_000, data, parties: null, new TestLog());

        Assert.Equal((ushort)15, character.Level);
        Assert.Equal(13_600ul, character.Experience);
        Assert.Equal(13_600ul, character.RequiredExperience);
    }
}
