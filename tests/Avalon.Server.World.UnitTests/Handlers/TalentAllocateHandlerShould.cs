using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Talents;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Talents;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_TALENT_ALLOCATE (#1181) through the real handler, over the seeded tree, for a level-10 Warrior in a real instance
/// holding the spoke's first five nodes (57-61, Hurled Axe at 61), so 5 of its 10 points are left, with Hurled Axe cooling
/// down: every request gets one SMSG_TALENT_RESULT; an accepted one holds its nodes, marks each for the save, refreshes the
/// stats, sends the ability list only when the abilities held changed (a held ability keeps its running cooldown, a new
/// one arrives ready) and owes the talent state; a refusal changes nothing. Character id 118_101 is this class's.
/// </summary>
public class TalentAllocateHandlerShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly AbilityId s_heavyStrike = new(319), s_cleave = new(200), s_hurledAxe = new(202);
    private static readonly int[] s_spoke = [57, 58, 59, 60, 61];

    private const uint Me = 118_101, RequestId = 23;

    [Theory]
    [InlineData("small nodes")]
    [InlineData("ability node")]
    [InlineData("refused")]
    [InlineData("no character")]
    public void Apply_an_accepted_allocation_and_change_nothing_on_a_refusal(string row)
    {
        var world = new QuietWorld(s_data);
        using MapInstance instance = TestMapInstances.Build(world);
        CharacterEntity character = TestCharacters.New(Me);
        character.Level = 10;
        MapInstanceClient client = Join(instance, character);
        TalentTestCharacters.Enter(character, s_data, s_spoke);
        IAbility hurledAxe = character.Spells[s_hurledAxe]!;
        hurledAxe.CooldownTimer = 2f;
        IAbility[] abilitiesBefore = [.. character.Spells.All];
        DerivedCharacterStats statsBefore = character.Stats!.Value;
        client.Sent.Clear();
        var handler = new TalentAllocateHandler(NullLogger<TalentAllocateHandler>.Instance, world);

        if (row == "no character")
        {
            IWorldConnection nobody = Substitute.For<IWorldConnection>();
            nobody.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null);
            handler.Execute(nobody, Request(2));
            nobody.DidNotReceive().Send(Arg.Any<OutboundPacket>());
            return;
        }

        uint[] request = row switch
        {
            "small nodes" => [2, 3],
            "ability node" => [2, 3, 4, 5, 6],
            _ => [2, 3, 2],
        };
        handler.Execute(client.Connection, Request(request));

        STalentResultPacket result = Assert.Single(client.Read<STalentResultPacket>(NetworkPacketType.SMSG_TALENT_RESULT));
        List<SCharacterAbilitiesPacket> abilityLists =
            client.Read<SCharacterAbilitiesPacket>(NetworkPacketType.SMSG_CHARACTER_ABILITIES);
        SaveMarks saved = character.SaveState.TakeMarks();
        IReadOnlyDictionary<int, SaveMark>? marks = saved.Talents;
        Assert.Equal(RequestId, result.RequestId);

        if (row == "refused")
        {
            Assert.Equal((TalentResult.AlreadyAllocated, 2u), (result.Result, result.NodeId));
            Assert.Equal(s_spoke, character.Talents.Held);
            Assert.Null(marks);
            Assert.Null(saved.StatsVersion);
            Assert.Equal(statsBefore.Strength, character.Stats!.Value.Strength);
            Assert.Equal(abilitiesBefore, character.Spells.All);
            Assert.False(character.Talents.StateOwed);
            Assert.Single(client.Sent);
            return;
        }

        Assert.Equal((TalentResult.Ok, 0u), (result.Result, result.NodeId));
        Assert.Equal([.. request.Select(n => (int)n).Order()], marks!.Keys.Order());
        Assert.Equal([.. request.Select(n => (int)n).Concat(s_spoke).Order()], character.Talents.Held);
        Assert.True(character.Talents.StateOwed);

        if (row == "small nodes")
        {
            DerivedCharacterStats stats = character.Stats!.Value;
            Assert.Equal(statsBefore.Strength + 5, stats.Strength);
            Assert.Equal(statsBefore.Offence.Increased(OffenceStat.Physical) + 5,
                stats.Offence.Increased(OffenceStat.Physical));
            Assert.Empty(abilityLists);
            Assert.Equal(abilitiesBefore, character.Spells.All);
            return;
        }

        // The starter, then each held ability node's by ascending node id: Cleave at 6, Hurled Axe at 61.
        SCharacterAbilitiesPacket sent = Assert.Single(abilityLists);
        Assert.Equal([s_heavyStrike.Value, s_cleave.Value, s_hurledAxe.Value], sent.Abilities.Select(a => a.AbilityId));
        Assert.Same(hurledAxe, character.Spells[s_hurledAxe]);
        Assert.Equal(2f, hurledAxe.CooldownTimer);
        Assert.Equal(0f, character.Spells[s_cleave]!.CooldownTimer);
    }

    private static CTalentAllocatePacket Request(params uint[] nodeIds) => new() { RequestId = RequestId, NodeIds = [.. nodeIds] };
}
