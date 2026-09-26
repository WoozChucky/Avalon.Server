using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>
/// The spec's buy, sell and buyback checks, one row per check and outcome, in the spec's order: the
/// first failure answers. VendorRules only decides, so every row also proves it changed nothing.
/// </summary>
public class VendorRulesShould
{
    public sealed record Held(SlotRef At, ItemTemplate Template, uint Count = 1, uint Durability = 0);

    private static SlotRef Bag(ushort slot) => new(InventoryType.Bag, slot);

    private static SlotRef Vault(ushort slot) => new(InventoryType.Bank, slot);

    private static SlotRef Eq(ushort slot) => new(InventoryType.Equipment, slot);

    private static Held At(SlotRef slot, ItemTemplate template, uint count = 1, uint durability = 0) =>
        new(slot, template, count, durability);

    /// <summary>Keepsakes in Bag slots 0-28: one slot, 29, is free.</summary>
    private static Held[] AllButLastSlot => Enumerable.Range(0, 29).Select(s => At(Bag((ushort)s), Keepsake)).ToArray();

    private static Held[] FullBag => Enumerable.Range(0, 30).Select(s => At(Bag((ushort)s), Keepsake)).ToArray();

    private static CharacterEntity Arrange(IReadOnlyCollection<Held> held, ulong money, bool dead)
    {
        CharacterEntity character = TestCharacters.New(money: money);
        foreach (IGrouping<InventoryType, Held> container in held.GroupBy(h => h.At.Container))
        {
            character.Container(container.Key).Load(
                container.Select(h => TestCharacters.Item(h.At.Slot, h.Template, h.Count, h.Durability)).ToList());
        }

        character.IsDead = dead;
        return character;
    }

    private static int ItemsHeld(CharacterEntity character) =>
        character.Container(InventoryType.Equipment).Items.Count
        + character.Container(InventoryType.Bag).Items.Count
        + character.Container(InventoryType.Bank).Items.Count;

    private static void AssertUnchanged(CharacterEntity character, ulong money, int held)
    {
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
        Assert.Equal(money, character.Data!.Money);
        Assert.Equal(held, ItemsHeld(character));
    }

    // ---- Buy ------------------------------------------------------------------------------------

    public sealed record BuyCase(
        string Name,
        Held[] Holding,
        ulong Money,
        uint Sequence,
        uint? Count,
        VendorResult Expected,
        ulong Total = 0,
        bool Shop = true,
        bool Dead = false,
        uint BladesSold = 0,
        uint ElixirsSold = 0,
        bool QuestMet = false,
        bool TonicGone = false,
        uint? CharmSellPrice = null);

