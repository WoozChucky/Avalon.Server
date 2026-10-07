using Avalon.Domain.Characters;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World.Handlers;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>
/// #738 through the real quest handlers: an accept, a turn-in or an abandon on the tick a shop opens is already in the
/// opening list, so the vendor pass sends nothing more; one while the shop is open resends it once.
/// </summary>
public class VendorQuestRefreshShould : IAsyncLifetime
{
    private VendorWorld _w = null!;
    private QuestService _quests = null!;

    public async Task InitializeAsync()
    {
        // The gated row's quest: given and taken back by the Smith, one talk with the Smith.
        _w = await VendorWorld.CreateAsync(
            [QuestTestData.Quest(GatedQuest, giver: Smith.Value, ender: Smith.Value).WithStage(0, QuestTestData.Talk(71, Smith.Value))]);
        _w.Quests = QuestProgress.Instance;
        _quests = new QuestService(_w.World, Substitute.For<IServiceProvider>(), _w.Economy, new SteadyLootRandom(0), _w.Clock,
            NullLogger<QuestService>.Instance);
        Assert.True(_w.Data.Quests.TryGet(GatedQuest, out _));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private QuestResult Accept()
    {
        new QuestAcceptHandler(_quests, NullLogger<QuestAcceptHandler>.Instance).Execute(_w.Main.Connection,
            new CQuestAcceptPacket { QuestId = GatedQuest, NpcGuid = VendorWorld.SmithGuid.RawValue });
        return LastResult();
    }

    private QuestResult TurnIn()
    {
        new QuestTurnInHandler(_quests, NullLogger<QuestTurnInHandler>.Instance).Execute(_w.Main.Connection,
            new CQuestTurnInPacket { QuestId = GatedQuest, NpcGuid = VendorWorld.SmithGuid.RawValue });
        return LastResult();
    }

    private QuestResult Abandon()
    {
        new QuestAbandonHandler(_quests, NullLogger<QuestAbandonHandler>.Instance).Execute(_w.Main.Connection,
            new CQuestAbandonPacket { QuestId = GatedQuest });
        return LastResult();
    }

    private QuestResult LastResult() =>
        _w.Main.Read<SQuestResultPacket>(Avalon.Network.Packets.Abstractions.NetworkPacketType.SMSG_QUEST_RESULT)[^1].Result;

    /// <summary>Held and ready, as a talk with the Smith would leave it.</summary>
    private void HoldReady()
    {
        QuestLog log = _w.Main.Character.Quests;
        log.SetState(log.Start(GatedQuest, Now), CharacterQuestState.ReadyToTurnIn);
    }

    private static bool ListsGated(SVendorListPacket list) => list.Entries.Any(e => e.Sequence == GatedSequence);

    [Theory]
    [InlineData("accept")]
    [InlineData("turn-in")]
    [InlineData("abandon")]
    public void Send_one_list_for_a_quest_change_and_a_shop_opening_in_the_same_tick(string change)
    {
        if (change != "accept")
            HoldReady();
        if (change != "abandon")
            _w.Interact(_w.Main, VendorWorld.SmithGuid);
        Assert.Equal(QuestResult.Ok, change switch { "accept" => Accept(), "turn-in" => TurnIn(), _ => Abandon() });
        _w.OpenShop();

        _w.EndOfTick();

        SVendorListPacket list = Assert.Single(_w.Main.Lists());
        if (change == "turn-in")
            Assert.True(ListsGated(list));
    }

    /// <summary>A turn-in at the Smith while its shop is open meets the gate: the vendor pass resends the list once, with the row.</summary>
    [Fact]
    public void Resend_the_list_once_when_a_turn_in_meets_the_gate_while_the_shop_is_open()
    {
        HoldReady();
        _w.OpenShop();
        Assert.False(ListsGated(Assert.Single(_w.Main.Lists())));

        Assert.Equal(QuestResult.Ok, TurnIn());
        _w.EndOfTick();
        _w.EndOfTick();

        Assert.Equal(2, _w.Main.Lists().Count);
        Assert.True(ListsGated(_w.Main.Lists()[^1]));
    }
}
