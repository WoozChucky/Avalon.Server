using Avalon.Common;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Trade;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Trading;
using Microsoft.EntityFrameworkCore;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.Inventory.TestModifiers;

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
    public async Task Move_items_and_gold_only_once_the_paired_save_is_known_to_have_committed(string row, bool swapped)
    {
        (TradeTestWorld w, Trader a, Trader b, InventoryItem sword, InventoryItem potion) = Confirmed();
        string modifiers = Describe(a.Character.ItemModifiers.Of(sword.InstanceId));
        string rolls = DescribeRolls(a.Character.ItemModifiers.Of(sword.InstanceId));

        Assert.True(a.States().Last().Completing);
        (TradeSidePlan first, TradeSidePlan second, TradeRecord record) = w.Saver.Trades.Single();
        TradeRecordItem given = Assert.Single(record.Items, i => i.GiverCharacterId == a.Character.Data!.Id);
        Assert.Equal((sword.InstanceId, ItemRarity.Magic, (ushort)7), (given.ItemInstanceId, given.Rarity, given.ItemLevel));
        Assert.Equal(rolls, DescribeRolls(given.Modifiers));
        Assert.Equal([(ItemBaseStat.DamageMin, 4), (ItemBaseStat.DamageMax, 9), (ItemBaseStat.AttackTime, 760)],
            given.BaseStats.OrderBy(s => s.Stat).Select(s => (s.Stat, s.Value)));
        // The receiver's side of the paired save writes the sword's base stats as its own.
        ItemModifierWrite received = TradeSaveBatch.Apply(new CharacterSaveBatch(b.Character.Data!, [], [], [], []), second,
            DateTime.UnixEpoch).Modifiers!;
        Assert.Equal(s_swordBaseStats, ItemBaseStats.Of(received.BaseStats.Where(s => s.ItemInstanceId == sword.InstanceId)));
        Assert.Equal((30UL, 0UL), (record.FirstGold, record.SecondGold));
        Assert.Equal(sword.InstanceId, At(a, 0).InstanceId);          // memory unchanged while the save is in flight
        Assert.Equal(TradeResult.Completing, w.Trades.Cancel(a.Id));
        Assert.Equal(TradeResult.Completing, w.Trades.SetGold(b.Id, 1));
        Assert.True(b.Character.Locks.IsReserved(second.Incoming[0].Item.Slot));
        w.Saver.AtRelease = () =>
            b.Character.Container(InventoryType.Bag).TryGet(second.Incoming[0].Item.Slot, out InventoryItem held)
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
            Assert.Equal(sword.InstanceId, At(b, second.Incoming[0].Item.Slot).InstanceId);
            Assert.Equal(potion.InstanceId, At(a, first.Incoming[0].Item.Slot).InstanceId);
            Assert.Equal((70UL, 30UL), (a.Character.Data!.Gold, b.Character.Data!.Gold));

            // #1179: the receiver holds the sword's modifiers under their own ids, and the giver's next save names neither
            // (CharacterSaverShould writes the paired save itself).
            Assert.Equal(modifiers, Describe(b.Character.ItemModifiers.Of(sword.InstanceId)));
            Assert.Equal(s_swordBaseStats, b.Character.ItemModifiers.BaseStatsOf(sword.InstanceId));
            Assert.Empty(a.Character.ItemModifiers.Of(sword.InstanceId));
            CharacterSaveBatch next = CharacterSaveSnapshot.Take(a.Character).Batch;
            Assert.DoesNotContain(sword.InstanceId, next.UpsertItems.Select(i => i.Id).Concat(next.DeleteItems));
            Assert.Null(next.Modifiers);
        }
        else
        {
            Assert.Equal(sword.InstanceId, At(a, 0).InstanceId);
            Assert.Equal(modifiers, Describe(a.Character.ItemModifiers.Of(sword.InstanceId)));
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

        Assert.Equal(sword.InstanceId, At(b, second.Incoming[0].Item.Slot).InstanceId);   // no Tick ran
        Assert.Equal([a.Character.Data!.Id, b.Character.Data!.Id], w.Saver.Released);
    }

    private static readonly ItemBaseStats s_swordBaseStats = new() { DamageMin = 4, DamageMax = 9, AttackTime = 760 };

    /// <summary>
    /// A holds the Sword at Bag 0 and 100 gold and offers both and 30 gold; B offers its Potion ×5 at Bag 0; both
    /// confirm the current revision, so the paired save is in flight. The Sword is Magic, item level 7, with its base
    /// stats and two modifiers, and was added this session: New, its rolled rows marked, never saved (#1179).
    /// </summary>
    internal static (TradeTestWorld W, Trader A, Trader B, InventoryItem Sword, InventoryItem Potion) Confirmed()
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1, gold: 100);
        Trader b = w.Online(2);
        InventoryItem sword = Item(0, Sword) with { Rarity = ItemRarity.Magic, ItemLevel = 7 };
        InventoryItem potion = Item(0, Potion, count: 5);
        b.Character.Container(InventoryType.Bag).Load([potion]);
        Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(a.Character).TryAddInstance(new CarriedItem(sword,
            s_swordBaseStats,
        [
            Modifier(sword.InstanceId, 202, 0, (0, StatType.AddedFireDamage, 2, 5)),
            Modifier(sword.InstanceId, 10103, 1, (0, StatType.Strength, 8, null), (1, StatType.Armor, 3, null)),
        ])));
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