    private static readonly BuyCase[] BuyCases =
    [
        new("Buy one when no count is given", [], 100, TonicSequence, null, VendorResult.Ok, Total: 10),
        new("Multiply the price by the count", [], 100, TonicSequence, 5, VendorResult.Ok, Total: 50),
        new("Buy exactly what the gold covers", [], 50, TonicSequence, 5, VendorResult.Ok, Total: 50),
        new("Charge the row's price override", [], 40, CharmSequence, null, VendorResult.Ok, Total: 40),
        // A /reload items raised the Charm's SellPrice to 45, above the row's override of 40, with no
        // vendor reload to refuse the row: the price never drops below what the item sells back for.
        new("Charge the SellPrice when the row is priced below it", [], 100, CharmSequence, null, VendorResult.Ok, Total: 45, CharmSellPrice: 45),
        new("Buy a whole stack", [], 1000, TonicSequence, 20, VendorResult.Ok, Total: 200),
        new("Buy the last unit", [], 1000, BladeSequence, null, VendorResult.Ok, Total: 100, BladesSold: 1),

        // 1. Alive. 2. Shop open.
        new("Answer Dead before anything else", [], 0, 99, 0, VendorResult.Dead, Shop: false, Dead: true),
        new("Answer ShopClosed with no shop open", [], 0, 99, 0, VendorResult.ShopClosed, Shop: false),

        // 3. The row exists and its quest gate passes.
        new("Refuse a sequence nobody stocks", [], 100, 99, null, VendorResult.NotFound),
        new("Hide a row whose quest is not met", [], 100, GatedSequence, null, VendorResult.NotFound),
        new("Sell a gated row once its quest is met", [], 100, GatedSequence, null, VendorResult.Ok, Total: 80, QuestMet: true),
        new("Refuse a row whose item template is gone", [], 100, TonicSequence, null, VendorResult.NotFound, TonicGone: true),

        // 4. The count.
        new("Refuse a count of 0", [], 100, TonicSequence, 0, VendorResult.InvalidCount),
        new("Refuse a count above the stack size", [], 1000, TonicSequence, 21, VendorResult.InvalidCount),
        new("Check the count before the stock", [], 1000, BladeSequence, 2, VendorResult.InvalidCount, BladesSold: 2),

        // 5. Stock for the count.
        new("Refuse a sold-out row", [], 1000, BladeSequence, null, VendorResult.OutOfStock, BladesSold: 2),
        new("Refuse more than the stock left", [At(Bag(0), Tonic, 20)], 1000, ElixirSequence, 2, VendorResult.OutOfStock, ElixirsSold: 4),
        new("Check the stock before the gold", [], 0, BladeSequence, null, VendorResult.OutOfStock, BladesSold: 2),

        // 6. Gold for price times count.
        new("Refuse too little gold", [], 9, TonicSequence, null, VendorResult.NotEnoughGold),
        new("Refuse gold for one but not for the count", [], 49, TonicSequence, 5, VendorResult.NotEnoughGold),
        new("Check the gold before the cost items", [], 29, ElixirSequence, null, VendorResult.NotEnoughGold),

        // 7. Cost items times count, from the Bag only.
        new("Buy with the cost items in the bag", [At(Bag(0), Tonic, 2)], 30, ElixirSequence, null, VendorResult.Ok, Total: 30),
        new("Count cost items across stacks", [At(Bag(0), Tonic, 1), At(Bag(5), Tonic, 1)], 30, ElixirSequence, null, VendorResult.Ok, Total: 30),
        new("Multiply the cost items by the count", [At(Bag(0), Tonic, 3)], 60, ElixirSequence, 2, VendorResult.MissingItems),
        new("Count only the bag for cost items", [At(Vault(0), Tonic, 2), At(Bag(0), Tonic, 1)], 30, ElixirSequence, null, VendorResult.MissingItems),

        // 8. Room, judged after the costs are taken out.
        new("Refuse a full bag", FullBag, 100, TonicSequence, null, VendorResult.InventoryFull),
        new("Fill a partial stack in an otherwise full bag", [.. AllButLastSlot, At(Bag(29), Tonic, 19)], 100, TonicSequence, 1, VendorResult.Ok, Total: 10),
        new("Refuse past a partial stack's room", [.. AllButLastSlot, At(Bag(29), Tonic, 19)], 100, TonicSequence, 2, VendorResult.InventoryFull),
        new("Use the slot a spent cost stack frees", [.. AllButLastSlot, At(Bag(29), Tonic, 2)], 30, ElixirSequence, null, VendorResult.Ok, Total: 30),
        new("Count no room in a cost stack that survives", [.. AllButLastSlot, At(Bag(29), Tonic, 3)], 30, ElixirSequence, null, VendorResult.InventoryFull),

        // 9. Unique.
        new("Refuse a second copy of a unique item", [At(Bag(0), Charm)], 40, CharmSequence, null, VendorResult.UniqueAlreadyOwned),
        new("Refuse a unique item already in the bank", [At(Vault(3), Charm)], 40, CharmSequence, null, VendorResult.UniqueAlreadyOwned),
        new("Check the room before the unique rule", [.. FullBag, At(Vault(3), Charm)], 40, CharmSequence, null, VendorResult.InventoryFull),
    ];

    public static IEnumerable<object[]> BuyCaseNames => BuyCases.Select(c => new object[] { c.Name });

