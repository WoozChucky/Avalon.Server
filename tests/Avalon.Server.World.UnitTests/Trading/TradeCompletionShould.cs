using Avalon.Common;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Trade;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Trading;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Trading;

/// <summary>
/// A trade both sides confirmed (#1175): save first, then swap. Nothing moves in memory until the paired save is known
/// to have committed, an unknown outcome is settled by the trade record, and every path releases both holds.
/// </summary>
public class TradeCompletionShould
{
    [Theory]
    [InlineData("committed", true)]
    [InlineData("refused", false)]
    [InlineData("unknown, record found", true)]
    [InlineData("unknown, record absent", false)]
    [InlineData("unknown, lookup fails once, then found", true)]
    public void Move_items_and_gold_only_once_the_paired_save_is_known_to_have_committed(string row, bool swapped)
    {
        (TradeTestWorld w, Trader a, Trader b, InventoryItem sword, InventoryItem potion) = Confirmed();

        Assert.True(a.States().Last().Completing);
        (TradeSidePlan first, TradeSidePlan second, TradeRecord record) = w.Saver.Trades.Single();
        Assert.Equal([sword.InstanceId],
            record.Items.Where(i => i.GiverCharacterId == a.Character.Data!.Id).Select(i => i.ItemInstanceId));
        Assert.Equal((30UL, 0UL), (record.FirstGold, record.SecondGold));
        Assert.Equal(sword.InstanceId, At(a, 0).InstanceId);          // memory unchanged while the save is in flight
        Assert.Equal(TradeResult.Completing, w.Trades.Cancel(a.Id));
        Assert.Equal(TradeResult.Completing, w.Trades.SetGold(b.Id, 1));
        Assert.True(b.Character.Locks.IsReserved(second.Incoming[0].Slot));
        w.Saver.AtRelease = () =>
            b.Character.Container(InventoryType.Bag).TryGet(second.Incoming[0].Slot, out InventoryItem held)
            && held.InstanceId == sword.InstanceId;

        w.Saver.Finish(row switch
        {
            "committed" => SaveOutcome.Committed,
            "refused" => SaveOutcome.Refused,
            _ => SaveOutcome.Unknown,
        });
        w.Trades.Tick();

        if (row.StartsWith("unknown", StringComparison.Ordinal))
        {
            Assert.Equal(TradeState.Completing, w.Trades.TradeOf(a.Id)!.State);
            Assert.Equal(sword.InstanceId, At(a, 0).InstanceId);
            Assert.Empty(w.Saver.Released);
            Assert.Equal(1, w.Saver.Lookups);

            if (row == "unknown, lookup fails once, then found")
            {
                w.Saver.Answer(null);
                w.Trades.Tick();
                Assert.Equal(1, w.Saver.Lookups);                     // no retry before 1 s
                w.Clock.Advance(TimeSpan.FromSeconds(1));
                w.Trades.Tick();
                Assert.Equal(2, w.Saver.Lookups);
            }

            w.Saver.Answer(swapped);
            w.Trades.Tick();
        }

        if (swapped)
        {
            Assert.Equal(sword.InstanceId, At(b, second.Incoming[0].Slot).InstanceId);
            Assert.Equal(potion.InstanceId, At(a, first.Incoming[0].Slot).InstanceId);
            Assert.Equal((70UL, 30UL), (a.Character.Data!.Gold, b.Character.Data!.Gold));
        }
        else
        {
            Assert.Equal(sword.InstanceId, At(a, 0).InstanceId);
            Assert.Equal(potion.InstanceId, At(b, 0).InstanceId);
            Assert.Equal((100UL, 0UL), (a.Character.Data!.Gold, b.Character.Data!.Gold));
        }

        TradeResult closed = swapped ? TradeResult.Ok : TradeResult.SaveFailed;
        Assert.All([a, b], t => Assert.Equal([closed], t.Closed().Select(p => p.Result)));
        Assert.Equal([a.Character.Data!.Id, b.Character.Data!.Id], w.Saver.Released);
        Assert.Equal([swapped, swapped], w.Saver.ReleaseChecks);      // swapped: released after the swap
        Assert.False(a.Character.Locks.Any);
        Assert.False(b.Character.Locks.Any);
        Assert.Null(w.Trades.TradeOf(a.Id));
    }

