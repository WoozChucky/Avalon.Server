using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Loot;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// The pickup checks, over a real character, real inventory and wallet, and a real store. Each
/// result code comes from its own condition, and the order is pinned by drops that fail two
/// checks at once.
/// </summary>
public class LootPickupShould
{
    private const float Range = 5f;
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ObjectGuid DropGuid = new(ObjectType.Loot, 1);

    private readonly GroundLootStore _store = new();
    private readonly CharacterEntity _picker = New(id: 7);
    private ulong _maxMoney = 1_000;

    private ICharacterEconomy Economy()
    {
        var economy = Substitute.For<ICharacterEconomy>();
        economy.InventoryOf(Arg.Any<CharacterEntity>()).Returns(ci => InventoryFor(ci.Arg<CharacterEntity>()));
        economy.WalletOf(Arg.Any<CharacterEntity>()).Returns(ci => new CharacterWallet(ci.Arg<CharacterEntity>(), _maxMoney));
        return economy;
    }

    private void Drop(ItemTemplateId? item = null, uint count = 1, ulong gold = 0, uint? owner = 7,
        DateTime? freeForAllAt = null, Vector3? at = null) => _store.Add(new GroundLoot
    {
        Guid = DropGuid,
        Position = at ?? new Vector3(1f, 0f, 1f),
        ItemTemplateId = item,
        Count = count,
        Gold = gold,
        OwnerCharacterId = owner,
        FreeForAllAt = freeForAllAt ?? Now.AddSeconds(30),
    });

    private LootPickupOutcome PickUp(CharacterEntity? picker = null, GroundLootStore? store = null) =>
        LootPickup.TryPickUp(picker ?? _picker, store ?? _store, DropGuid, Range, Now, Economy(), NullLogger.Instance);

    private void FillBag() => _picker.Container(InventoryType.Bag)
        .Load(Enumerable.Range(0, 30).Select(s => Item((ushort)s, Sword)).ToList());

    [Fact]
    public void Put_An_Item_In_The_Bag_Remove_The_Drop_And_Mark_The_Save()
    {
        Drop(item: Potion.Id, count: 3);

        Assert.Equal(new LootPickupOutcome(LootPickupResult.Ok, Removed: true), PickUp());

        Assert.Equal(0, _store.Count);
        Assert.Equal(3u, At(_picker, InventoryType.Bag, 0).Count);
        Assert.True(_picker.SaveState.HasChanges);
        Assert.True(_picker.ClientChanges.HasChanges);
    }

    [Fact]
    public void Put_Gold_In_The_Wallet_Remove_The_Pile_And_Mark_The_Save()
    {
        Drop(gold: 25);

        Assert.Equal(LootPickupResult.Ok, PickUp().Result);

        Assert.Equal(0, _store.Count);
        Assert.Equal(25UL, _picker.Data!.Money);
        Assert.True(_picker.SaveState.MoneyDirty);
        Assert.True(_picker.ClientChanges.MoneyChanged);
    }

    [Fact]
    public void Answer_Not_Found_For_A_Drop_That_Is_Not_There()
    {
        Assert.Equal(new LootPickupOutcome(LootPickupResult.NotFound, Removed: false), PickUp());
    }