    private static void Sell(VendorStockState stock, uint sequence, uint count)
    {
        if (count > 0)
            stock.Take(stock.Rows.Single(r => r.Sequence == sequence), count, Now);
    }

    [Theory]
    [MemberData(nameof(BuyCaseNames))]
    public void Decide_every_buy(string name)
    {
        BuyCase row = BuyCases.Single(c => c.Name == name);
        CharacterEntity character = Arrange(row.Holding, row.Money, row.Dead);
        VendorStockState stock = Stock();
        Sell(stock, BladeSequence, row.BladesSold);
        Sell(stock, ElixirSequence, row.ElixirsSold);
        var quests = Substitute.For<IQuestProgress>();
        quests.IsMet(Arg.Any<CharacterEntity>(), GatedQuest, QuestRequirementState.Completed).Returns(row.QuestMet);
        Func<ItemTemplateId, ItemTemplate?> find = Find;
        if (row.TonicGone)
            find = id => id == Tonic.Id ? null : Find(id);
        if (row.CharmSellPrice is { } sellPrice)
        {
            var raised = new ItemTemplate
            {
                Id = Charm.Id, Name = Charm.Name, Class = Charm.Class, SubClass = Charm.SubClass,
                MaxStackSize = Charm.MaxStackSize, Flags = Charm.Flags, BuyPrice = Charm.BuyPrice, SellPrice = sellPrice,
            };
            find = id => id == Charm.Id ? raised : Find(id);
        }

        BuyDecision decision = VendorRules.DecideBuy(character, row.Shop, stock, row.Sequence, row.Count, find, quests);

        Assert.Equal(row.Expected, decision.Result);
        if (row.Expected == VendorResult.Ok)
        {
            Assert.NotNull(decision.Plan);
            Assert.Equal(row.Total, decision.Plan.TotalPrice);
            Assert.Equal(row.Count ?? 1, decision.Plan.Count);
            Assert.Equal(row.Sequence, decision.Plan.Row.Sequence);
        }
        else
        {
            Assert.Null(decision.Plan);
        }

        AssertUnchanged(character, row.Money, row.Holding.Length);
    }

    [Fact]
    public void Plan_the_cost_items_multiplied_by_the_count()
    {
        CharacterEntity character = Arrange([At(Bag(0), Tonic, 4)], 60, dead: false);

        BuyDecision decision = VendorRules.DecideBuy(character, true, Stock(), ElixirSequence, 2, Find, NoQuestProgress.Instance);

        Assert.Equal(VendorResult.Ok, decision.Result);
        Assert.Equal([new VendorCostLine(Tonic.Id, 4)], decision.Plan!.Costs);
        Assert.Equal(60UL, decision.Plan.TotalPrice);
    }

    /// <summary>
    /// An open shop with no stock (the instance keeps none) is a closed shop, not a crash: the
    /// handler passes whatever ShopAccess.TryUse left in its out parameter.
    /// </summary>
    [Fact]
    public void Answer_ShopClosed_with_the_shop_open_but_no_stock()
    {
        CharacterEntity character = Arrange([], 1000, dead: false);

        BuyDecision decision = VendorRules.DecideBuy(character, true, null, TonicSequence, null, Find, NoQuestProgress.Instance);

        Assert.Equal(new BuyDecision(VendorResult.ShopClosed, null), decision);
        AssertUnchanged(character, 1000, 0);
    }

    /// <summary>The spec's quest-gate case: with no quest system every gated row stays hidden.</summary>
    [Fact]
    public void Hide_every_gated_row_while_no_quest_can_be_met()
    {
        CharacterEntity character = Arrange([], 1000, dead: false);

        Assert.False(NoQuestProgress.Instance.IsMet(character, GatedQuest, QuestRequirementState.Completed));
        Assert.False(NoQuestProgress.Instance.IsMet(character, GatedQuest, QuestRequirementState.Active));
        Assert.Equal(VendorResult.NotFound,
            VendorRules.DecideBuy(character, true, Stock(), GatedSequence, null, Find, NoQuestProgress.Instance).Result);
    }

