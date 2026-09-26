using Avalon.Domain.World;
using Avalon.Network.Packets.Vendor;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// The vendor service operations (spec #432): each applies a decision VendorRules accepted, all or
/// nothing, with the SaveState and ClientChanges marks #459 prescribes. The rules themselves are
/// VendorRulesShould's.
/// </summary>
public class VendorTradeShould
{
    private static VendorTrade Trade(CharacterEntity character, ulong maxMoney = 1_000_000) =>
        new(character, TestCharacters.InventoryFor(character, Find), new CharacterWallet(character, maxMoney), Find, maxMoney);

    private static InventoryItem At(CharacterEntity character, ushort slot) =>
        TestCharacters.At(character, InventoryType.Bag, slot);

    [Fact]
    public void Take_the_costs_the_gold_and_the_stock_and_add_the_item_on_a_buy()
    {
        CharacterEntity character = TestCharacters.New(money: 100);
        InventoryItem tonics = TestCharacters.Item(0, Tonic, count: 3);
        character.Container(InventoryType.Bag).Load([tonics]);
        VendorStockState stock = Stock();
        VendorStockView elixir = stock.Rows.Single(r => r.Sequence == ElixirSequence);

        VendorResult result = Trade(character).TryBuy(true, stock, ElixirSequence, 1, NoQuestProgress.Instance, Now);

        Assert.Equal(VendorResult.Ok, result);
        Assert.Equal(70UL, character.Data!.Money);
        Assert.Equal(1u, At(character, 0).Count);                  // two of the three Tonics were the cost
        InventoryItem bought = At(character, 1);
        Assert.Equal((Elixir.Id, 1u), (bought.TemplateId, bought.Count));
        Assert.Equal(4u, stock.Available(elixir));
        Assert.True(stock.Changed);

        Assert.True(character.SaveState.MoneyDirty);
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(tonics.InstanceId));
        Assert.Equal(SaveState.New, character.SaveState.ItemState(bought.InstanceId));
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 1));
        Assert.True(character.ClientChanges.MoneyChanged);
        Assert.Contains((InventoryType.Bag, (ushort)0), character.ClientChanges.Slots);
        Assert.Contains((InventoryType.Bag, (ushort)1), character.ClientChanges.Slots);
        // A stock change reaches open shops through the stock state, not the buyer's flag.
        Assert.False(character.VendorListOwed);
    }

    [Fact]
    public void Leave_an_unlimited_row_uncounted_on_a_buy()
    {
        CharacterEntity character = TestCharacters.New(money: 100);
        VendorStockState stock = Stock();

        Assert.Equal(VendorResult.Ok, Trade(character).TryBuy(true, stock, TonicSequence, 3, NoQuestProgress.Instance, Now));

        Assert.Equal(3u, At(character, 0).Count);
        Assert.False(stock.Changed);
    }

    [Fact]
    public void Change_nothing_for_a_refused_buy()
    {
        CharacterEntity character = TestCharacters.New(money: 100);
        character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, Tonic, count: 1)]);
        VendorStockState stock = Stock();

        VendorResult result = Trade(character).TryBuy(true, stock, ElixirSequence, 1, NoQuestProgress.Instance, Now);

        Assert.Equal(VendorResult.MissingItems, result);
        Assert.Equal(100UL, character.Data!.Money);
        Assert.Equal(1u, At(character, 0).Count);
        Assert.Equal(5u, stock.Available(stock.Rows.Single(r => r.Sequence == ElixirSequence)));
        Assert.False(stock.Changed);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Sell_a_whole_stack_into_the_buyback_list_under_its_own_id()
    {
        CharacterEntity character = TestCharacters.New(money: 0);
        InventoryItem blade = TestCharacters.Item(4, Blade, durability: 42);
        character.Container(InventoryType.Bag).Load([blade]);

        Assert.Equal(VendorResult.Ok, Trade(character).TrySell(true, 4, null));

        Assert.Equal(25UL, character.Data!.Money);
        Assert.False(character.Container(InventoryType.Bag).TryGet(4, out _));
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(blade.InstanceId));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 4));
        BuybackEntry entry = Assert.Single(character.Buyback.Entries);
        Assert.Equal(blade, entry.Item);
        Assert.Equal(25UL, entry.Price);
        Assert.True(character.VendorListOwed);
        Assert.True(character.ClientChanges.MoneyChanged);
    }

    [Fact]
    public void Sell_part_of_a_stack_and_keep_a_copy_for_buyback()
    {
        CharacterEntity character = TestCharacters.New(money: 0);
        InventoryItem tonics = TestCharacters.Item(2, Tonic, count: 5);
        character.Container(InventoryType.Bag).Load([tonics]);

        Assert.Equal(VendorResult.Ok, Trade(character).TrySell(true, 2, 2));

        Assert.Equal(8UL, character.Data!.Money);
        Assert.Equal(tonics with { Count = 3 }, At(character, 2));
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(tonics.InstanceId));
        InventoryItem copy = Assert.Single(character.Buyback.Entries).Item;
        Assert.NotEqual(tonics.InstanceId, copy.InstanceId);
        Assert.Equal((2u, (ushort)2, Tonic.Id), (copy.Count, copy.Slot, copy.TemplateId));
        Assert.Equal(8UL, character.Buyback.Entries[0].Price);
    }

    [Fact]
    public void Buy_back_the_exact_instance_and_take_it_off_the_list()
    {
        CharacterEntity character = TestCharacters.New(money: 30);
        InventoryItem blade = TestCharacters.Item(4, Blade, durability: 42);
        character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, Tonic), blade]);
        VendorTrade trade = Trade(character);
        trade.TrySell(true, 4, null);
        character.VendorListOwed = false;

        Assert.Equal(VendorResult.Ok, trade.TryBuyback(true, 0));

        Assert.Equal(30UL, character.Data!.Money);
        Assert.Equal(blade with { Slot = 1 }, At(character, 1));      // the lowest free slot, same id and durability
        Assert.Empty(character.Buyback.Entries);
        Assert.Equal(SaveState.New, character.SaveState.ItemState(blade.InstanceId));
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 1));
        Assert.True(character.VendorListOwed);
    }

    [Fact]
    public void Change_nothing_for_a_refused_sale_or_buyback()
    {
        CharacterEntity character = TestCharacters.New(money: 0);
        character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, Keepsake)]);
        VendorTrade trade = Trade(character);

        Assert.Equal(VendorResult.NotSellable, trade.TrySell(true, 0, null));
        Assert.Equal(VendorResult.NotFound, trade.TryBuyback(true, 0));

        Assert.Equal(0UL, character.Data!.Money);
        Assert.Single(character.Container(InventoryType.Bag).Items);
        Assert.Empty(character.Buyback.Entries);
        Assert.False(character.VendorListOwed);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    /// <summary>
    /// VendorRules judges room and uniqueness itself, on the Bag as it will be once the costs are
    /// out, and the apply step then calls TryAdd, which judges them again through CanAdd's check
    /// (#432). If the two ever disagreed, a buy would throw after its costs and gold were taken.
    /// Over many bag layouts: every buy the rules accept is one CanAdd accepts once the costs are
    /// out, and applies whole; every buy refused for room or uniqueness is one CanAdd refuses too.
    /// </summary>
    [Fact]
    public void Agree_with_CanAdd_on_every_buy_the_rules_accept_or_refuse_for_room()
    {
        uint[] sequences = [TonicSequence, BladeSequence, ElixirSequence, CharmSequence];
        uint[] counts = [1, 2, 3, 5, 19, 20];
        int accepted = 0, refusedForRoom = 0;

        for (int seed = 0; seed < 300; seed++)
        {
            (List<InventoryItem> bag, List<InventoryItem> bank) = Layout(new Random(seed));

            foreach (uint sequence in sequences)
            {
                foreach (uint count in counts)
                {
                    string at = $"seed {seed}, sequence {sequence}, count {count}";
                    CharacterEntity probe = Loaded(bag, bank);
                    VendorStockState stock = Stock();
                    BuyDecision decision = VendorRules.DecideBuy(
                        probe, true, stock, sequence, count, Find, NoQuestProgress.Instance);

                    if (decision.Result is not (VendorResult.Ok or VendorResult.InventoryFull or VendorResult.UniqueAlreadyOwned))
                        continue;

                    ItemTemplate item = Find(stock.Rows.Single(r => r.Sequence == sequence).ItemTemplateId)!;
                    CharacterInventoryService inventory = TestCharacters.InventoryFor(probe, Find);
                    foreach (VendorStockView row in stock.Rows.Where(r => r.Sequence == sequence))
                    {
                        foreach (VendorCostView cost in row.Costs)
                            Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(cost.ItemTemplateId, cost.Count * count));
                    }

                    InventoryAddResult canAdd = inventory.CanAdd(item.Id, count);
                    if (decision.Result == VendorResult.Ok)
                    {
                        accepted++;
                        Assert.True(canAdd == InventoryAddResult.Ok, $"{at}: the rules accepted, CanAdd said {canAdd}");

                        CharacterEntity buyer = Loaded(bag, bank);
                        Assert.Equal(VendorResult.Ok, Trade(buyer).TryBuy(true, Stock(), sequence, count, NoQuestProgress.Instance, Now));
                    }
                    else
                    {
                        refusedForRoom++;
                        Assert.True(canAdd != InventoryAddResult.Ok, $"{at}: the rules said {decision.Result}, CanAdd said Ok");
                    }
                }
            }
        }

        // Both sides of the agreement were exercised.
        Assert.True(accepted > 100, $"only {accepted} buys were accepted");
        Assert.True(refusedForRoom > 100, $"only {refusedForRoom} buys were refused for room or uniqueness");
    }

    /// <summary>A random Bag, often full, and sometimes a Charm in the Bag or the Bank.</summary>
    private static (List<InventoryItem> Bag, List<InventoryItem> Bank) Layout(Random random)
    {
        // Half the bags are full to the last slot, where room depends on what a cost frees.
        double fill = random.Next(2) == 0 ? random.NextDouble() : 1.0;
        bool charmOwned = random.Next(4) == 0;
        List<InventoryItem> bank = charmOwned && random.Next(2) == 0 ? [TestCharacters.Item(0, Charm)] : [];
        bool charmPlaced = bank.Count > 0;
        List<InventoryItem> bag = [];

        for (ushort slot = 0; slot < 30; slot++)
        {
            if (random.NextDouble() >= fill)
                continue;

            switch (random.Next(5))
            {
                case 0:
                    bag.Add(TestCharacters.Item(slot, Tonic, count: (uint)random.Next(1, 21)));
                    break;
                case 1:
                    bag.Add(TestCharacters.Item(slot, Elixir, count: (uint)random.Next(1, 6)));
                    break;
                case 2:
                    bag.Add(TestCharacters.Item(slot, Blade, durability: 42));
                    break;
                case 3 when charmOwned && !charmPlaced:
                    bag.Add(TestCharacters.Item(slot, Charm));
                    charmPlaced = true;
                    break;
                default:
                    bag.Add(TestCharacters.Item(slot, Keepsake));
                    break;
            }
        }

        return (bag, bank);
    }

    private static CharacterEntity Loaded(List<InventoryItem> bag, List<InventoryItem> bank)
    {
        CharacterEntity character = TestCharacters.New(money: 1_000_000);
        character.Container(InventoryType.Bag).Load(bag);
        character.Container(InventoryType.Bank).Load(bank);
        return character;
    }

    [Fact]
    public void Keep_only_the_last_ten_sales()
    {
        CharacterEntity character = TestCharacters.New(money: 0);
        character.Container(InventoryType.Bag).Load(
            Enumerable.Range(0, 11).Select(s => TestCharacters.Item((ushort)s, Tonic)).ToList());
        VendorTrade trade = Trade(character);

        for (uint slot = 0; slot < 11; slot++)
            Assert.Equal(VendorResult.Ok, trade.TrySell(true, slot, null));

        Assert.Equal(VendorBuyback.Capacity, character.Buyback.Entries.Count);
        Assert.Equal((ushort)10, character.Buyback.Entries[0].Item.Slot);
        Assert.DoesNotContain(character.Buyback.Entries, e => e.Item.Slot == 0);
        Assert.Equal(44UL, character.Data!.Money);
    }
}