    [Fact]
    public void Answer_Not_Found_For_A_Drop_In_Another_Instance()
    {
        Drop(gold: 25);   // in _store, which is not the picker's instance below

        Assert.Equal(LootPickupResult.NotFound, PickUp(store: new GroundLootStore()).Result);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Answer_Not_Found_When_The_Character_Has_No_Instance()
    {
        Drop(gold: 25);

        Assert.Equal(LootPickupResult.NotFound,
            LootPickup.TryPickUp(_picker, null, DropGuid, Range, Now, Economy(), NullLogger.Instance).Result);
    }

    [Fact]
    public void Answer_Too_Far_Beyond_The_Pickup_Range_And_Leave_The_Drop()
    {
        Drop(gold: 25, at: new Vector3(0f, 0f, 5.1f));

        Assert.Equal(LootPickupResult.TooFar, PickUp().Result);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Allow_A_Pickup_Exactly_At_The_Range()
    {
        Drop(gold: 25, at: new Vector3(0f, 0f, 5f));

        Assert.Equal(LootPickupResult.Ok, PickUp().Result);
    }

    [Fact]
    public void Answer_Not_Yours_To_Another_Character_Inside_The_Grace_Period()
    {
        Drop(gold: 25, owner: 99);

        Assert.Equal(LootPickupResult.NotYours, PickUp().Result);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Let_Another_Character_Take_It_Once_The_Grace_Period_Is_Over()
    {
        Drop(gold: 25, owner: 99, freeForAllAt: Now);

        Assert.Equal(LootPickupResult.Ok, PickUp().Result);
    }

    [Fact]
    public void Let_Anyone_Take_A_Drop_Nobody_Owns()
    {
        Drop(gold: 25, owner: null, freeForAllAt: Now);

        Assert.Equal(LootPickupResult.Ok, PickUp().Result);
    }

    [Fact]
    public void Answer_Inventory_Full_And_Leave_The_Drop()
    {
        FillBag();
        Drop(item: Potion.Id);

        Assert.Equal(new LootPickupOutcome(LootPickupResult.InventoryFull, Removed: false), PickUp());
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Answer_Unique_Already_Owned_And_Leave_The_Drop()
    {
        _picker.Container(InventoryType.Bag).Load([Item(0, Relic)]);
        Drop(item: Relic.Id);

        Assert.Equal(LootPickupResult.UniqueAlreadyOwned, PickUp().Result);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Answer_Money_Cap_Reached_And_Leave_The_Pile()
    {
        _maxMoney = 10;
        Drop(gold: 25);

        Assert.Equal(LootPickupResult.MoneyCapReached, PickUp().Result);
        Assert.Equal(1, _store.Count);
        Assert.Equal(0UL, _picker.Data!.Money);
    }

    [Fact]
    public void Remove_A_Drop_Whose_Item_Template_No_Longer_Exists()
    {
        // 999 is in no template list: an Items reload removed it after the kill rolled it.
        Drop(item: new ItemTemplateId(999));

        Assert.Equal(new LootPickupOutcome(LootPickupResult.NotFound, Removed: true), PickUp());
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void Give_Only_The_First_Of_Two_Pickups_Of_The_Same_Drop()
    {
        CharacterEntity other = New(id: 8);
        Drop(item: Sword.Id, owner: null, freeForAllAt: Now);

        Assert.Equal(LootPickupResult.Ok, PickUp().Result);
        Assert.Equal(LootPickupResult.NotFound, PickUp(picker: other).Result);
        Assert.Empty(other.Container(InventoryType.Bag).Items);
    }

    [Fact]
    public void Answer_Too_Far_When_The_Distance_Is_Not_A_Number()
    {
        // A NaN position makes every comparison false, so "farther than the range" alone would let
        // it through from anywhere.
        _picker.Position = new Vector3(float.NaN, 0f, 0f);
        Drop(gold: 25);

        Assert.Equal(new LootPickupOutcome(LootPickupResult.TooFar, Removed: false), PickUp());
        Assert.Equal(1, _store.Count);
        Assert.Equal(0UL, _picker.Data!.Money);
    }

    [Fact]
    public void Answer_Not_Found_And_Keep_The_Drop_For_An_Add_Result_It_Does_Not_Know()
    {
        Drop(item: Potion.Id);
        var inventory = Substitute.For<IInventoryService>();
        inventory.TryAdd(Arg.Any<ItemTemplateId>(), Arg.Any<uint>()).Returns((InventoryAddResult)99);
        var economy = Substitute.For<ICharacterEconomy>();
        economy.InventoryOf(Arg.Any<CharacterEntity>()).Returns(inventory);

        var logger = new ErrorCountingLogger();

        LootPickupOutcome outcome = LootPickup.TryPickUp(_picker, _store, DropGuid, Range, Now, economy, logger);

        Assert.Equal(new LootPickupOutcome(LootPickupResult.NotFound, Removed: false), outcome);
        Assert.Equal(1, _store.Count);
        Assert.Equal(1, logger.Errors);
    }

    [Fact]
    public void Check_Range_Before_Ownership()
    {
        Drop(gold: 25, owner: 99, at: new Vector3(0f, 0f, 50f));

        Assert.Equal(LootPickupResult.TooFar, PickUp().Result);
    }

    [Fact]
    public void Check_Ownership_Before_The_Bag()
    {
        FillBag();
        Drop(item: Potion.Id, owner: 99);

        Assert.Equal(LootPickupResult.NotYours, PickUp().Result);
    }

    private sealed class ErrorCountingLogger : ILogger
    {
        public int Errors { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors++;
        }
    }
}