    // ---- Sell -----------------------------------------------------------------------------------

    public sealed record SellCase(
        string Name,
        Held[] Holding,
        uint BagSlot,
        uint? Count,
        VendorResult Expected,
        ulong Payment = 0,
        ulong Money = 0,
        ulong MaxMoney = 1_000_000,
        bool Shop = true,
        bool Dead = false);

    private static readonly SellCase[] SellCases =
    [
        new("Sell a whole stack when no count is given", [At(Bag(0), Tonic, 5)], 0, null, VendorResult.Ok, Payment: 20),
        new("Sell part of a stack", [At(Bag(0), Tonic, 5)], 0, 2, VendorResult.Ok, Payment: 8),
        new("Sell a weapon at full durability", [At(Bag(0), Blade, durability: 42)], 0, null, VendorResult.Ok, Payment: 25),
        new("Sell armour at full durability", [At(Bag(0), Plate, durability: 69)], 0, null, VendorResult.Ok, Payment: 20),
        new("Sell a potion at durability 0", [At(Bag(0), Tonic, 1, durability: 0)], 0, null, VendorResult.Ok, Payment: 4),

        // 1. Alive. 2. Shop open.
        new("Answer Dead before anything else", [], 70000, 0, VendorResult.Dead, Shop: false, Dead: true),
        new("Answer ShopClosed with no shop open", [At(Bag(0), Tonic, 5)], 3, 0, VendorResult.ShopClosed, Shop: false),

        // 3. A usable Bag slot. 4. An item there, of a sellable template.
        new("Refuse an empty bag slot", [At(Bag(0), Tonic, 5)], 3, null, VendorResult.NotFound),
        new("Refuse a slot past the bag", [], 30, null, VendorResult.NotFound),
        new("Refuse a slot no container has", [], 70000, null, VendorResult.NotFound),
        new("Sell nothing from equipment", [At(Eq(EquipmentSlots.MainHand), Blade, durability: 42)], EquipmentSlots.MainHand, null, VendorResult.NotFound),
        new("Refuse a NoSell item", [At(Bag(0), Keepsake)], 0, null, VendorResult.NotSellable),
        new("Refuse an item that sells for nothing", [At(Bag(0), Trinket)], 0, null, VendorResult.NotSellable),
        new("Refuse an item whose template is gone", [At(Bag(0), EquipTemplates.Ghost)], 0, null, VendorResult.NotSellable),

        // 5. Not damaged.
        new("Refuse a weapon one point below full", [At(Bag(0), Blade, durability: 41)], 0, null, VendorResult.Damaged),
        new("Refuse armour below full", [At(Bag(0), Plate, durability: 68)], 0, null, VendorResult.Damaged),
        new("Check the damage before the count", [At(Bag(0), Blade, durability: 41)], 0, 0, VendorResult.Damaged),

        // 6. The count.
        new("Refuse a count of 0", [At(Bag(0), Tonic, 5)], 0, 0, VendorResult.InvalidCount),
        new("Refuse a count above the stack", [At(Bag(0), Tonic, 5)], 0, 6, VendorResult.InvalidCount),

        // 7. The money cap.
        new("Reach the money cap exactly", [At(Bag(0), Tonic, 5)], 0, null, VendorResult.Ok, Payment: 20, Money: 980, MaxMoney: 1000),
        new("Refuse a sale past the money cap", [At(Bag(0), Tonic, 5)], 0, null, VendorResult.MoneyCapReached, Money: 990, MaxMoney: 1000),
        new("Refuse any sale above a lowered cap", [At(Bag(0), Tonic, 1)], 0, null, VendorResult.MoneyCapReached, Money: 2000, MaxMoney: 1000),
    ];

