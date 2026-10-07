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
    private static readonly DateTime s_now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ObjectGuid s_dropGuid = new(ObjectType.Loot, 1);

    private readonly GroundLootStore _store = new();
    private readonly CharacterEntity _picker = New(id: 7);
    private ulong _maxMoney = 1_000;

    private ICharacterEconomy Economy()
    {
        ICharacterEconomy economy = Substitute.For<ICharacterEconomy>();
        economy.InventoryOf(Arg.Any<CharacterEntity>()).Returns(ci => InventoryFor(ci.Arg<CharacterEntity>()));
        economy.WalletOf(Arg.Any<CharacterEntity>()).Returns(ci => new CharacterWallet(ci.Arg<CharacterEntity>(), _maxMoney));
        return economy;
    }

    private void Drop(ItemTemplateId? item = null, uint count = 1, ulong gold = 0, uint? owner = 7,
        DateTime? freeForAllAt = null, Vector3? at = null) => _store.Add(new GroundLoot
        {
            Guid = s_dropGuid,
            Position = at ?? new Vector3(1f, 0f, 1f),
            ItemTemplateId = item,
            Count = count,
            Gold = gold,
            OwnerCharacterId = owner,
            FreeForAllAt = freeForAllAt ?? s_now.AddSeconds(30),
        });

    private LootPickupOutcome PickUp(CharacterEntity? picker = null, GroundLootStore? store = null) =>
        LootPickup.TryPickUp(picker ?? _picker, store ?? _store, s_dropGuid, Range, s_now, Economy(), NullLogger.Instance);

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

    /// <summary>A drop is looked up only in the character's own instance, and nowhere when it has none.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Answer_Not_Found_For_A_Drop_Outside_The_Characters_Instance(bool noInstance)
    {
        Drop(gold: 25);   // in _store, which is not the picker's instance below

        LootPickupOutcome outcome = LootPickup.TryPickUp(_picker, noInstance ? null : new GroundLootStore(), s_dropGuid, Range,
            s_now, Economy(), NullLogger.Instance);

        Assert.Equal(new LootPickupOutcome(LootPickupResult.NotFound, Removed: false), outcome);
        Assert.Equal(1, _store.Count);
    }

    [Theory]
    [InlineData(5f, LootPickupResult.Ok)]
    [InlineData(5.1f, LootPickupResult.TooFar)]
    public void Allow_A_Pickup_Up_To_Exactly_The_Range_And_Leave_The_Drop_Beyond_It(float distance, LootPickupResult expected)
    {
        Drop(gold: 25, at: new Vector3(0f, 0f, distance));

        Assert.Equal(expected, PickUp().Result);
        Assert.Equal(expected == LootPickupResult.Ok ? 0 : 1, _store.Count);
    }

    /// <summary>Another character's drop is not yours inside the grace period; after it, or with no owner, anyone's.</summary>
    [Theory]
    [InlineData(99u, 30, LootPickupResult.NotYours)]
    [InlineData(99u, 0, LootPickupResult.Ok)]
    [InlineData(null, 0, LootPickupResult.Ok)]
    public void Keep_A_Drop_For_Its_Owner_Until_The_Grace_Period_Is_Over(uint? owner, int secondsLeft, LootPickupResult expected)
    {
        Drop(gold: 25, owner: owner, freeForAllAt: s_now.AddSeconds(secondsLeft));

        Assert.Equal(expected, PickUp().Result);
        Assert.Equal(expected == LootPickupResult.Ok ? 0 : 1, _store.Count);
    }

    [Theory]
    [InlineData(LootPickupResult.InventoryFull)]
    [InlineData(LootPickupResult.UniqueAlreadyOwned)]
    [InlineData(LootPickupResult.MoneyCapReached)]
    public void Answer_A_Refusal_And_Leave_The_Drop(LootPickupResult refusal)
    {
        switch (refusal)
        {
            case LootPickupResult.InventoryFull:
                FillBag();
                Drop(item: Potion.Id);
                break;
            case LootPickupResult.UniqueAlreadyOwned:
                _picker.Container(InventoryType.Bag).Load([Item(0, Relic)]);
                Drop(item: Relic.Id);
                break;
            default:
                _maxMoney = 10;
                Drop(gold: 25);
                break;
        }

        Assert.Equal(new LootPickupOutcome(refusal, Removed: false), PickUp());
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
        Drop(item: Sword.Id, owner: null, freeForAllAt: s_now);

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
        IInventoryService inventory = Substitute.For<IInventoryService>();
        inventory.TryAdd(Arg.Any<ItemTemplateId>(), Arg.Any<uint>()).Returns((InventoryAddResult)99);
        ICharacterEconomy economy = Substitute.For<ICharacterEconomy>();
        economy.InventoryOf(Arg.Any<CharacterEntity>()).Returns(inventory);

        var logger = new ErrorCountingLogger();

        LootPickupOutcome outcome = LootPickup.TryPickUp(_picker, _store, s_dropGuid, Range, s_now, economy, logger);

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
