using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Trade;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.Inventory.TestModifiers;

namespace Avalon.Server.World.UnitTests.Trading;

/// <summary>
/// Trade requests and the open trade window (#1175): who may ask whom, what may be offered, the revision a confirm
/// must match, and every way a trade closes, which releases every lock on both sides.
/// </summary>
public class TradeServiceShould
{
    private static readonly ObjectGuid s_shopkeeper = new(ObjectType.Creature, 77);

    [Fact]
    public void Open_a_trade_for_both_when_a_request_is_accepted()
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1);
        Trader b = w.Online(2);

        Assert.Equal(TradeResult.Ok, w.Trades.Request(a.Id, b.Id));
        Assert.Equal((a.Id, 30_000u), (b.Requests().Single().CharacterId, b.Requests().Single().ExpiresInMs));

        // An accept refused by a condition (here the requester's shop window) leaves the request open until it expires.
        a.Character.OpenShopNpc = s_shopkeeper;
        Assert.Equal(TradeResult.TargetNpcWindowOpen, w.Trades.Respond(b.Id, accept: true));
        Assert.True(w.Trades.HoldsRequest(b.Id));
        a.Character.OpenShopNpc = null;

        Assert.Equal(TradeResult.Ok, w.Trades.Respond(b.Id, accept: true));
        Assert.Equal(b.Id, a.Opened().Single().PartnerCharacterId);
        Assert.Equal(a.Id, b.Opened().Single().PartnerCharacterId);
        Assert.All([a, b], t => Assert.Equal(1u, t.States().Single().Revision));
    }

    public static TheoryData<string, TradeResult> RequestRefusals => new()
    {
        { "sender dead", TradeResult.Dead },
        { "sender in combat", TradeResult.InCombat },
        { "sender has a shop open", TradeResult.NpcWindowOpen },
        { "sender already trading", TradeResult.Busy },
        { "sender holds a request", TradeResult.Busy },
        { "past the budget", TradeResult.TooFast },
        { "self", TradeResult.Self },
        { "target offline", TradeResult.NotFound },
        { "target in another instance", TradeResult.NotFound },
        { "target dead", TradeResult.TargetDead },
        { "target in combat", TradeResult.TargetInCombat },
        { "target 5.5 m away", TradeResult.TooFar },
        { "target trading", TradeResult.TargetBusy },
    };

    [Theory]
    [MemberData(nameof(RequestRefusals))]
    public void Refuse_a_request_by_the_rules(string name, TradeResult expected)
    {
        var w = new TradeTestWorld(c => c.TradeRequestsPerMinute = name == "past the budget" ? 1 : 0);
        Trader a = w.Online(1);
        Trader b = name switch
        {
            "target in another instance" => w.Online(2, instance: Guid.NewGuid()),
            "target 5.5 m away" => w.Online(2, at: new Vector3(5.5f, 0, 0)),
            _ => w.Online(2),
        };
        Trader c = w.Online(3);

        switch (name)
        {
            case "sender dead": a.Character.IsDead = true; break;
            case "sender in combat": a.Character.MarkCombat(); break;
            case "sender has a shop open": a.Character.OpenShopNpc = s_shopkeeper; break;
            case "sender already trading": w.Open(a, c); break;
            case "sender holds a request": Assert.Equal(TradeResult.Ok, w.Trades.Request(a.Id, c.Id)); break;
            case "past the budget":
                Assert.Equal(TradeResult.Ok, w.Trades.Request(a.Id, c.Id));
                Assert.Equal(TradeResult.Ok, w.Trades.Respond(c.Id, accept: false));
                break;
            case "target offline": w.Characters.Remove(b.Connection, b.Character); break;
            case "target dead": b.Character.IsDead = true; break;
            case "target in combat": b.Character.MarkCombat(); break;
            case "target trading": w.Open(b, c); break;
        }

        Assert.Equal(expected, w.Trades.Request(a.Id, name == "self" ? a.Id : b.Id));
        Assert.Empty(b.Requests());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expire_an_unanswered_request_after_30_seconds(bool ignored)
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1);
        Trader b = w.Online(2);
        if (ignored)
            b.Character.Ignores.Add(a.Id, a.Character.Name, w.Clock.GetUtcNow().UtcDateTime);

        Assert.Equal(TradeResult.Ok, w.Trades.Request(a.Id, b.Id));
        Assert.Equal(ignored ? 0 : 1, b.Requests().Count);

        w.Clock.Advance(TimeSpan.FromSeconds(29.9));
        w.Trades.Tick();
        Assert.Empty(a.Results());

        w.Clock.Advance(TimeSpan.FromSeconds(0.1));
        w.Trades.Tick();
        Assert.Equal((TradeResult.Expired, b.Character.Name), (a.Results().Single().Result, a.Results().Single().Name));
        Assert.Equal(ignored ? 0 : 1, b.Results().Count(r => r.Result == TradeResult.Expired));
        Assert.Equal(TradeResult.NoRequest, w.Trades.Respond(b.Id, true));
        Assert.Equal(TradeResult.Ok, w.Trades.Request(a.Id, b.Id));     // the slot is free again
    }

    public static TheoryData<string, TradeResult> OfferRefusals => new()
    {
        { "not trading", TradeResult.NotTrading },
        { "trade slot 15", TradeResult.InvalidSlot },
        { "bag slot 30", TradeResult.InvalidSlot },
        { "empty bag slot", TradeResult.ItemNotFound },
        { "NoTrade item", TradeResult.NotTradeable },
        { "quest item", TradeResult.NotTradeable },
        { "template gone", TradeResult.NotTradeable },
        { "attuned item", TradeResult.NotTradeable },
        { "offered twice", TradeResult.AlreadyOffered },
        { "occupied trade slot", TradeResult.TradeSlotTaken },
        { "remove from an empty trade slot", TradeResult.ItemNotFound },
        { "more gold than held", TradeResult.NotEnoughGold },
    };

    [Theory]
    [MemberData(nameof(OfferRefusals))]
    public void Refuse_an_offer_or_a_gold_amount_by_the_rules(string name, TradeResult expected)
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1, gold: 100);
        Trader b = w.Online(2);
        InventoryItem sword = Item(0, Sword);
        InventoryItem[] held =
        [
            sword, Item(1, TradeTestWorld.Contraband), Item(2, TradeTestWorld.Token),
            Item(3, new ItemTemplate { Id = new ItemTemplateId(999), Name = "Gone" }), Item(4, Potion, count: 5),
            Item(5, Sword) with { Flags = ItemInstanceFlags.Attuned },
        ];
        if (name == "attuned item")
        {
            // #1180: a woken copy is bound; a sealed rare that has not woken yet trades freely.
            InventoryItem sealedRare = Item(7, Sword) with { Rarity = ItemRarity.Rare, ItemLevel = 9 };
            ItemInstanceModifier sealedRow = Modifier(sealedRare.InstanceId, 202, 0, (0, StatType.AddedFireDamage, 2, 5));
            sealedRow.SealProgress = 9;
            a.Character.ItemModifiers.Load(
                [new ItemInstance { Id = sealedRare.InstanceId, TemplateId = Sword.Id, Identified = true, Modifiers = [sealedRow] }]);
            held = [.. held, sealedRare];
        }

        a.Character.Container(InventoryType.Bag).Load(held);

        if (name != "not trading")
            w.Open(a, b);

        // An offer the row needs in place first: the refusal below must lock nothing more.
        if (name is "offered twice" or "occupied trade slot")
            Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, bagSlot: 0, tradeSlot: 0));
        if (name == "attuned item")
            Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, bagSlot: 7, tradeSlot: 0));

        InventoryItem[] bag = a.Character.Container(InventoryType.Bag).Held.ToArray();
        bool[] lockedBefore = bag.Select(i => a.Character.Locks.IsLocked(i.InstanceId)).ToArray();

        TradeResult result = name switch
        {
            "not trading" => w.Trades.OfferItem(a.Id, 0, 0),
            "trade slot 15" => w.Trades.OfferItem(a.Id, 0, 15),
            "bag slot 30" => w.Trades.OfferItem(a.Id, 30, 0),
            "empty bag slot" => w.Trades.OfferItem(a.Id, 6, 0),
            "NoTrade item" => w.Trades.OfferItem(a.Id, 1, 0),
            "quest item" => w.Trades.OfferItem(a.Id, 2, 0),
            "template gone" => w.Trades.OfferItem(a.Id, 3, 0),
            "attuned item" => w.Trades.OfferItem(a.Id, 5, 1),
            "offered twice" => w.Trades.OfferItem(a.Id, 0, 1),
            "occupied trade slot" => w.Trades.OfferItem(a.Id, 4, 0),
            "remove from an empty trade slot" => w.Trades.RemoveItem(a.Id, 3),
            "more gold than held" => w.Trades.SetGold(a.Id, 101),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

        Assert.Equal(expected, result);
        // A refusal locks nothing: no row but the three with an offer in place has anything locked.
        Assert.Equal(lockedBefore, bag.Select(i => a.Character.Locks.IsLocked(i.InstanceId)));
        Assert.Equal(0UL, a.Character.Locks.HeldGold);
        Assert.Equal(name is "offered twice" or "occupied trade slot" or "attuned item", a.Character.Locks.Any);
    }

    /// <summary>
    /// Each state shows the offer as it now stands, each item with its rarity, item level and modifiers (#1179), and a
    /// trade slot refilled with a different item shows only that item's, though the server reuses the slot's entries.
    /// Any change resets both confirmations.
    /// </summary>
    [Fact]
    public void Show_the_offer_as_it_stands_and_refuse_a_stale_confirm()
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1);
        Trader b = w.Online(2, gold: 100);
        InventoryItem rare = Item(0, Sword) with { Rarity = ItemRarity.Rare, ItemLevel = 9 };
        InventoryItem magic = Item(1, Sword) with { Rarity = ItemRarity.Magic, ItemLevel = 4 };
        Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(a.Character).TryAddInstance(new CarriedItem(rare,
            new ItemBaseStats { DamageMin = 3, DamageMax = 7, AttackTime = 700 },
        [
            Modifier(rare.InstanceId, 10103, 0, (0, StatType.Strength, 8, null), (1, StatType.Armor, 3, null)),
            Modifier(rare.InstanceId, 203, 1, (0, StatType.AddedFireDamage, 3, 7)),
        ], 0)));
        Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(a.Character).TryAddInstance(new CarriedItem(magic, default,
            [Modifier(magic.InstanceId, 204, 0, (0, StatType.AddedColdDamage, 2, 5))], 0)));
        w.Open(a, b);

        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, 0, 0));
        Assert.Equal(2u, a.States().Last().Revision);
        Assert.Equal("Rare 9: DamageMin 3; DamageMax 7; AttackTime 700; 10103 [Strength 8.., Armor 3..] 203 [AddedFireDamage 3..7]",
            Shown(Assert.Single(b.States().Last().Partner.Items)));
        Assert.Equal(TradeResult.Ok, w.Trades.Confirm(a.Id, 2));
        Assert.True(b.States().Last().Partner.Confirmed);
        Assert.Equal(TradeResult.Ok, w.Trades.SetGold(b.Id, 10));

        Assert.All([a, b], t => Assert.False(t.States().Last().You.Confirmed || t.States().Last().Partner.Confirmed));
        Assert.Equal(3u, b.States().Last().Revision);
        Assert.Equal(TradeResult.StaleOffer, w.Trades.Confirm(a.Id, 2));
        Assert.Equal(3u, a.States().Last().Revision);              // the offer as it now stands, sent again

        int sent = b.States().Count;
        Assert.Equal(TradeResult.Ok, w.Trades.SetGold(b.Id, 10));     // no change: nothing sent, nothing reset
        Assert.Equal(sent, b.States().Count);

        Assert.Equal(TradeResult.Ok, w.Trades.Confirm(a.Id, 3));
        Assert.True(b.States().Last().Partner.Confirmed);

        // The same trade slot, now holding an item with fewer modifiers and values and no base stats: none of the rare's
        // are left.
        Assert.Equal(TradeResult.Ok, w.Trades.RemoveItem(a.Id, 0));
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, 1, 0));
        Assert.False(b.States().Last().Partner.Confirmed);
        TradeItemDto shown = Assert.Single(b.States().Last().Partner.Items);
        Assert.Equal("Magic 4: 204 [AddedColdDamage 2..5]", Shown(shown));
        Assert.Null(shown.BaseStats);

        // And back to the rare, built again from the entries the smaller item left over.
        Assert.Equal(TradeResult.Ok, w.Trades.RemoveItem(a.Id, 0));
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, 0, 0));
        Assert.Equal("Rare 9: DamageMin 3; DamageMax 7; AttackTime 700; 10103 [Strength 8.., Armor 3..] 203 [AddedFireDamage 3..7]",
            Shown(Assert.Single(b.States().Last().Partner.Items)));
    }

    /// <summary>An offered item as the client reads it: rarity, item level, its base stats, then each modifier's values.</summary>
    private static string Shown(TradeItemDto item) =>
        $"{item.Rarity} {item.ItemLevel}: " + string.Concat((item.BaseStats ?? []).Select(b => $"{b.Stat} {b.Value}; ")) +
        string.Join(" ", (item.Modifiers ?? []).Select(m =>
            $"{m.ModifierId} [{string.Join(", ", m.Values.Select(v => $"{v.Stat} {v.Value}..{v.Value2}"))}]"));

    public static TheoryData<string, TradeResult, TradeResult> Closes => new()
    {
        { "a cancels", TradeResult.Cancelled, TradeResult.CancelledByPartner },
        { "b walks 6.5 m away", TradeResult.OutOfRange, TradeResult.OutOfRange },
        { "a dies", TradeResult.Died, TradeResult.Died },
        { "b enters combat", TradeResult.EnteredCombat, TradeResult.EnteredCombat },
        { "a opens a shop", TradeResult.NpcWindowOpened, TradeResult.NpcWindowOpened },
        { "a leaves the world", TradeResult.Unknown, TradeResult.Left },
        { "a changes instance", TradeResult.Left, TradeResult.Left },
    };

    [Theory]
    [MemberData(nameof(Closes))]
    public void Close_the_trade_for_both_and_release_every_lock(string cause, TradeResult toA, TradeResult toB)
    {
        var w = new TradeTestWorld();
        Trader a = w.Online(1, gold: 100);
        Trader b = w.Online(2);
        a.Character.Container(InventoryType.Bag).Load([Item(0, Sword)]);
        b.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);
        w.Open(a, b);
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, 0, 0));
        Assert.Equal(TradeResult.Ok, w.Trades.SetGold(a.Id, 30));
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(b.Id, 0, 0));
        Assert.True(a.Character.Locks.Any && b.Character.Locks.Any);

        switch (cause)
        {
            case "a cancels":
                Assert.Equal(TradeResult.Ok, w.Trades.Cancel(a.Id));
                break;
            case "b walks 6.5 m away":
                b.Character.Position = new Vector3(6, 0, 0);
                w.Trades.Tick();
                Assert.NotNull(w.Trades.TradeOf(a.Id));                 // exactly 6 m keeps it open
                b.Character.Position = new Vector3(6.5f, 0, 0);
                w.Trades.Tick();
                break;
            case "a dies":
                a.Character.IsDead = true;
                w.Trades.Tick();
                break;
            case "b enters combat":
                b.Character.MarkCombat();
                w.Trades.Tick();
                break;
            case "a opens a shop":
                w.Trades.NpcWindowOpening(a.Character);
                break;
            case "a leaves the world":
                w.Trades.CharacterOffline(a.Connection, a.Character);
                break;
            case "a changes instance":
                a.Character.InstanceId = Guid.NewGuid();
                w.Trades.InstanceChanged(a.Connection);
                break;
        }

        Assert.Equal(toA == TradeResult.Unknown ? [] : [toA], a.Closed().Select(p => p.Result));
        Assert.Equal([toB], b.Closed().Select(p => p.Result));
        Assert.Null(w.Trades.TradeOf(a.Id));
        Assert.Null(w.Trades.TradeOf(b.Id));
        Assert.False(a.Character.Locks.Any);
        Assert.False(b.Character.Locks.Any);
    }
}
