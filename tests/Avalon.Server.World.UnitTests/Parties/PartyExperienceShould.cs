using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyExperienceShould
{
    private static ICharacter At(ushort level)
    {
        ICharacter member = Substitute.For<ICharacter>();
        member.Level.Returns(level);
        return member;
    }

    /// <summary>
    /// The split with a bonus of 0.10 per extra member and a level gap of 5. A member can gain experience only below
    /// <paramref name="maxLevel" /> (null: always), and one at it is left out of n, the bonus and the weighting, as one
    /// the gap or more above the creature is (#735).
    /// </summary>
    [Theory]
    [InlineData(100u, (ushort)16, PartyExperienceMode.LevelWeighted, null, new ushort[] { 10, 10, 20 }, new uint[] { 30, 30, 60 })] // 120 x L / 40
    [InlineData(66u, (ushort)10, PartyExperienceMode.Even, null, new ushort[] { 10, 10 }, new uint[] { 36, 36 })] // 66 x 1.1 / 2 = 36.3, floored
    [InlineData(100u, (ushort)10, PartyExperienceMode.Even, null, new ushort[] { 14, 15 }, new uint[] { 100 })] // 15 >= 10 + 5 gets nothing and is not counted: 14 alone, no bonus
    [InlineData(100u, (ushort)10, PartyExperienceMode.Even, null, new ushort[] { 15 }, new uint[0])] // the gap applies solo too
    [InlineData(100u, (ushort)18, PartyExperienceMode.Even, (ushort)20, new ushort[] { 10, 20, 12 }, new uint[] { 55, 55 })] // 100 x 1.1 / 2
    [InlineData(100u, (ushort)18, PartyExperienceMode.LevelWeighted, (ushort)20, new ushort[] { 10, 20, 12 }, new uint[] { 50, 60 })] // 110 x L / 22
    public void Share_the_experience_among_the_members_who_count(uint experience, ushort creatureLevel,
        PartyExperienceMode mode, ushort? maxLevel, ushort[] levels, uint[] expected) =>
        Assert.Equal(expected, PartyExperience.Split(experience, creatureLevel, levels.Select(At).ToList(), mode,
                bonusPerExtra: 0.10f, levelGap: 5, canGainExperience: maxLevel is { } max ? member => member.Level < max : null)
            .Select(s => s.Experience));
}
