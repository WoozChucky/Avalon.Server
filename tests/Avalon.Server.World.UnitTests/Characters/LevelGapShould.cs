using Avalon.Domain.World;
using Avalon.Network.Packets.Party;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #735, owner decision: a level missing below the highest CharacterLevelExperiences row is treated exactly like the
/// maximum level. A character can gain experience only while its level and the next both have a row
/// (<see cref="ExperienceAward.CanGainExperience"/>, the one rule the award and the party split share): an award that
/// levels a character to the level before a gap stops there with 0, a character on that level gains nothing and is
/// left out of the party split, and nothing is logged for either.
/// </summary>
public class LevelGapShould
{
    /// <summary>Level 4 is missing: 3 is a dead end below the gap, 6 the highest row.</summary>
    private static readonly CharacterLevelExperience[] s_gappedLevels =
    [
        new() { Level = 1, Experience = 100 },
        new() { Level = 2, Experience = 200 },
        new() { Level = 3, Experience = 300 },
        new() { Level = 5, Experience = 500 },
        new() { Level = 6, Experience = 600 },
    ];

    /// <summary>Not stubbed with Returns: the substitute remembers what Grant sets, so the level it reads back rises.</summary>
    private static ICharacter Character(ushort level, ulong experience)
    {
        ICharacter character = Substitute.For<ICharacter>();
        character.Level = level;
        character.Experience = experience;
        return character;
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]   // the next level has no row: the gap
    [InlineData(4, false)]   // no row of its own
    [InlineData(5, true)]
    [InlineData(6, false)]   // the highest row: the maximum level
    public async Task Let_a_character_gain_experience_only_while_its_level_and_the_next_have_rows(int level, bool expected)
    {
        StaticData data = await TestStaticData.LoadAsync(levels: s_gappedLevels);

        Assert.Equal(expected, ExperienceAward.CanGainExperience(Character((ushort)level, 0), data));
    }

    [Fact]
    public async Task Stop_before_the_gap_with_no_experience_when_an_award_crosses_into_it()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: s_gappedLevels);
        ICharacter character = Character(2, 150);
        var log = new TestLog();

        ExperienceAward.Grant(character, 10_000, data, parties: null, log);

        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(0ul, character.Experience);
        Assert.Equal(300ul, character.RequiredExperience);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(0ul)]
    [InlineData(120ul)]
    public async Task Give_a_character_sitting_below_the_gap_nothing_and_log_nothing(ulong experience)
    {
        StaticData data = await TestStaticData.LoadAsync(levels: s_gappedLevels);
        ICharacter character = Character(3, experience);
        var log = new TestLog();

        ExperienceAward.Grant(character, 500, data, parties: null, log);

        Assert.Equal((ushort)3, character.Level);
        Assert.Equal(experience, character.Experience);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Still_level_and_carry_as_usual_below_the_level_before_the_gap()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: s_gappedLevels);
        ICharacter character = Character(1, 0);

        ExperienceAward.Grant(character, 150, data, parties: null, new TestLog());

        Assert.Equal((ushort)2, character.Level);
        Assert.Equal(50ul, character.Experience);
    }

    [Fact]
    public async Task Leave_a_party_member_below_the_gap_out_of_the_split()
    {
        StaticData data = await TestStaticData.LoadAsync(levels: s_gappedLevels);
        ICharacter belowGap = Character(3, 0);
        ICharacter other = Character(2, 0);

        IReadOnlyList<ExperienceShare> shares = PartyExperience.Split(100, creatureLevel: 3, [belowGap, other],
            PartyExperienceMode.Even, bonusPerExtra: 0.10f, levelGap: 5,
            canGainExperience: member => ExperienceAward.CanGainExperience(member, data));

        ExperienceShare share = Assert.Single(shares);
        Assert.Same(other, share.Member);
        Assert.Equal(100u, share.Experience);   // alone once the other is left out: the whole share, no bonus
    }
}