    public static IEnumerable<object[]> SellCaseNames => SellCases.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(SellCaseNames))]
    public void Decide_every_sale(string name)
    {
        SellCase row = SellCases.Single(c => c.Name == name);
        CharacterEntity character = Arrange(row.Holding, row.Money, row.Dead);

        SellDecision decision = VendorRules.DecideSell(character, row.Shop, row.BagSlot, row.Count, Find, row.MaxMoney);

        Assert.Equal(row.Expected, decision.Result);
        if (row.Expected == VendorResult.Ok)
        {
            Assert.Equal(new SellPlan((ushort)row.BagSlot, row.Count ?? row.Holding[0].Count, row.Payment), decision.Plan);
        }
        else
        {
            Assert.Null(decision.Plan);
        }

        AssertUnchanged(character, row.Money, row.Holding.Length);
    }

    // ---- Buyback --------------------------------------------------------------------------------

    public sealed record BuybackCase(
        string Name,
        (ItemTemplate Template, uint Count, ulong Price)[] Sold,
        Held[] Holding,
        ulong Money,
        uint Index,
        VendorResult Expected,
        bool Shop = true,
        bool Dead = false);

    private static readonly BuybackCase[] BuybackCases =
    [
        new("Buy back the newest sale", [(Tonic, 5, 20)], [], 20, 0, VendorResult.Ok),
        new("Buy back an older sale by its index", [(Tonic, 5, 20), (Blade, 1, 25)], [], 20, 1, VendorResult.Ok),
        new("Buy back an item whose template is gone", [(EquipTemplates.Ghost, 1, 5)], [], 5, 0, VendorResult.Ok),

        // 1. Alive. 2. Shop open.
        new("Answer Dead before anything else", [], [], 0, 5, VendorResult.Dead, Shop: false, Dead: true),
        new("Answer ShopClosed with no shop open", [(Tonic, 5, 20)], [], 0, 1, VendorResult.ShopClosed, Shop: false),

        // 3. The index.
        new("Refuse an index past the list", [(Tonic, 5, 20)], [], 20, 1, VendorResult.NotFound),
        new("Refuse any index when nothing was sold", [], [], 20, 0, VendorResult.NotFound),
        new("Refuse the largest index a client can send", [(Tonic, 5, 20)], [], 20, uint.MaxValue, VendorResult.NotFound),

        // 4. Gold for the price it sold for.
        new("Refuse too little gold", [(Tonic, 5, 20)], [], 19, 0, VendorResult.NotEnoughGold),

        // 5. A free Bag slot: the exact instance is never merged into a stack.
        new("Refuse a full bag", [(Tonic, 5, 20)], FullBag, 20, 0, VendorResult.InventoryFull),
        new("Never merge into a stack with room", [(Tonic, 5, 20)], [.. AllButLastSlot, At(Bag(29), Tonic, 1)], 20, 0, VendorResult.InventoryFull),

        // Not in the spec's list (#432): re-adding a sold unique item beside a newer copy would leave two.
        new("Refuse to buy back a unique item already owned again", [(Charm, 1, 12)], [At(Bag(0), Charm)], 12, 0, VendorResult.UniqueAlreadyOwned),
    ];

    public static IEnumerable<object[]> BuybackCaseNames => BuybackCases.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(BuybackCaseNames))]
    public void Decide_every_buyback(string name)
    {
        BuybackCase row = BuybackCases.Single(c => c.Name == name);
        CharacterEntity character = Arrange(row.Holding, row.Money, row.Dead);
        foreach ((ItemTemplate template, uint count, ulong price) in row.Sold)
            character.Buyback.Push(new BuybackEntry(TestCharacters.Item(0, template, count), price));

        BuybackDecision decision = VendorRules.DecideBuyback(character, row.Shop, row.Index, Find);

        Assert.Equal(row.Expected, decision.Result);
        if (row.Expected == VendorResult.Ok)
        {
            Assert.Equal(row.Index, decision.Plan!.Index);
            Assert.Same(character.Buyback.Entries[(int)row.Index], decision.Plan.Entry);
        }
        else
        {
            Assert.Null(decision.Plan);
        }

        AssertUnchanged(character, row.Money, row.Holding.Length);
        Assert.Equal(row.Sold.Length, character.Buyback.Entries.Count);
    }
}
