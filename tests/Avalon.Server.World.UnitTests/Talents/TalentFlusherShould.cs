using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Talents;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>
/// #1181: the talent window's state follows every change of what it shows, once, from the tick's flush: a level-up,
/// whatever granted it, an accepted allocation and a turn-in of a quest that pays a talent point; the repeatable Boar
/// Cull pays none and changes nothing the window shows. An allocation that changes the abilities held is followed by the
/// ability list, once, ahead of the state. Over the seed, for a level-10 Warrior with no talents, whose state
/// was sent at select.
/// </summary>
public class TalentFlusherShould
{
    private const uint AlphasHowl = 3, BoarCull = 10, HeavyStrike = 319, Cleave = 200;

    [Theory]
    [InlineData("nothing", false, 0)]
    [InlineData("level-up", true, 0)]
    [InlineData("allocation", true, 0)]
    [InlineData("ability allocation", true, 1)]
    [InlineData("paying turn-in", true, 0)]
    [InlineData("repeatable turn-in", false, 0)]
    public void Send_the_state_once_after_what_changes_it(string change, bool sent, int abilityLists)
    {
        var w = QuestTestWorld.OverSeed();
        QuestClient c = w.Join(id: 118_104, level: 10);
        CharacterEntity character = c.Character;
        TalentTestCharacters.Enter(character, w.Data);
        (uint earned, uint spent) expected = (10, 0);

        switch (change)
        {
            case "level-up":
                ExperienceAward.Grant(character, 7_600, w.Data, null, NullLogger.Instance);
                Assert.Equal(11, character.Level);
                expected = (11, 0);
                break;
            case "allocation":
                new TalentAllocateHandler(NullLogger<TalentAllocateHandler>.Instance, w.World)
                    .Execute(c.Connection, new CTalentAllocatePacket { RequestId = 1, NodeIds = [2] });
                expected = (10, 1);
                break;
            case "ability allocation":
                new TalentAllocateHandler(NullLogger<TalentAllocateHandler>.Instance, w.World)
                    .Execute(c.Connection, new CTalentAllocatePacket { RequestId = 1, NodeIds = [2, 3, 4, 5, 6] });
                expected = (10, 5);
                break;
            case "paying turn-in":
                Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, character, AlphasHowl,
                    w.ReadyAtSeed(c, AlphasHowl).Guid.RawValue, choiceItemTemplateId: 32));
                expected = (11, 0);
                break;
            case "repeatable turn-in":
                Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, character, BoarCull,
                    w.ReadyAtSeed(c, BoarCull).Guid.RawValue));
                Assert.Equal(10, character.Level);
                break;
        }

        c.Clear();
        TalentFlusher.Flush(c.Connection, w.Data.Quests);
        TalentFlusher.Flush(c.Connection, w.Data.Quests);

        List<STalentStatePacket> states = c.Read<STalentStatePacket>(NetworkPacketType.SMSG_TALENT_STATE);
        Assert.Equal(sent ? 1 : 0, states.Count);
        if (sent)
            Assert.Equal(expected, (states[0].PointsEarned, states[0].PointsSpent));
        Assert.Equal((sent ? 1 : 0) + abilityLists, c.Sent.Count);

        // The ability list, once, before the state: the starter, then Cleave at node 6.
        List<SCharacterAbilitiesPacket> lists = c.Read<SCharacterAbilitiesPacket>(NetworkPacketType.SMSG_CHARACTER_ABILITIES);
        Assert.Equal(abilityLists, lists.Count);
        if (abilityLists > 0)
        {
            Assert.Equal([HeavyStrike, Cleave], lists[0].Abilities.Select(a => a.AbilityId));
            Assert.Equal(NetworkPacketType.SMSG_CHARACTER_ABILITIES, c.Sent[0].Header.Type);
        }
    }
}
