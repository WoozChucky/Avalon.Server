using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyExperienceShould
{
    private static ICharacter At(ushort level)
    {
        ICharacter member = Substitute.For<ICharacter>();
        member.Level.Returns(level);
        return member;
    }

    private static uint[] Split(uint xp, ushort creatureLevel, PartyExperienceMode mode, params ushort[] levels) =>
        PartyExperience.Split(xp, creatureLevel, levels.Select(At).ToList(), mode, bonusPerExtra: 0.10f, levelGap: 5)
            .Select(s => s.Experience).ToArray();

    [Fact]
    public void Give_one_member_everything_with_no_bonus() =>
        Assert.Equal([100u], Split(100, 10, PartyExperienceMode.Even, 10));

    [Fact]
    public void Share_evenly_with_the_party_bonus() =>
        Assert.Equal([40u, 40u, 40u], Split(100, 16, PartyExperienceMode.Even, 10, 10, 20)); // 100 × 1.2 / 3

    [Fact]
    public void Share_by_level_with_the_party_bonus() =>
        Assert.Equal([30u, 30u, 60u], Split(100, 16, PartyExperienceMode.LevelWeighted, 10, 10, 20)); // 120 × L / 40

    [Fact]
    public void Floor_each_share() =>
        Assert.Equal([36u, 36u], Split(66, 10, PartyExperienceMode.Even, 10, 10)); // 66 × 1.1 / 2 = 36.3

    [Fact]
    public void Leave_out_and_not_count_a_member_the_level_gap_or_more_above()
    {
        uint[] shares = Split(100, 10, PartyExperienceMode.Even, 14, 15);

        Assert.Equal([100u], shares); // 15 >= 10 + 5 gets nothing; 14 is alone, so no bonus
    }

    [Fact]
    public void Give_a_solo_character_the_gap_or_more_above_nothing() =>
        Assert.Empty(Split(100, 10, PartyExperienceMode.Even, 15));
}
