using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.ChunkLayouts;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Loot;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Avalon.World.Respawn;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// All-or-nothing adds and removals over a character's containers (spec #459 section 2). Every
/// successful change marks its save state and its client change; a refused one touches neither.
/// </summary>
public class CharacterInventoryServiceShould
{
    [Fact]
    public void Fill_partial_stacks_in_slot_order_before_taking_free_slots()
    {
        CharacterEntity character = New();
        InventoryItem low = Item(2, Potion, count: 15), high = Item(5, Potion, count: 18);
        character.Container(InventoryType.Bag).Load([low, high]);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 10));

        Assert.Equal(20u, At(character, InventoryType.Bag, 2).Count);
        Assert.Equal(20u, At(character, InventoryType.Bag, 5).Count);
        Assert.Equal(3u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(3, character.Container(InventoryType.Bag).Items.Count);
    }

    [Fact]
    public void Start_one_new_stack_per_free_slot_lowest_first()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, Sword)]);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 45));

        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
        Assert.Equal(20u, At(character, InventoryType.Bag, 2).Count);
        Assert.Equal(5u, At(character, InventoryType.Bag, 3).Count);
        Assert.Equal(3, new[] { 1, 2, 3 }.Select(s => At(character, InventoryType.Bag, (byte)s).InstanceId).Distinct().Count());
    }

    /// <summary>
    /// Two items with room for one: two potions past a partial stack and no slot free, or two copies of a ring base that
    /// rolls its implicit, with one slot free (#1179: a copy carrying modifiers is one item in a slot of its own, whatever
    /// its template stacks to, and this ring's says 20).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refuse_an_add_that_does_not_fit_whole_and_change_nothing(bool rolls)
    {
        CharacterEntity character = New();
        var bag = Enumerable.Range(0, 29).Select(s => Item((byte)s, Sword)).ToList();
        if (!rolls)
            bag.Add(Item(29, Potion, count: 19));
        character.Container(InventoryType.Bag).Load(bag);
        ItemModifierCatalog? linked = rolls ? RingLinkingAnImplicit() : null;
        ItemTemplate added = rolls ? s_stackedRing : Potion;
        ItemTemplate? Find(ItemTemplateId id) => id == s_stackedRing.Id ? s_stackedRing : TestCharacters.Find(id);

        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character, Find, modifiers: linked).CanAdd(added.Id, 2));
        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character, Find, modifiers: linked).TryAdd(added.Id, 2));

        Assert.Equal(rolls ? 29 : 30, character.Container(InventoryType.Bag).Items.Count());
        if (!rolls)
            Assert.Equal(19u, At(character, InventoryType.Bag, 29).Count);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    // A Common ring base whose template says it stacks to 20.
    private static readonly ItemTemplate s_stackedRing = new()
    {
        Id = new ItemTemplateId(600),
        Name = "Ring",
        Class = ItemClass.Armor,
        Slot = ItemSlotType.Finger,
        BaseKind = ItemBaseKind.Ring,
        Rarity = ItemRarity.Common,
        MaxStackSize = 20,
    };

    /// <summary>A catalog with one implicit modifier, which the ring base links.</summary>
    private static ItemModifierCatalog RingLinkingAnImplicit()
    {
        var group = new ItemModifierGroup { Id = 1, Kind = ItemModifierKind.Implicit, Name = s_stackedRing.Name };
        var modifier = new ItemModifier
        {
            Id = 1,
            GroupId = 1,
            Group = group,
            Tier = 9,
            RequiredItemLevel = 1,
            Stats = [new ItemModifierStat { ModifierId = 1, Index = 0, Stat = StatType.Health, Min = 1, Max = 2 }],
        };
        return new ItemModifierCatalog([modifier],
            [new ItemTemplateModifier { TemplateId = s_stackedRing.Id, Index = 0, ModifierId = 1 }], NullLogger.Instance);
    }

    [Theory]
    [InlineData(InventoryType.Bag)]
    [InlineData(InventoryType.Equipment)]
    [InlineData(InventoryType.Bank)]
    public void Refuse_a_second_copy_of_a_unique_item_wherever_the_first_is(InventoryType heldIn)
    {
        CharacterEntity character = New();
        character.Container(heldIn).Load([Item(0, Relic)]);

        Assert.Equal(InventoryAddResult.UniqueAlreadyOwned, InventoryFor(character).TryAdd(Relic.Id, 1));
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Refuse_more_than_one_copy_of_a_unique_item_in_one_add()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.UniqueAlreadyOwned, InventoryFor(character).TryAdd(Relic.Id, 2));
        Assert.Empty(character.Container(InventoryType.Bag).Items);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Relic.Id, 1));
    }

    /// <summary>Review Focus 4.</summary>
    [Fact]
    public void Treat_a_max_stack_size_of_zero_as_one()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Pebble.Id, 3));

        Assert.Equal(3, character.Container(InventoryType.Bag).Items.Count);
        Assert.All(character.Container(InventoryType.Bag).Items, item => Assert.Equal(1u, item.Count));
    }

    /// <summary>Review Focus 4: a stack above the template's maximum (a lowered template) is never topped up.</summary>
    [Fact]
    public void Leave_an_overfull_stack_alone()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 25)]);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 1));

        Assert.Equal(25u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(1u, At(character, InventoryType.Bag, 1).Count);
    }

    /// <summary>
    /// Review Focus 3, with room that only a wider integer can count: two part-filled stacks of a
    /// template whose stacks hold <see cref="uint.MaxValue" /> have room for almost twice that, which
    /// summed as a uint wraps below the count and would refuse an add that fits.
    /// </summary>
    [Fact]
    public void Count_room_past_the_largest_uint_without_wrapping()
    {
        CharacterEntity character = New();
        List<InventoryItem> bag = [Item(0, Hoard, count: 1), Item(1, Hoard, count: 1)];
        bag.AddRange(Enumerable.Range(2, 28).Select(s => Item((byte)s, Sword)));
        character.Container(InventoryType.Bag).Load(bag);

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Hoard.Id, uint.MaxValue));

        Assert.Equal(uint.MaxValue, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(2u, At(character, InventoryType.Bag, 1).Count);
        Assert.Equal(30, character.Container(InventoryType.Bag).Items.Count);
    }

    /// <summary>Review Focus 3: one short of fitting is refused whole, and nothing moves.</summary>
    [Fact]
    public void Refuse_a_widest_stack_count_one_short_of_fitting_and_change_nothing()
    {
        CharacterEntity character = New();
        List<InventoryItem> bag = [Item(0, Hoard, count: 1)];
        bag.AddRange(Enumerable.Range(1, 29).Select(s => Item((byte)s, Sword)));
        character.Container(InventoryType.Bag).Load(bag);

        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character).CanAdd(Hoard.Id, uint.MaxValue));
        Assert.Equal(InventoryAddResult.InventoryFull, InventoryFor(character).TryAdd(Hoard.Id, uint.MaxValue));

        Assert.Equal(1u, At(character, InventoryType.Bag, 0).Count);
        Assert.Equal(30, character.Container(InventoryType.Bag).Items.Count);
        Assert.False(character.SaveState.HasChanges);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Change_nothing_for_a_count_of_zero()
    {
        CharacterEntity character = New();

        Assert.Equal(InventoryAddResult.Ok, InventoryFor(character).TryAdd(Potion.Id, 0));
        Assert.Equal(InventoryRemoveResult.NotFound, InventoryFor(character).TryRemove(Potion.Id, 0));

        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Remove_by_template_from_the_highest_bag_slot_first()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(1, Potion, count: 20), Item(4, Potion, count: 20), Item(7, Potion, count: 5)]);

        Assert.Equal(InventoryRemoveResult.Ok, InventoryFor(character).TryRemove(Potion.Id, 12));

        Assert.False(character.Container(InventoryType.Bag).TryGet(7, out _));
        Assert.Equal(13u, At(character, InventoryType.Bag, 4).Count);
        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
    }

    [Fact]
    public void Report_not_enough_and_remove_nothing()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Bag).Load([Item(1, Potion, count: 20), Item(4, Potion, count: 5)]);

        Assert.Equal(InventoryRemoveResult.NotEnough, InventoryFor(character).TryRemove(Potion.Id, 26));

        Assert.Equal(20u, At(character, InventoryType.Bag, 1).Count);
        Assert.Equal(5u, At(character, InventoryType.Bag, 4).Count);
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Look_only_in_the_bag_when_removing_by_template()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Equipment).Load([Item(0, Sword)]);

        Assert.Equal(InventoryRemoveResult.NotFound, InventoryFor(character).TryRemove(Sword.Id, 1));
        Assert.Single(character.Container(InventoryType.Equipment).Items);
    }

    [Fact]
    public void Remove_by_instance_in_part_then_whole()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(3, Potion, count: 20);
        character.Container(InventoryType.Bag).Load([potion]);
        CharacterInventoryService inventory = InventoryFor(character);

        Assert.Equal(InventoryRemoveResult.NotEnough, inventory.TryRemove(potion.InstanceId, 21));
        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(potion.InstanceId, 5));
        Assert.Equal(15u, At(character, InventoryType.Bag, 3).Count);
        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(potion.InstanceId, 15));
        Assert.False(character.Container(InventoryType.Bag).TryGet(3, out _));
        Assert.Equal(InventoryRemoveResult.NotFound, inventory.TryRemove(potion.InstanceId, 1));
    }

    [Fact]
    public void Mark_what_each_change_does_to_items_and_slots()
    {
        CharacterEntity character = New();
        InventoryItem loaded = Item(0, Potion, count: 10);
        character.Container(InventoryType.Bag).Load([loaded]);
        CharacterInventoryService inventory = InventoryFor(character);

        inventory.TryAdd(Potion.Id, 15);   // tops slot 0 up to 20, then 5 into slot 1
        InventoryItem created = At(character, InventoryType.Bag, 1);

        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(loaded.InstanceId));
        Assert.Equal(SaveState.Unchanged, character.SaveState.SlotState(InventoryType.Bag, 0));
        Assert.Equal(SaveState.New, character.SaveState.ItemState(created.InstanceId));
        Assert.Equal(SaveState.New, character.SaveState.SlotState(InventoryType.Bag, 1));

        inventory.TryRemove(loaded.InstanceId, 20);

        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(loaded.InstanceId));
        Assert.Equal(SaveState.Removed, character.SaveState.SlotState(InventoryType.Bag, 0));
    }

    [Fact]
    public void Give_a_new_weapon_the_durability_character_creation_gives_one()
    {
        CharacterEntity character = New();

        InventoryFor(character).TryAdd(Sword.Id, 1);

        Assert.Equal(ItemInstanceDefaults.InitialDurability(Sword), At(character, InventoryType.Bag, 0).Durability);
        Assert.Equal(42u, At(character, InventoryType.Bag, 0).Durability);
    }

    /// <summary>
    /// The tracker cannot enforce this itself: ItemChanged on a Removed item would bring a deleted
    /// row back on the next save. Nothing after a removal may touch the removed instance again.
    /// </summary>
    [Fact]
    public void Never_mark_a_removed_item_changed_again()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(0, Potion, count: 5);
        InventoryItem other = Item(1, Potion, count: 5);
        character.Container(InventoryType.Bag).Load([potion, other]);
        CharacterInventoryService inventory = InventoryFor(character);

        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(potion.InstanceId, 5));

        Assert.Equal(InventoryRemoveResult.NotFound, inventory.TryRemove(potion.InstanceId, 1));
        Assert.Equal(InventoryRemoveResult.NotFound, inventory.TryRemove(potion.InstanceId, 0));
        Assert.Equal(InventoryAddResult.Ok, inventory.TryAdd(Potion.Id, 20));
        Assert.Equal(InventoryRemoveResult.Ok, inventory.TryRemove(Potion.Id, 3));

        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(potion.InstanceId));
        Assert.NotEqual(potion.InstanceId, At(character, InventoryType.Bag, 0).InstanceId);
    }

    /// <summary>
    /// #1179: however a copy of a base is made (bought, given by an item script, paid as a quest's reward, or handed to a
    /// new character), it carries its base stats, exactly the stats its template has a range for, each rolled within
    /// its range, and its base's implicits: one modifier per link, in link order, ordinals from 0, every value within its
    /// line's range. The copy is marked, and the write that stores it writes both. A quest's reward is rolled Magic, so its
    /// affixes follow the implicits, and it arrives unidentified; every other copy carries the implicits alone and arrives
    /// identified, a unique an item script adds included, which carries its base's implicit and then its own line (its
    /// base, a ring, has no base stats). Potion 1, made the same way, carries neither, is not marked and is identified.
    /// </summary>
    [Theory]
    [InlineData("vendor purchase")]
    [InlineData("item script add")]
    [InlineData("item script add of a unique")]
    [InlineData("quest reward")]
    [InlineData("starting item")]
    public async Task Give_every_copy_its_base_stats_and_implicits_however_it_is_made(string how)
    {
        const uint QuestId = 7901, KillId = 79011;
        using var seed = SqliteDatabase.World();
        ItemTemplateId made = how switch
        {
            "vendor purchase" or "starting item" => new ItemTemplateId(37),
            "item script add of a unique" => ItemBaseSeed.Equipment().Single(t => t.Name == "Thornheart Signet").Id,
            _ => new ItemTemplateId(70),
        };
        var potion = new ItemTemplateId(1);
        QuestTestWorld w = await QuestTestWorld.CreateAsync(
            [Quest(QuestId).WithStage(0, Kill(KillId, Boar, 1)).Paying(made.Value, 1).Paying(potion.Value, 1)],
            repositories: r => r with
            {
                Items = new ItemTemplateRepository(seed),
                ItemModifiers = new ItemModifierRepository(seed, NullLoggerFactory.Instance),
                ClassStats = new ClassLevelStatRepository(seed),
                CreateInfos = StartingWith(made, potion),
            });
        QuestClient c = w.Join(gold: 1000);

        List<MadeCopy> copies;
        switch (how)
        {
            case "vendor purchase":
                // Hilde's row of the plate chest, and a potion row beside it.
                var stock = new VendorStockState(new CreatureTemplateId(13),
                [
                    new VendorStockView(1, 1, made, null, null, null, null, []),
                    new VendorStockView(2, 2, potion, null, null, null, null, []),
                ]);
                VendorTrade vendor = w.Economy.VendorOf(c.Character);
                Assert.Equal(VendorResult.Ok, vendor.TryBuy(true, stock, 1, 1, NoQuestProgress.Instance, DateTime.UnixEpoch));
                Assert.Equal(VendorResult.Ok, vendor.TryBuy(true, stock, 2, 1, NoQuestProgress.Instance, DateTime.UnixEpoch));
                copies = HeldAndWritten(c.Character);
                break;
            case "item script add" or "item script add of a unique":
                ItemTemplate used = w.Data.FindItemTemplate(potion)!;
                var tools = new ItemUseTools(w.World, w.Economy,
                    new TownReturn(NullLogger.Instance, w.World, Substitute.For<IRespawnTargetResolver>(), InlineDatabaseWork.Instance),
                    new MapTeleport(NullLogger<MapTeleport>.Instance, w.World, Substitute.For<ICharacterSaver>()),
                    Substitute.For<ICreaturePlacementService>(), TimeProvider.System, NullLogger<ItemUseTools>.Instance);
                IItemUseContext use = new ItemUseContext(c.Connection, c.Character, Substitute.For<IItemUseHost>(),
                    Item(29, used), used, tools);
                Assert.True(use.GiveItem(made, 1));
                Assert.True(use.GiveItem(potion, 1));
                copies = HeldAndWritten(c.Character);
                break;
            case "quest reward":
                w.Accept(c, QuestId);
                w.Quests.AddProgress(c.Character, QuestId, KillId, 1);
                Creature giver = w.Place(Giver);
                w.Talk(c, giver);
                Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, QuestId, giver.Guid.RawValue));
                copies = HeldAndWritten(c.Character);
                break;
            default:
                copies = await CreatedAsync(w.World);
                break;
        }

        // The base's copy carries its base stats and its links, the potion neither.
        Assert.Equal(new[] { made.Value, potion.Value }.Order(), copies.Select(copy => copy.Template.Value).Order());
        bool unique = how == "item script add of a unique";
        foreach (MadeCopy copy in copies)
        {
            // Only a quest's reward rolls its rarity, and so only it arrives unidentified (#1179); the write says the same.
            bool identified = how != "quest reward" || copy.Template != made;
            Assert.Equal((identified, identified), (copy.Identified, copy.WrittenIdentified));

            // A unique's base stats and first link are its base's.
            ItemTemplate template = w.Data.FindItemTemplate(copy.Template)!;
            ItemTemplate source = template.BaseTemplateId is { } named ? w.Data.FindItemTemplate(named)! : template;
            List<ItemTemplateBaseStat> ranges = source.BaseStats;
            Assert.Equal(copy.Template == made && !unique, ranges.Count > 0);
            Assert.Equal(copy.Template == made, copy.Marked);
            Assert.Equal(ranges.Select(range => range.Stat).Order(), Stats(copy.BaseStats));
            foreach ((ItemBaseStat stat, int value) in copy.BaseStats)
            {
                ItemTemplateBaseStat range = ranges.Single(r => r.Stat == stat);
                Assert.InRange(value, range.Min, range.Max);
            }

            Assert.Equal(copy.BaseStats, copy.WrittenBaseStats);

            // A base links its one implicit, a unique its base's and then its own line, the potion nothing.
            IReadOnlyList<ItemModifier> links = ReferenceEquals(source, template)
                ? w.Data.ItemModifiers.LinkedTo(copy.Template)
                : [.. w.Data.ItemModifiers.LinkedTo(source.Id), .. w.Data.ItemModifiers.LinkedTo(copy.Template)];
            Assert.Equal(copy.Template != made ? 0 : unique ? 2 : 1, links.Count);
            List<ItemInstanceModifier> linked = [.. copy.Held.Take(links.Count)];
            if (how != "quest reward" || copy.Template != made)
                Assert.Equal(links.Count, copy.Held.Count);
            Assert.Equal(links.Select(link => link.Id), linked.Select(modifier => modifier.ModifierId));
            Assert.Equal(Enumerable.Range(0, links.Count).Select(ordinal => (byte)ordinal), linked.Select(modifier => modifier.Ordinal));
            for (int i = 0; i < links.Count; i++)
            {
                Assert.Equal(links[i].Stats.Select(line => (line.Index, line.Stat)), linked[i].Values.Select(v => (v.Index, v.Stat)));
                Assert.All(links[i].Stats.Zip(linked[i].Values), pair =>
                {
                    Assert.InRange(pair.Second.Value, pair.First.Min, pair.First.Max);
                    Assert.Equal(pair.First.Min2 is not null, pair.Second.Value2 is not null);   // an "adds 1 to 2" line's high end
                    if (pair.Second.Value2 is { } high)
                        Assert.InRange(high, pair.First.Min2!.Value, pair.First.Max2!.Value);
                });
            }

            Assert.Equal(TestModifiers.Describe(copy.Held), TestModifiers.Describe(copy.Written));
        }
    }

    /// <summary>
    /// A copy as it was made: its template, its modifiers and base stats as held, and as the write that stores it carries
    /// them, whether that write rewrites its rolled rows at all (its save mark), and whether it is identified, as held and
    /// as written.
    /// </summary>
    private sealed record MadeCopy(
        ItemTemplateId Template, IReadOnlyList<ItemInstanceModifier> Held, IReadOnlyList<ItemInstanceModifier> Written,
        ItemBaseStats BaseStats, ItemBaseStats WrittenBaseStats, bool Marked, bool Identified, bool WrittenIdentified);

    private static List<ItemBaseStat> Stats(ItemBaseStats baseStats)
    {
        List<ItemBaseStat> stats = [];
        foreach ((ItemBaseStat stat, _) in baseStats)
            stats.Add(stat);

        return stats;
    }

    /// <summary>A warrior's create info, listing <paramref name="items" /> as its starting items.</summary>
    private static ICharacterCreateInfoRepository StartingWith(params ItemTemplateId[] items)
    {
        ICharacterCreateInfoRepository repository = Substitute.For<ICharacterCreateInfoRepository>();
        repository.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>(
            [new CharacterCreateInfo { Class = CharacterClass.Warrior, Map = 1, StartingItems = [.. items] }]));
        return repository;
    }

    /// <summary>
    /// Every Bag item's modifiers and base stats in the store, and what the next save writes of them: the write's rows for
    /// the item, its values put back on each, or none when the save does not rewrite the item's set.
    /// </summary>
    private static List<MadeCopy> HeldAndWritten(CharacterEntity character)
    {
        CharacterSaveBatch batch = CharacterSaveSnapshot.Take(character).Batch;
        ItemModifierWrite? write = batch.Modifiers;
        IReadOnlyList<ItemInstanceModifier> WrittenFor(ItemInstanceId item) =>
        [
            .. (write?.Modifiers ?? []).Where(m => m.ItemInstanceId == item).OrderBy(m => m.Ordinal).Select(m => new ItemInstanceModifier
            {
                Id = m.Id,
                ItemInstanceId = m.ItemInstanceId,
                ModifierId = m.ModifierId,
                Ordinal = m.Ordinal,
                Values = [.. write!.Values.Where(v => v.ItemInstanceModifierId == m.Id).OrderBy(v => v.Index)],
            }),
        ];

        return
        [
            .. character.Container(InventoryType.Bag).Items.Select(item => new MadeCopy(item.TemplateId,
                character.ItemModifiers.Of(item.InstanceId), WrittenFor(item.InstanceId),
                character.ItemModifiers.BaseStatsOf(item.InstanceId),
                ItemBaseStats.Of((write?.BaseStats ?? []).Where(s => s.ItemInstanceId == item.InstanceId)),
                write?.Items.Contains(item.InstanceId) == true, item.Identified,
                batch.UpsertItems.Single(row => row.Id == item.InstanceId).Identified)),
        ];
    }

    /// <summary>
    /// A warrior created through the handler: each starting item as its roll made it (held), and the rows the creation
    /// inserts with it (written). The insert is the write, so a copy counts as marked when it carries rows of either kind.
    /// </summary>
    private static async Task<List<MadeCopy>> CreatedAsync(IWorld world)
    {
        ICharacterRepository characters = Substitute.For<ICharacterRepository>();
        CharacterCreationBatch? created = null;
        characters.CreateForGameplayAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<CharacterCreationBatch>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>()).Returns(call =>
        {
            created = call.Arg<CharacterCreationBatch>();
            return Task.FromResult(new CharacterCreationReply(created.Row));
        });
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.AccountId.Returns(new AccountId(1));
        connection.GameplayAuthority.Returns(new GameplayWriteAuthority(new AccountId(1), Guid.NewGuid(), 1));
        connection.IsConnected.Returns(true);
        connection.Character.Returns((ICharacter?)null!);

        var roller = new RecordingRoller(TestEconomy.Roller(new ItemIdAllocator()));
        new CharacterCreateHandler(NullLogger<CharacterCreateHandler>.Instance, characters, roller, world)
            .Execute(connection, new CCharacterCreatePacket { Name = "Starter", Class = CharacterClass.Warrior });
        await (Task)connection.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IWorldConnection.EnqueueContinuation)).GetArguments()[0]!;

        Assert.NotNull(created);
        return
        [
            .. created.Items.Select(item =>
            {
                RolledItem rolled = roller.Made.Single(made => made.InstanceId == item.Id);
                return new MadeCopy(item.TemplateId, rolled.Modifiers, item.Modifiers, rolled.BaseStats,
                    ItemBaseStats.Of(item.BaseStats), item.Modifiers.Count > 0 || item.BaseStats.Count > 0, item.Identified,
                    item.Identified);
            }),
        ];
    }

    /// <summary>The roller the handler is given, keeping every plain copy it made, as the roll made it.</summary>
    private sealed class RecordingRoller(IItemRoller inner) : IItemRoller
    {
        public List<RolledItem> Made { get; } = [];

        public RolledItem RollPlain(ItemTemplate template, ushort itemLevel, ItemsPatch items)
        {
            RolledItem rolled = inner.RollPlain(template, itemLevel, items);
            Made.Add(rolled);
            return rolled;
        }

        public RolledItem? Roll(ItemTemplate template, in DropContext context, ItemsPatch items) =>
            inner.Roll(template, context, items);

        public void RollDrops(List<RolledDrop> drops, in DropContext context, ItemsPatch items) =>
            inner.RollDrops(drops, context, items);

        public AwakenedModifier Awaken(ItemInstanceModifier sealedRow, ushort itemLevel, ItemModifierCatalog catalog) =>
            inner.Awaken(sealedRow, itemLevel, catalog);
    }
}
