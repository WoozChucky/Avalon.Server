using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Talents;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Talents;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Persistence;
using Avalon.World.Public.Abilities;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_TALENT_REFUND at the Lectern of Paths (#1181 Part C), through the real handler, a real instance with the seeded
/// Lectern placed in it and the seeded Workbench beside it, the seeded tree, and the economy's own wallet: every request
/// gets one SMSG_TALENT_RESULT, a refusal names the first rule it breaks and changes nothing (no node, no gold, no save
/// mark, nothing owed); an accepted refund drops its nodes, pays its price in the same save as its rows, and is followed
/// at the tick's flush by the ability list and the talent state with the price at the character's level. The character
/// is a Warrior 4 m from the Lectern holding its lane's first five nodes (2-6, Cleave at 6). Character id 118_201 is
/// this class's.
/// </summary>
public class TalentRefundHandlerShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly DoodadTemplateView s_lectern = s_data.Doodads.Templates.Single(t => t.Service == DoodadService.Lectern);
    private static readonly DoodadTemplateView s_workbench =
        s_data.Doodads.Templates.Single(t => t.Service == DoodadService.Workbench);
    private static readonly AbilityId s_heavyStrike = new(319), s_cleave = new(200);
    private static readonly int[] s_lane = [2, 3, 4, 5, 6];

    private const uint Me = 118_201, RequestId = 37;

    // The rules in the order TalentRefundRules asks them. Every row breaks its own rule and every one after it, so it
    // shows each earlier rule is asked first.
    private static readonly TalentResult[] s_order =
    [
        TalentResult.Dead, TalentResult.Stunned, TalentResult.NotFound, TalentResult.TooFar, TalentResult.Casting,
        TalentResult.UnknownNode, TalentResult.NotAllocated, TalentResult.WouldDisconnect, TalentResult.NotEnoughGold,
    ];

    [Theory]
    [InlineData("dead", TalentResult.Dead, 0u)]
    [InlineData("stunned", TalentResult.Stunned, 0u)]
    [InlineData("unknown guid", TalentResult.NotFound, 0u)]
    [InlineData("the workbench's guid", TalentResult.NotFound, 0u)]
    [InlineData("5.01 m away", TalentResult.TooFar, 0u)]
    [InlineData("position NaN", TalentResult.TooFar, 0u)]
    [InlineData("casting", TalentResult.Casting, 0u)]
    [InlineData("unknown node", TalentResult.UnknownNode, 999u)]
    [InlineData("not held", TalentResult.NotAllocated, 20u)]
    [InlineData("listed twice", TalentResult.NotAllocated, 6u)]
    [InlineData("the middle of the path", TalentResult.WouldDisconnect, 4u)]
    [InlineData("level 11, 99 gold", TalentResult.NotEnoughGold, 0u)]
    [InlineData("level 12, 300 gold, 100 held for a trade", TalentResult.NotEnoughGold, 0u)]
    public void Refuse_a_refund_naming_why_in_order(string @case, TalentResult result, uint node)
    {
        bool held = @case == "level 12, 300 gold, 100 held for a trade";
        using var f = new Fixture(level: held ? (ushort)12 : (ushort)11, gold: held ? 300UL : 99UL);
        CharacterEntity character = f.Client.Character;

        // [3] would strand 4-6, and its 100 gold is more than 99: every row breaks every rule after its own. The seeded
        // tree holds ids up to 305, so 999 is unknown.
        uint[] request = @case switch
        {
            "unknown node" => [999, 3],
            "not held" => [20, 3],
            "listed twice" => [6, 6],
            "level 11, 99 gold" => [6],
            "level 12, 300 gold, 100 held for a trade" => [6, 5],
            _ => [3],
        };

        if (held)
            character.Locks.HeldGold = 100;
        if (Breaks(TalentResult.Casting))
            character.Spells[s_heavyStrike]!.Casting = true;
        if (Breaks(TalentResult.TooFar))
            character.Position = @case == "position NaN" ? new Vector3(float.NaN, 0f, 0f) : new Vector3(4.99f, 0f, 0f);

        ObjectGuid lectern = Breaks(TalentResult.NotFound)
            ? @case == "the workbench's guid" ? f.Workbench.Guid : new ObjectGuid(ObjectType.Doodad, 999)
            : f.Lectern.Guid;

        if (Breaks(TalentResult.Stunned))
            character.Auras.Add(Auras.AuraTestData.HeldControl(CrowdControl.Stun), DateTimeOffset.UnixEpoch);
        if (Breaks(TalentResult.Dead))
            character.IsDead = true;

        ulong goldBefore = character.Data!.Gold;

        f.Refund(lectern, request);

        STalentResultPacket answer = Assert.Single(f.Results());
        Assert.Equal((RequestId, result, node), (answer.RequestId, answer.Result, answer.NodeId));
        Assert.Single(f.Client.Sent);
        Assert.Equal(s_lane, character.Talents.Held);
        Assert.Equal(goldBefore, character.Data.Gold);
        Assert.False(character.SaveState.GoldDirty);
        Assert.Empty(character.SaveState.TakeMarks().Talents!);
        Assert.Equal((false, false), (character.Talents.StateOwed, character.Talents.AbilitiesOwed));

        bool Breaks(TalentResult rule) => Array.IndexOf(s_order, rule) >= Array.IndexOf(s_order, result);
    }

    [Theory]
    [InlineData("free at level 10")]
    [InlineData("paid at level 12")]
    [InlineData("reset all")]
    [InlineData("taken again arrives ready")]
    public void Refund_and_follow_with_the_abilities_and_the_state(string row)
    {
        bool paid = row == "paid at level 12";
        using var f = new Fixture(level: paid ? (ushort)12 : (ushort)10, gold: paid ? 300UL : 0UL);
        CharacterEntity character = f.Client.Character;
        IAbility cleave = character.Spells[s_cleave]!;
        if (row == "taken again arrives ready")
            cleave.CooldownTimer = 3f;

        f.Refund(f.Lectern.Guid, row switch
        {
            "paid at level 12" or "taken again arrives ready" => [6],
            "reset all" => [2, 3, 4, 5, 6],
            _ => [6, 5],
        });
        if (row == "taken again arrives ready")
        {
            new TalentAllocateHandler(NullLogger<TalentAllocateHandler>.Instance, f.World)
                .Execute(f.Client.Connection, new CTalentAllocatePacket { RequestId = RequestId, NodeIds = [6] });
        }

        // The results are the requests' only packets: the list and the state follow at the tick's flush.
        List<STalentResultPacket> results = f.Results();
        Assert.All(results, r => Assert.Equal((RequestId, TalentResult.Ok, 0u), (r.RequestId, r.Result, r.NodeId)));
        Assert.Equal(row == "taken again arrives ready" ? 2 : 1, results.Count);
        Assert.Equal(results.Count, f.Client.Sent.Count);
        SaveMarks marks = character.SaveState.TakeMarks();
        f.Client.Sent.Clear();
        TalentFlusher.Flush(f.Client.Connection, s_data.Quests, new TalentConfiguration());
        List<SCharacterAbilitiesPacket> lists =
            f.Client.Read<SCharacterAbilitiesPacket>(NetworkPacketType.SMSG_CHARACTER_ABILITIES);
        STalentStatePacket state = Assert.Single(f.Client.Read<STalentStatePacket>(NetworkPacketType.SMSG_TALENT_STATE));

        switch (row)
        {
            case "free at level 10":
                Assert.Equal([2, 3, 4], character.Talents.Held);
                Assert.Equal([5, 6], marks.Talents!.Keys.Order());
                Assert.Null(marks.GoldVersion);
                Assert.Equal([s_heavyStrike.Value], Assert.Single(lists).Abilities.Select(a => a.AbilityId));
                Assert.Equal((10u, 3u, 0UL), (state.PointsEarned, state.PointsSpent, state.RefundPricePerNode));
                break;
            case "paid at level 12":
                // One save writes the gold and the row (5.2.11).
                Assert.Equal(175UL, character.Data!.Gold);
                Assert.NotNull(marks.GoldVersion);
                Assert.Equal([6], marks.Talents!.Keys);
                Assert.Equal((12u, 4u, 125UL), (state.PointsEarned, state.PointsSpent, state.RefundPricePerNode));
                break;
            case "reset all":
                Assert.Empty(character.Talents.Held);
                Assert.Equal([s_heavyStrike.Value], Assert.Single(lists).Abilities.Select(a => a.AbilityId));
                Assert.Equal(0u, state.PointsSpent);
                break;
            case "taken again arrives ready":
                // Two changes in one tick, one list.
                Assert.Equal(0f, character.Spells[s_cleave]!.CooldownTimer);
                Assert.Equal([s_heavyStrike.Value, s_cleave.Value],
                    Assert.Single(lists).Abilities.Select(a => a.AbilityId));
                break;
        }
    }

    /// <summary>
    /// The instance with the seeded Lectern at x 10 and the seeded Workbench beside it, and the Warrior at the level and
    /// with the gold given, 4 m from the Lectern, entered holding 2-6 with nothing to save or send.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture(ushort level, ulong gold)
        {
            World = NewWorld(s_data);
            Instance = TestMapInstances.Build(World, time: new FakeTimeProvider());
            World.InstanceRegistry.GetInstanceById(Instance.InstanceId).Returns(Instance);
            Lectern = Place(2, s_lectern, new Vector3(10f, 0f, 0f));
            Workbench = Place(1, s_workbench, new Vector3(8f, 0f, 2f));

            CharacterEntity character = TestCharacters.New(Me, gold);
            character.Level = level;
            character.Position = new Vector3(6f, 0f, 0f);
            Client = Join(Instance, character);
            TalentTestCharacters.Enter(character, s_data, s_lane);
            Handler = new TalentRefundHandler(NullLogger<TalentRefundHandler>.Instance, World, TestEconomy.Over(World));
            Client.Sent.Clear();
        }

        public IWorld World { get; }
        public MapInstance Instance { get; }
        public MapInstanceClient Client { get; }
        public Doodad Lectern { get; }
        public Doodad Workbench { get; }
        public TalentRefundHandler Handler { get; }

        public void Refund(ObjectGuid lectern, uint[] nodeIds) => Handler.Execute(Client.Connection,
            new CTalentRefundPacket { RequestId = RequestId, LecternGuid = lectern.RawValue, NodeIds = [.. nodeIds] });

        public List<STalentResultPacket> Results() =>
            Client.Read<STalentResultPacket>(NetworkPacketType.SMSG_TALENT_RESULT);

        public void Dispose() => Instance.Dispose();

        private Doodad Place(uint id, DoodadTemplateView template, Vector3 position)
        {
            var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, id), template, position, 0f);
            Instance.AddDoodad(doodad);
            return doodad;
        }
    }
}