    [Fact]
    public void Keep_the_window_open_when_a_completion_check_fails()
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1);
        Trader b = w.Online(2);
        InventoryItem sword = Item(0, Sword);
        a.Character.Container(InventoryType.Bag).Load([sword]);
        // B's Bag is full and it offers nothing: its room after removal is 0 for 1 incoming item.
        b.Character.Container(InventoryType.Bag).Load(
            Enumerable.Range(0, b.Character.Container(InventoryType.Bag).Capacity).Select(s => Item((byte)s, Potion)).ToArray());
        w.Open(a, b);
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, 0, 0));

        uint revision = a.States().Last().Revision;
        Assert.Equal(TradeResult.Ok, w.Trades.Confirm(a.Id, revision));
        Assert.Equal(TradeResult.Ok, w.Trades.Confirm(b.Id, revision));

        Assert.Equal(TradeResult.BagFull, b.Results().Last().Result);
        Assert.Equal(TradeResult.PartnerBagFull, a.Results().Last().Result);
        Assert.Empty(w.Saver.Trades);
        Assert.False(a.States().Last().You.Confirmed || a.States().Last().Partner.Confirmed);
        Assert.Equal(TradeState.Open, w.Trades.TradeOf(a.Id)!.State);
        Assert.True(a.Character.Locks.IsLocked(sword.InstanceId));     // still trading, still locked
    }

    [Fact]
    public void Finish_the_trade_for_a_trader_who_left_while_it_completed()
    {
        (TradeTestWorld w, Trader a, Trader b, _, InventoryItem potion) = Confirmed();

        w.Trades.CharacterOffline(a.Connection, a.Character);
        Assert.Empty(b.Closed());

        w.Saver.Finish(SaveOutcome.Committed);
        w.Trades.Tick();

        Assert.Equal(potion.InstanceId, At(a, 0).InstanceId);          // applied to the entity its despawn save will snapshot
        Assert.Equal(TradeResult.Ok, b.Closed().Single().Result);
        Assert.Empty(a.Closed());
        Assert.Equal([a.Character.Data!.Id, b.Character.Data!.Id], w.Saver.Released);
    }

    [Fact]
    public async Task Settle_a_completing_trade_at_shutdown()
    {
        (TradeTestWorld w, Trader a, Trader b, InventoryItem sword, _) = Confirmed();
        TradeSidePlan second = w.Saver.Trades.Single().Second;

        Task settle = w.Trades.SettleAsync(TimeSpan.FromSeconds(5), default);
        w.Saver.Finish(SaveOutcome.Committed);
        await settle.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(sword.InstanceId, At(b, second.Incoming[0].Slot).InstanceId);   // no Tick ran
        Assert.Equal([a.Character.Data!.Id, b.Character.Data!.Id], w.Saver.Released);
    }

    /// <summary>
    /// A holds the Sword at Bag 0 and 100 gold and offers both and 30 gold; B offers its Potion ×5 at Bag 0; both
    /// confirm the current revision, so the paired save is in flight.
    /// </summary>
    private static (TradeTestWorld W, Trader A, Trader B, InventoryItem Sword, InventoryItem Potion) Confirmed()
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1, gold: 100);
        Trader b = w.Online(2);
        InventoryItem sword = Item(0, Sword);
        InventoryItem potion = Item(0, Potion, count: 5);
        a.Character.Container(InventoryType.Bag).Load([sword]);
        b.Character.Container(InventoryType.Bag).Load([potion]);
        w.Open(a, b);
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, 0, 0));
        Assert.Equal(TradeResult.Ok, w.Trades.SetGold(a.Id, 30));
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(b.Id, 0, 0));

        uint revision = a.States().Last().Revision;
        Assert.Equal(TradeResult.Ok, w.Trades.Confirm(a.Id, revision));
        Assert.Equal(TradeResult.Ok, w.Trades.Confirm(b.Id, revision));
        return (w, a, b, sword, potion);
    }

    private static InventoryItem At(Trader trader, byte bagSlot) =>
        TestCharacters.At(trader.Character, InventoryType.Bag, bagSlot);
}
