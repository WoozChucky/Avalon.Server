using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Loot;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.Inventory.TestModifiers;

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
    private ulong _maxGold = 1_000;
    private Func<ItemTemplateId, ItemTemplate?> _templates = Find;

    private ICharacterEconomy Economy()
    {
        ICharacterEconomy economy = Substitute.For<ICharacterEconomy>();
        economy.InventoryOf(Arg.Any<CharacterEntity>()).Returns(ci => InventoryFor(ci.Arg<CharacterEntity>(), _templates));
        economy.WalletOf(Arg.Any<CharacterEntity>()).Returns(ci => new CharacterWallet(ci.Arg<CharacterEntity>(), _maxGold));
        return economy;
    }

    private void Drop(ItemTemplateId? item = null, uint count = 1, ulong gold = 0, uint? owner = 7,
        DateTime? freeForAllAt = null, Vector3? at = null, RolledItem? rolled = null) => _store.Add(new GroundLoot
        {
            Guid = s_dropGuid,
            Position = at ?? new Vector3(1f, 0f, 1f),
            ItemTemplateId = item,
            Count = count,
            Rolled = rolled,
            Gold = gold,
            OwnerCharacterId = owner,
            FreeForAllAt = freeForAllAt ?? s_now.AddSeconds(30),
        });

    private LootPickupOutcome PickUp(CharacterEntity? picker = null, GroundLootStore? store = null) =>
        LootPickup.TryPickUp(picker ?? _picker, store ?? _store, s_dropGuid, Range, s_now, Economy(), NullLogger.Instance);

    private void FillBag() => _picker.Container(InventoryType.Bag)
        .Load(Enumerable.Range(0, 30).Select(s => Item((byte)s, Sword)).ToList());

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

    /// <summary>
    /// #1179: a drop that rolled its copy is that copy until it is picked up. A picker with a full Bag is refused and the
    /// copy stays on the ground as it lay; the next picker gets exactly it (its id, rarity, item level, name, base stats
    /// and modifier rows), and the next save writes it with them.
    /// </summary>
    [Fact]
    public void Put_the_ground_instance_in_the_bag_exactly()
    {
        ItemTemplate chest = ScenarioReferenceData.Shared.Data.FindItemTemplate(new ItemTemplateId(37))!;
        _templates = id => id == chest.Id ? chest : Find(id);
        var id = new ItemInstanceId(Guid.CreateVersion7());
        List<ItemInstanceModifier> modifiers =
        [
            Modifier(id, CatalogKeys.ModifierId("P11 T9"), 0, (0, StatType.Health, 2, null)),
            Modifier(id, CatalogKeys.ModifierId("P2 T7"), 1, (0, StatType.AddedFireDamage, 2, 3)),
            Modifier(id, CatalogKeys.ModifierId("S1 T7"), 2, (0, StatType.Strength, 3, null)),
        ];
        var baseStats = new ItemBaseStats { Armour = 9 };
        var rolled = new RolledItem(id, chest.Id, ItemRarity.Rare, 9, "Thorn Bite", baseStats, modifiers);
        Drop(item: chest.Id, owner: null, freeForAllAt: s_now, rolled: rolled);
        string asRolled = Describe(modifiers);

        CharacterEntity full = New(id: 8);
        full.Container(InventoryType.Bag).Load(Enumerable.Range(0, 30).Select(s => Item((byte)s, Sword)).ToList());
        Assert.Equal(new LootPickupOutcome(LootPickupResult.InventoryFull, Removed: false), PickUp(picker: full));
        Assert.True(_store.TryGet(s_dropGuid, out GroundLoot? left));
        Assert.Same(rolled, left.Rolled);
        Assert.Equal(asRolled, Describe(rolled.Modifiers));

        Assert.Equal(new LootPickupOutcome(LootPickupResult.Ok, Removed: true), PickUp());

        InventoryItem bagged = At(_picker, InventoryType.Bag, 0);
        Assert.Equal((id, chest.Id, 1u, ItemRarity.Rare, (ushort)9, "Thorn Bite"),
            (bagged.InstanceId, bagged.TemplateId, bagged.Count, bagged.Rarity, bagged.ItemLevel, bagged.RareName));
        Assert.Equal(asRolled, Describe(_picker.ItemModifiers.Of(id)));
        Assert.Equal(baseStats, _picker.ItemModifiers.BaseStatsOf(id));

        CharacterSaveBatch save = CharacterSaveSnapshot.Take(_picker).Batch;
        ItemInstance row = Assert.Single(save.UpsertItems);
        Assert.Equal((id, ItemRarity.Rare, (ushort)9, "Thorn Bite"), (row.Id, row.Rarity, row.ItemLevel, row.RareName));
        ItemModifierWrite written = save.Modifiers!;
        Assert.Equal(baseStats, ItemBaseStats.Of(written.BaseStats.Where(s => s.ItemInstanceId == id)));
        Assert.Equal(asRolled, Describe([.. written.Modifiers.Where(m => m.ItemInstanceId == id).OrderBy(m => m.Ordinal)
            .Select(m => new ItemInstanceModifier
            {
                Id = m.Id,
                ModifierId = m.ModifierId,
                Ordinal = m.Ordinal,
                Values = [.. written.Values.Where(v => v.ItemInstanceModifierId == m.Id).OrderBy(v => v.Index)],
            })]));
    }

    [Fact]
    public void Put_Gold_In_The_Wallet_Remove_The_Pile_And_Mark_The_Save()
    {
        Drop(gold: 25);

        Assert.Equal(LootPickupResult.Ok, PickUp().Result);

        Assert.Equal(0, _store.Count);
        Assert.Equal(25UL, _picker.Data!.Gold);
        Assert.True(_picker.SaveState.GoldDirty);
        Assert.True(_picker.ClientChanges.GoldChanged);
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
    [InlineData(LootPickupResult.GoldCapReached)]
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
                _maxGold = 10;
                Drop(gold: 25);
                break;
        }

        Assert.Equal(new LootPickupOutcome(refusal, Removed: false), PickUp());
        Assert.Equal(1, _store.Count);
        Assert.Equal(0UL, _picker.Data!.Gold);
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
        Assert.Equal(0UL, _picker.Data!.Gold);
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
