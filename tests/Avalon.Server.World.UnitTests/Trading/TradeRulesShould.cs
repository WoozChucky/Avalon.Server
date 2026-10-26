using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Trade;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Trading;

namespace Avalon.Server.World.UnitTests.Trading;

/// <summary>
/// The completion checks of a trade, in their order (the first failure answers), and where the received items land.
/// TradeRules only plans, so these read its decision and nothing else.
/// </summary>
public class TradeRulesShould
{
    private static readonly ItemTemplate s_contraband = new()
    { Id = new ItemTemplateId(9001), Name = "Contraband", Class = ItemClass.Junk, MaxStackSize = 1, Flags = ItemTemplateFlags.NoTrade };

    private static readonly ItemTemplate s_token = new()
    { Id = new ItemTemplateId(9002), Name = "Token", Class = ItemClass.Quest, MaxStackSize = 1, Flags = ItemTemplateFlags.QuestItem };

    private const ulong NoCap = 1_000_000;

    private static ItemTemplate? Find(ItemTemplateId id) =>
        id == s_contraband.Id ? s_contraband : id == s_token.Id ? s_token : TestCharacters.Find(id);

    private static CharacterEntity Holding(uint id, ulong gold, IEnumerable<InventoryItem> bag, IEnumerable<InventoryItem>? bank = null)
    {
        CharacterEntity character = TestCharacters.New(id, gold);
        character.Container(InventoryType.Bag).Load(bag.ToList());
        if (bank is not null)
            character.Container(InventoryType.Bank).Load(bank.ToList());

        return character;
    }

    private static InventoryItem[] Pebbles(int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(s => TestCharacters.Item((byte)s, TestCharacters.Pebble)).ToArray();

    [Fact]
    public void Place_received_items_in_the_lowest_slots_left_free_once_the_outgoing_items_leave()
    {
        InventoryItem sword = TestCharacters.Item(3, TestCharacters.Sword);
        CharacterEntity a = Holding(1, 100, Pebbles(0, 2).Append(sword).Concat(Pebbles(4, 28)));

        InventoryItem potion = TestCharacters.Item(0, TestCharacters.Potion, 5);
        InventoryItem pebble = TestCharacters.Item(1, TestCharacters.Pebble);
        CharacterEntity b = Holding(2, 0, [potion, pebble]);

        TradeDecision d = TradeRules.Plan(new TradeOffer(a, [sword], 40), new TradeOffer(b, [potion, pebble], 0), Find, NoCap);

        Assert.Equal(TradeResult.Ok, d.Result);
        Assert.Equal([3, 29], d.Plan!.First.Incoming.Select(i => (int)i.Item.Slot));
        Assert.Equal(potion.InstanceId, d.Plan.First.Incoming[0].Item.InstanceId);
        Assert.Equal(5u, d.Plan.First.Incoming[0].Item.Count);
        Assert.Equal([0], d.Plan.Second.Incoming.Select(i => (int)i.Item.Slot));
        Assert.Equal((40UL, 0UL), (d.Plan.First.GoldOut, d.Plan.First.GoldIn));
        Assert.Equal((0UL, 40UL), (d.Plan.Second.GoldOut, d.Plan.Second.GoldIn));
    }

    public static TheoryData<string, TradeResult, int> Refusals() => new()
    {
        { "offered item moved from its slot", TradeResult.OfferInvalid, 0 },
        { "offered item now NoTrade (a reload)", TradeResult.OfferInvalid, 0 },
        { "offered item is a quest item", TradeResult.OfferInvalid, 1 },
        { "offered item woke and became Attuned after the offer", TradeResult.OfferInvalid, 1 },
        { "giver holds less gold than offered", TradeResult.OfferInvalid, 0 },
        { "receiver bag full after its own leave", TradeResult.BagFull, 1 },
        { "receiver would own two Unique Relics", TradeResult.UniqueAlreadyOwned, 1 },
        { "giving the only Relic away and receiving one", TradeResult.Ok, -1 },
        { "receiver would pass Game:MaxGold", TradeResult.GoldCapReached, 1 },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Refuse_a_completion_by_the_rules(string name, TradeResult expected, int failingSide)
    {
        (TradeOffer first, TradeOffer second, ulong maxGold) = Arrange(name);

        TradeDecision d = TradeRules.Plan(first, second, Find, maxGold);

        Assert.Equal((expected, failingSide), (d.Result, d.FailingSide));
        Assert.Equal(expected == TradeResult.Ok, d.Plan is not null);
    }

    private static (TradeOffer First, TradeOffer Second, ulong MaxGold) Arrange(string name)
    {
        InventoryItem sword = TestCharacters.Item(4, TestCharacters.Sword);
        InventoryItem potion = TestCharacters.Item(5, TestCharacters.Potion, 3);
        InventoryItem relicA = TestCharacters.Item(6, TestCharacters.Relic);
        InventoryItem relicB = TestCharacters.Item(0, TestCharacters.Relic);
        InventoryItem contraband = TestCharacters.Item(7, s_contraband);
        InventoryItem token = TestCharacters.Item(0, s_token);
        CharacterEntity a = Holding(1, 50, [sword, potion, relicA, contraband]);

        switch (name)
        {
            case "offered item moved from its slot":
                return (new TradeOffer(a, [sword with { Slot = 3 }], 0), new TradeOffer(Holding(2, 0, []), [], 0), NoCap);

            case "offered item now NoTrade (a reload)":
                return (new TradeOffer(a, [contraband], 0), new TradeOffer(Holding(2, 0, []), [], 0), NoCap);

            case "offered item is a quest item":
                return (new TradeOffer(a, [sword], 0), new TradeOffer(Holding(2, 0, [token]), [token], 0), NoCap);

            case "offered item woke and became Attuned after the offer":
                InventoryItem bound = TestCharacters.Item(0, TestCharacters.Sword);
                CharacterEntity woken = Holding(2, 0, [bound with { Flags = ItemInstanceFlags.Attuned }]);
                return (new TradeOffer(a, [sword], 0), new TradeOffer(woken, [bound], 0), NoCap);

            case "giver holds less gold than offered":
                return (new TradeOffer(a, [], 51), new TradeOffer(Holding(2, 0, []), [], 0), NoCap);

            case "receiver bag full after its own leave":
                CharacterEntity full = Holding(2, 0, Pebbles(0, 29));
                return (new TradeOffer(a, [sword, potion], 0), new TradeOffer(full, [TestCharacters.At(full, InventoryType.Bag, 0)], 0), NoCap);

            case "receiver would own two Unique Relics":
                return (new TradeOffer(a, [relicA], 0), new TradeOffer(Holding(2, 0, [], [relicB]), [], 0), NoCap);

            case "giving the only Relic away and receiving one":
                return (new TradeOffer(a, [relicA], 0), new TradeOffer(Holding(2, 0, [relicB]), [relicB], 0), NoCap);

            case "receiver would pass Game:MaxGold":
                return (new TradeOffer(a, [], 11), new TradeOffer(Holding(2, 90, []), [], 0), 100);

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }
}
