using System.Diagnostics.Metrics;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.Inventory.TestModifiers;

namespace Avalon.Server.World.UnitTests.Persistence;

/// <summary>
/// Memory is authoritative (spec #459 D3). A save writes the row and only what is not Unchanged,
/// clears states only once the commit reaches the tick thread, keeps them when it fails, and never
/// lets two saves of one character trip over each other.
/// </summary>
public sealed class CharacterSaverShould : IDisposable
{
    /// <summary>
    /// Only keeps a broken save from hanging the run: every wait here ends on a signal. Generous, because a save runs on
    /// the thread pool and SQLite, and a loaded runner can hold that work back for seconds.
    /// </summary>
    private static readonly TimeSpan s_limit = TimeSpan.FromSeconds(30);

    private readonly SqliteDatabase<CharacterDbContext> _db = SqliteDatabase.Characters();
    private readonly List<(Task<bool> Task, Action<bool> Callback)> _queued = [];
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();

    public CharacterSaverShould()
    {
        using (CharacterDbContext context = _db.CreateDbContext())
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        _connection.When(c => c.EnqueueContinuation(Arg.Any<Task<bool>>(), Arg.Any<Action<bool>>()))
            .Do(call => _queued.Add((call.Arg<Task<bool>>(), call.Arg<Action<bool>>())));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Write_the_row_and_only_what_changed()
    {
        InventoryItem potion = Item(0, Potion, count: 5), sword = Item(1, Sword, durability: 50);
        CharacterEntity character = await SeedAsync(7, stored: [potion, sword], held: [potion, sword with { Durability = 99 }]);

        InventoryFor(character).TryAdd(Potion.Id, 3);
        new CharacterWallet(character, ulong.MaxValue).TryAddGold(250);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();

        Assert.Equal(8u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.Equal(50u, (await StoredItemAsync(sword.InstanceId))!.Durability);   // Unchanged: not written
        Assert.Equal(250UL, (await StoredRowAsync(7)).Gold);
    }

    [Fact]
    public async Task Clear_the_saved_states_only_once_the_commit_reaches_the_tick()
    {
        CharacterEntity character = await SeedAsync(7);
        InventoryFor(character).TryAdd(Potion.Id, 1);

        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));
        Assert.True(character.SaveState.HasChanges);

        await PumpAsync();
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public async Task Keep_every_state_when_the_save_fails()
    {
        CharacterEntity character = await SeedAsync(7);
        InventoryFor(character).TryAdd(Potion.Id, 4);
        InventoryItem potion = At(character, InventoryType.Bag, 0);

        ICharacterSaveRepository failing = Substitute.For<ICharacterSaveRepository>();
        failing.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("database down")));

        Assert.False(await Saver(failing).Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();
        Assert.Equal(SaveState.New, character.SaveState.ItemState(potion.InstanceId));

        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();
        Assert.Equal(4u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public async Task Keep_a_change_made_while_the_save_was_in_flight()
    {
        InventoryItem potion = Item(0, Potion, count: 5);
        CharacterEntity character = await SeedAsync(7, stored: [potion]);
        var gated = new GatedRepository(Repository());
        CharacterSaver saver = Saver(gated);
        var wallet = new CharacterWallet(character, ulong.MaxValue);

        InventoryFor(character).TryAdd(Potion.Id, 3);   // 8, in the snapshot
        wallet.TryAddGold(10);                         // 10, in the snapshot
        Task<bool> first = saver.Save(_connection, character);
        InventoryFor(character).TryAdd(Potion.Id, 2);   // 10, after the snapshot
        wallet.TryAddGold(5);                          // 15, after the snapshot
        gated.Open();
        Assert.True(await first.WaitAsync(s_limit));
        await PumpAsync();

        Assert.Equal(8u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.Equal(10UL, (await StoredRowAsync(7)).Gold);
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(potion.InstanceId));
        Assert.True(character.SaveState.GoldDirty);

        Assert.True(await saver.Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();
        Assert.Equal(10u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.Equal(15UL, (await StoredRowAsync(7)).Gold);
        Assert.False(character.SaveState.HasChanges);
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public async Task Delete_an_item_removed_while_its_insert_was_in_flight()
    {
        CharacterEntity character = await SeedAsync(7);
        var gated = new GatedRepository(Repository());
        CharacterSaver saver = Saver(gated);

        InventoryFor(character).TryAdd(Sword.Id, 1);
        InventoryItem sword = At(character, InventoryType.Bag, 0);
        Task<bool> first = saver.Save(_connection, character);
        InventoryFor(character).TryRemove(sword.InstanceId, 1);
        gated.Open();
        Assert.True(await first.WaitAsync(s_limit));
        await PumpAsync();

        Assert.NotNull(await StoredItemAsync(sword.InstanceId));   // the insert landed
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(sword.InstanceId));

        Assert.True(await saver.Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();
        Assert.Null(await StoredItemAsync(sword.InstanceId));
        Assert.Empty(await StoredSlotsAsync(7));
        Assert.False(character.SaveState.HasChanges);
    }

    /// <summary>
    /// Review Focus 2, with the removal saved while the insert is still in flight: the delete must
    /// queue behind the insert, or the insert lands last and leaves an orphan.
    /// </summary>
    [Fact]
    public async Task Queue_a_delete_behind_the_insert_it_undoes()
    {
        CharacterEntity character = await SeedAsync(7);
        var gated = new GatedRepository(Repository());
        CharacterSaver saver = Saver(gated);

        InventoryFor(character).TryAdd(Sword.Id, 1);
        InventoryItem sword = At(character, InventoryType.Bag, 0);
        Task<bool> insert = saver.Save(_connection, character);
        InventoryFor(character).TryRemove(sword.InstanceId, 1);
        Task<bool> delete = saver.Save(_connection, character);
        gated.Open();

        Assert.True(await insert.WaitAsync(s_limit));
        Assert.True(await delete.WaitAsync(s_limit));
        await PumpAsync();

        Assert.Null(await StoredItemAsync(sword.InstanceId));
        Assert.Empty(await StoredSlotsAsync(7));
        Assert.False(character.SaveState.HasChanges);
    }

    /// <summary>Review Focus 1: the logout lands while the periodic save is still writing.</summary>
    [Fact]
    public async Task Queue_a_despawn_save_behind_one_still_in_flight()
    {
        CharacterEntity character = await SeedAsync(7);
        var gated = new GatedRepository(Repository());
        CharacterSaver saver = Saver(gated);

        character.Data!.Online = true;
        InventoryFor(character).TryAdd(Sword.Id, 1);
        Task<bool> periodic = saver.Save(_connection, character);
        character.Data!.Online = false;
        Task<bool> despawn = saver.SaveOnDespawnAsync(character, prepareRow: null, CancellationToken.None);
        gated.Open();

        Assert.True(await periodic.WaitAsync(s_limit));
        Assert.True(await despawn.WaitAsync(s_limit));
        await using CharacterDbContext read = _db.CreateDbContext();
        Assert.Single(await read.ItemInstances.AsNoTracking().Where(i => i.CharacterId == new CharacterId(7)).ToListAsync());
        Assert.Single(await StoredSlotsAsync(7));
        Assert.False((await StoredRowAsync(7)).Online);   // the despawn landed last
    }

    /// <summary>
    /// A relog builds a new entity for the same character. Its first save must still wait for the
    /// old entity's despawn save, because the chain belongs to the character, not to the entity.
    /// </summary>
    [Fact]
    public async Task Queue_a_new_entity_save_behind_the_old_entity_despawn_save()
    {
        CharacterEntity oldSession = await SeedAsync(7);
        var recording = new RecordingRepository();
        CharacterSaver saver = Saver(recording);

        Task<bool> despawn = saver.SaveOnDespawnAsync(oldSession, prepareRow: null, CancellationToken.None);
        CharacterEntity newSession = New(7);
        Task<bool> next = saver.Save(_connection, newSession);

        await recording.FirstStarted.WaitAsync(s_limit);
        await Task.Delay(50);   // gives an unchained second save time to start; a correct chain keeps the count at 1 however long this waits (#599)
        Assert.Equal(1, recording.Started);

        recording.Release();
        Assert.True(await despawn.WaitAsync(s_limit));
        Assert.True(await next.WaitAsync(s_limit));
        Assert.Equal(2, recording.Started);
    }

    [Fact]
    public async Task Report_a_character_idle_only_once_its_saves_have_committed()
    {
        CharacterEntity character = await SeedAsync(7);
        var gated = new GatedRepository(Repository());
        CharacterSaver saver = Saver(gated);

        Task<bool> despawn = saver.SaveOnDespawnAsync(character, prepareRow: null, CancellationToken.None);
        Task idle = saver.WhenIdle(new CharacterId(7));

        Assert.False(idle.IsCompleted);
        Assert.True(saver.WhenIdle(new CharacterId(8)).IsCompleted);   // another character is not held up

        gated.Open();
        await idle.WaitAsync(s_limit);
        Assert.True(despawn.IsCompleted);
    }

    [Fact]
    public async Task Report_a_character_idle_after_a_failed_save()
    {
        CharacterEntity character = await SeedAsync(7);
        ICharacterSaveRepository failing = Substitute.For<ICharacterSaveRepository>();
        failing.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("database down")));
        CharacterSaver saver = Saver(failing);

        Assert.False(await saver.SaveOnDespawnAsync(character, prepareRow: null, CancellationToken.None).WaitAsync(s_limit));

        await saver.WhenIdle(new CharacterId(7)).WaitAsync(s_limit);
    }

    [Fact]
    public async Task Save_several_characters_in_one_transaction_or_none()
    {
        CharacterEntity saved = await SeedAsync(7);
        CharacterEntity neverStored = New(8);
        new CharacterWallet(saved, ulong.MaxValue).TryAddGold(100);
        InventoryFor(saved).TryAdd(Potion.Id, 1);

        Assert.False(await Saver().Save([(_connection, saved), (_connection, neverStored)]).WaitAsync(s_limit));
        await PumpAsync();

        Assert.Equal(0UL, (await StoredRowAsync(7)).Gold);
        Assert.Empty(await StoredSlotsAsync(7));
        Assert.True(saved.SaveState.GoldDirty);
    }

    /// <summary>
    /// The repository takes at most one batch per character in a call. Two entities for one
    /// character in a single save is a caller bug, refused before anything is written.
    /// </summary>
    [Fact]
    public async Task Refuse_two_batches_for_one_character_in_one_save()
    {
        CharacterEntity first = await SeedAsync(7);
        CharacterEntity second = New(7);
        ICharacterSaveRepository repository = Substitute.For<ICharacterSaveRepository>();

        Assert.Throws<ArgumentException>(() => { _ = Saver(repository).Save([(_connection, first), (_connection, second)]); });

        await repository.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    [Fact]
    public async Task Keep_the_charges_an_item_was_loaded_with()
    {
        InventoryItem wand = Item(0, Potion, count: 1, charges: 3);
        CharacterEntity character = await SeedAsync(7, stored: [wand]);

        InventoryFor(character).TryAdd(Potion.Id, 1);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));

        Assert.Equal(3u, (await StoredItemAsync(wand.InstanceId))!.Charges);
    }

    /// <summary>
    /// #1179: rarity, item level and modifiers are facts of the instance, so they outlive a sale saved while the item sits
    /// in the buyback list (its rows, modifiers included, are deleted then), the buyback, its save and a reload: the
    /// bought-back item carries its modifiers back to the database under their own ids.
    /// </summary>
    [Fact]
    public async Task Keep_an_items_rarity_level_and_modifiers_through_a_sale_buyback_save_and_reload()
    {
        InventoryItem x = Item(4, Vendors.VendorTestData.Blade, durability: 42) with { Rarity = ItemRarity.Magic, ItemLevel = 7 };
        ItemInstanceModifier a = Modifier(x.InstanceId, 202, 0, (0, StatType.AddedFireDamage, 2, 5));
        ItemInstanceModifier b = Modifier(x.InstanceId, 10103, 1, (0, StatType.Strength, 8, null), (1, StatType.Armor, 3, null));
        await SeedAsync(7, stored: [x], modifiers: [b, a]);
        CharacterEntity character = await LoadAsync(7);
        Assert.Equal(Describe(a, b), Describe(character.ItemModifiers.Of(x.InstanceId)));
        var vendor = new VendorTrade(character, InventoryFor(character, Vendors.VendorTestData.Find),
            new CharacterWallet(character, ulong.MaxValue), Vendors.VendorTestData.Find, ulong.MaxValue, new SellFactors());
        CharacterSaver saver = Saver();

        Assert.Equal(Avalon.Network.Packets.Vendor.VendorResult.Ok, vendor.TrySell(shopOpen: true, 4, null));
        Assert.Empty(character.ItemModifiers.Of(x.InstanceId));
        Assert.True(await saver.Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();
        Assert.Null(await StoredItemAsync(x.InstanceId));

        Assert.Equal(Avalon.Network.Packets.Vendor.VendorResult.Ok, vendor.TryBuyback(shopOpen: true, 0));
        Assert.True(await saver.Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();

        CharacterEntity reloaded = await LoadAsync(7);
        InventoryItem back = Assert.Single(reloaded.Container(InventoryType.Bag).Items);
        Assert.Equal((x.InstanceId, ItemRarity.Magic, (ushort)7), (back.InstanceId, back.Rarity, back.ItemLevel));
        Assert.Equal(Describe(a, b), Describe(reloaded.ItemModifiers.Of(x.InstanceId)));
    }

    [Fact]
    public async Task Survive_a_move_to_an_empty_slot_through_a_save()
    {
        InventoryItem potion = Item(0, Potion, count: 5);
        CharacterEntity character = await SeedAsync(7, stored: [potion]);

        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            EquipTemplates.InventoryFor(character).TryMove(EquipTemplates.Bag(0), EquipTemplates.Bag(6), null, false));
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));

        CharacterInventory slot = Assert.Single(await StoredSlotsAsync(7));
        Assert.Equal((byte)6, slot.Slot);
        Assert.Equal(potion.InstanceId, slot.ItemId);
        Assert.Equal(5u, (await StoredItemAsync(potion.InstanceId))!.Count);
    }

    [Fact]
    public async Task Survive_a_split_through_a_save()
    {
        InventoryItem potion = Item(0, Potion, count: 10);
        CharacterEntity character = await SeedAsync(7, stored: [potion]);

        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            EquipTemplates.InventoryFor(character).TryMove(EquipTemplates.Bag(0), EquipTemplates.Bag(4), 3, false));
        InventoryItem split = At(character, InventoryType.Bag, 4);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));

        Assert.Equal(7u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.Equal(3u, (await StoredItemAsync(split.InstanceId))!.Count);
        List<CharacterInventory> slots = await StoredSlotsAsync(7);
        Assert.Equal(potion.InstanceId, slots.Single(s => s.Slot == 0).ItemId);
        Assert.Equal(split.InstanceId, slots.Single(s => s.Slot == 4).ItemId);
    }

    [Fact]
    public async Task Survive_a_swap_through_a_save()
    {
        InventoryItem potion = Item(0, Potion, count: 5), sword = Item(1, Sword);
        CharacterEntity character = await SeedAsync(7, stored: [potion, sword]);

        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            EquipTemplates.InventoryFor(character).TryMove(EquipTemplates.Bag(0), EquipTemplates.Bag(1), null, false));
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));

        List<CharacterInventory> slots = await StoredSlotsAsync(7);
        Assert.Equal(2, slots.Count);
        Assert.Equal(sword.InstanceId, slots.Single(s => s.Slot == 0).ItemId);
        Assert.Equal(potion.InstanceId, slots.Single(s => s.Slot == 1).ItemId);
    }

    /// <summary>
    /// One save window holding a destroy of X in slot 0 and then a move of Y into that same slot.
    /// X's delete cascades to whatever slot row still points at X, so the write must leave slot 0
    /// holding Y: deleting X after the slot row had become Y's, or re-inserting the slot after X's
    /// cascade took it, both do; losing slot 0 or keeping X would not.
    /// Regression guard: it passes against the repository as it is, because the slot-existence query
    /// runs after every delete. Taking that query before the deletes makes it fail (EF updates a
    /// slot row the cascade already removed), which was checked by hand.
    /// </summary>
    [Fact]
    public async Task Survive_a_destroy_then_a_move_into_the_freed_slot_through_a_save()
    {
        InventoryItem potion = Item(0, Potion, count: 5), sword = Item(1, Sword);
        CharacterEntity character = await SeedAsync(7, stored: [potion, sword]);
        CharacterInventoryService inventory = EquipTemplates.InventoryFor(character);

        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            inventory.TryDestroy(EquipTemplates.Bag(0), null, false));
        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            inventory.TryMove(EquipTemplates.Bag(1), EquipTemplates.Bag(0), null, false));
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));

        CharacterInventory slot = Assert.Single(await StoredSlotsAsync(7));
        Assert.Equal((InventoryType.Bag, (byte)0, sword.InstanceId), (slot.Container, slot.Slot, slot.ItemId));
        Assert.Null(await StoredItemAsync(potion.InstanceId));
        Assert.NotNull(await StoredItemAsync(sword.InstanceId));
    }

    [Fact]
    public async Task Insert_the_stats_row_on_the_first_save_and_update_it_on_the_next()
    {
        CharacterEntity character = await SeedAsync(7);
        DerivedCharacterStats first = new(240, 100, 22, 23, 20, 20, 0, 5f, 4, 5f, 46, 4);

        character.ApplyStats(first, CurrentValues.Refill, TestCombat.Formula);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));
        await PumpAsync();
        Assert.False(character.SaveState.StatsDirty);

        character.ApplyStats(first with { MaxHealth = 260, Armor = 8 }, CurrentValues.KeepShare, TestCombat.Formula);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(s_limit));

        await using CharacterDbContext read = _db.CreateDbContext();
        CharacterStats stored = await read.CharacterStats.AsNoTracking().SingleAsync();
        Assert.Equal((260u, 8u), (stored.MaxHealth, stored.Armor));
        Assert.Equal(260, (await StoredRowAsync(7)).Health);
    }

    /// <summary>
    /// One duration per save batch, tagged with whether it committed and which kind of save it was.
    /// A despawn save whose row preparation throws is a failed despawn save.
    /// </summary>
    [Theory]
    [InlineData(false, false, "committed", "periodic")]
    [InlineData(true, false, "failed", "periodic")]
    [InlineData(false, true, "failed", "despawn")]
    public async Task Record_one_save_duration_per_batch_with_its_outcome(bool fail, bool despawnThrows, string outcome, string kind)
    {
        using var meter = new Meter($"test-{Guid.NewGuid()}");
        List<Dictionary<string, object?>> measurements = [];
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) =>
            {
                if (i.Meter == meter && i.Name == "world.character.save.duration")
                    l.EnableMeasurementEvents(i);
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            Assert.True(value >= 0);
            Dictionary<string, object?> copy = [];
            foreach (KeyValuePair<string, object?> tag in tags)
                copy[tag.Key] = tag.Value;
            lock (measurements)
                measurements.Add(copy);
        });
        listener.Start();

        CharacterEntity character = await SeedAsync(7);
        ICharacterSaveRepository repository = Repository();
        if (fail)
        {
            repository = Substitute.For<ICharacterSaveRepository>();
            repository.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("boom")));
        }

        CharacterSaver saver = Saver(repository, meter);

        bool saved = despawnThrows
            ? await saver.SaveOnDespawnAsync(character, (_, _) => throw new InvalidOperationException("prepare"), CancellationToken.None).WaitAsync(s_limit)
            : await saver.Save(_connection, character).WaitAsync(s_limit);
        Assert.Equal(outcome == "committed", saved);

        Dictionary<string, object?> tags = Assert.Single(measurements);
        Assert.Equal(outcome, tags["outcome"]);
        Assert.Equal(kind, tags["kind"]);
    }

    /// <summary>
    /// Save first, then swap (#1175): the paired save writes both characters as they will be after the trade, with its
    /// record, in one transaction, while memory stays as it is. A refused write leaves both rows and the record out. The
    /// sword's modifier was never saved (#1179): the receiver's half writes it, with the copy's rarity and item level.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_both_sides_of_a_trade_and_its_record_in_one_transaction_or_neither(bool refused)
    {
        InventoryItem sword = Item(0, Sword) with { Rarity = ItemRarity.Magic, ItemLevel = 7 }, potion = Item(0, Potion, count: 5);
        CharacterEntity a = await SeedAsync(7, stored: [sword], gold: 100);
        ItemInstanceModifier fire = Modifier(sword.InstanceId, 202, 0, (0, StatType.AddedFireDamage, 2, 5));
        a.ItemModifiers.Load([new ItemInstance { Id = sword.InstanceId, TemplateId = sword.TemplateId, Modifiers = [fire] }]);
        CharacterEntity b;
        if (refused)
        {
            b = New(8);   // never stored, as Save_several_characters_in_one_transaction_or_none does
            b.Container(InventoryType.Bag).Load([potion]);
        }
        else
        {
            b = await SeedAsync(8, stored: [potion]);
        }

        (TradePlan plan, TradeRecord record) = PlanTrade(a, sword, 40, b, potion);
        CharacterSaver saver = Saver();

        Assert.Equal(refused ? SaveOutcome.Refused : SaveOutcome.Committed,
            await saver.SaveTrade([(_connection, a, plan.First), (_connection, b, plan.Second)], record).WaitAsync(s_limit));
        Assert.Equal(!refused, await saver.TradeCommittedAsync(record.Id, a.Data!.AccountId, b.Data!.AccountId).WaitAsync(s_limit));

        if (refused)
        {
            Assert.Equal(new CharacterId(7), (await StoredItemAsync(sword.InstanceId))!.CharacterId);
            Assert.Equal(sword.InstanceId, Assert.Single(await StoredSlotsAsync(7)).ItemId);
            Assert.Equal(100UL, (await StoredRowAsync(7)).Gold);
        }
        else
        {
            Assert.Equal(new CharacterId(8), (await StoredItemAsync(sword.InstanceId))!.CharacterId);
            Assert.Equal(new CharacterId(7), (await StoredItemAsync(potion.InstanceId))!.CharacterId);
            Assert.Equal((60UL, 40UL), ((await StoredRowAsync(7)).Gold, (await StoredRowAsync(8)).Gold));
            CharacterInventory slotA = Assert.Single(await StoredSlotsAsync(7));
            Assert.Equal((plan.First.Incoming[0].Item.Slot, potion.InstanceId), (slotA.Slot, slotA.ItemId));
            CharacterInventory slotB = Assert.Single(await StoredSlotsAsync(8));
            Assert.Equal((plan.Second.Incoming[0].Item.Slot, sword.InstanceId), (slotB.Slot, slotB.ItemId));
            await using CharacterDbContext read = _db.CreateDbContext();
            ItemInstance given = await read.ItemInstances.AsNoTracking().Include(i => i.Modifiers).ThenInclude(m => m.Values)
                .SingleAsync(i => i.Id == sword.InstanceId);
            Assert.Equal((ItemRarity.Magic, (ushort)7), (given.Rarity, given.ItemLevel));
            Assert.Equal(Describe(fire), Describe([.. given.Modifiers]));
        }

        // Memory never changes either way: the trade applies the swap itself, once it knows the outcome.
        Assert.Equal(sword.InstanceId, At(a, InventoryType.Bag, 0).InstanceId);
        Assert.Equal(100UL, a.Data!.Gold);
    }

    /// <summary>
    /// A save of a trader asked for while the paired save is in flight is held, and snapshotted only once the swap is
    /// applied: a snapshot taken before it would write the pre-trade row back whole, gold included.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hold_a_trader_s_other_saves_until_the_swap_is_applied(bool despawn)
    {
        InventoryItem sword = Item(0, Sword), potion = Item(0, Potion, count: 5);
        CharacterEntity a = await SeedAsync(7, stored: [sword], gold: 100);
        CharacterEntity b = await SeedAsync(8, stored: [potion]);
        (TradePlan plan, TradeRecord record) = PlanTrade(a, sword, 40, b, potion);
        var gated = new GatedRepository(Repository());
        CharacterSaver saver = Saver(gated);
        var walletA = new CharacterWallet(a, ulong.MaxValue);
        var walletB = new CharacterWallet(b, ulong.MaxValue);

        Task<SaveOutcome> pairedSave = saver.SaveTrade([(_connection, a, plan.First), (_connection, b, plan.Second)], record);
        Assert.Equal(WalletResult.Ok, walletA.TryAddGold(5));   // loot, after the paired snapshot
        Task<bool> held = despawn
            ? saver.SaveOnDespawnAsync(a, prepareRow: null, CancellationToken.None)
            : saver.Save(_connection, a);
        Assert.False(held.IsCompleted);
        Assert.False(saver.WhenIdle(a.Data!.Id).IsCompleted);

        gated.Open();
        Assert.Equal(SaveOutcome.Committed, await pairedSave.WaitAsync(s_limit));
        await PumpAsync();

        // The swap, as the trade applies it.
        a.Locks.Clear();
        b.Locks.Clear();
        CarriedItem givenSword = InventoryFor(a).TakeOut(new SlotRef(InventoryType.Bag, 0), 1);
        a.SaveState.ItemHandedOver(givenSword.Item.InstanceId);
        CarriedItem givenPotion = InventoryFor(b).TakeOut(new SlotRef(InventoryType.Bag, 0), 5);
        b.SaveState.ItemHandedOver(givenPotion.Item.InstanceId);
        Assert.Equal(InventoryAddResult.Ok, InventoryFor(b).TryAddInstanceAt(givenSword, plan.Second.Incoming[0].Item.Slot));
        Assert.Equal(InventoryAddResult.Ok, InventoryFor(a).TryAddInstanceAt(givenPotion, plan.First.Incoming[0].Item.Slot));
        Assert.Equal(WalletResult.Ok, walletA.TrySpend(40));
        Assert.Equal(WalletResult.Ok, walletB.TryAddGold(40));

        saver.ReleaseHold(a.Data.Id);
        saver.ReleaseHold(b.Data!.Id);

        Assert.True(await held.WaitAsync(s_limit));
        Assert.Equal(65UL, (await StoredRowAsync(7)).Gold);   // 100 - 40 + 5, never 105
        Assert.Equal(new CharacterId(8), (await StoredItemAsync(sword.InstanceId))!.CharacterId);
        CharacterInventory slotA = Assert.Single(await StoredSlotsAsync(7));   // the potion, never the sword back
        Assert.Equal((plan.First.Incoming[0].Item.Slot, potion.InstanceId), (slotA.Slot, slotA.ItemId));
        if (!despawn)
            Assert.True(await saver.Save(_connection, a).WaitAsync(s_limit));   // the giver's next save is not fenced out
        Assert.True(await saver.Save(_connection, b).WaitAsync(s_limit));
        await saver.WhenIdle(a.Data.Id).WaitAsync(s_limit);
    }

    /// <summary>A gives <paramref name="aItem" /> and <paramref name="aGold" />, B gives <paramref name="bItem" />.</summary>
    private static (TradePlan Plan, TradeRecord Record) PlanTrade(
        CharacterEntity a, InventoryItem aItem, ulong aGold, CharacterEntity b, InventoryItem bItem)
    {
        TradeDecision decision = TradeRules.Plan(new TradeOffer(a, [aItem], aGold), new TradeOffer(b, [bItem], 0), Find, ulong.MaxValue);
        Assert.Equal(Avalon.Network.Packets.Trade.TradeResult.Ok, decision.Result);
        var record = new TradeRecord
        {
            Id = Guid.CreateVersion7(),
            FirstCharacterId = a.Data!.Id,
            SecondCharacterId = b.Data!.Id,
            FirstGold = aGold,
            CompletedAt = DateTime.UtcNow,
            Items =
            [
                new TradeRecordItem { ItemInstanceId = aItem.InstanceId, GiverCharacterId = a.Data.Id, TemplateId = aItem.TemplateId, Count = aItem.Count },
                new TradeRecordItem { ItemInstanceId = bItem.InstanceId, GiverCharacterId = b.Data.Id, TemplateId = bItem.TemplateId, Count = bItem.Count },
            ],
        };
        return (decision.Plan!, record);
    }

    private CharacterSaveRepository Repository() => new(new DbTransactionRunner<CharacterDbContext>(_db));

    private CharacterSaver Saver(ICharacterSaveRepository? repository = null, Meter? meter = null) =>
        new(repository ?? Repository(), NullLogger<CharacterSaver>.Instance, meter);

    /// <summary>
    /// The tick's continuation drain: WorldConnection runs a callback once its task has completed,
    /// with the result, whatever the result is.
    /// </summary>
    private async Task PumpAsync()
    {
        foreach ((Task<bool> task, Action<bool> callback) in _queued.ToList())
            callback(await task.WaitAsync(s_limit));

        _queued.Clear();
    }

    /// <summary>
    /// Stores the character and <paramref name="stored" /> in Bag slots, with <paramref name="modifiers" /> on their items,
    /// and loads <paramref name="held" /> (default: the same) into memory.
    /// </summary>
    private async Task<CharacterEntity> SeedAsync(uint id, InventoryItem[]? stored = null, InventoryItem[]? held = null, ulong gold = 0,
        ItemInstanceModifier[]? modifiers = null)
    {
        stored ??= [];
        modifiers ??= [];
        CharacterEntity character = New(id, gold);
        CharacterId owner = character.Data!.Id;

        await new CharacterRepository(_db).CreateAsync(character.Data!.Copy());
        if (stored.Length > 0)
        {
            await new ItemInstanceRepository(_db).CreateAsync(stored.Select(i => new ItemInstance
            {
                Id = i.InstanceId,
                TemplateId = i.TemplateId,
                CharacterId = owner,
                Count = i.Count,
                Durability = i.Durability,
                Charges = i.Charges,
                Flags = i.Flags,
                Rarity = i.Rarity,
                ItemLevel = i.ItemLevel,
                UpdatedAt = DateTime.UtcNow,
            }).ToList());
            await new CharacterInventoryRepository(_db).CreateAsync(stored.Select(i => new CharacterInventory
            {
                CharacterId = owner,
                Container = InventoryType.Bag,
                Slot = i.Slot,
                ItemId = i.InstanceId,
            }).ToList());
        }

        if (modifiers.Length > 0)
        {
            // Rows of their own, not the caller's objects: the context's fix-up would otherwise fill them in.
            await using CharacterDbContext context = _db.CreateDbContext();
            context.AddRange(modifiers.Select(m =>
            {
                ItemInstanceModifier row = m.CopyRow();
                row.Values.AddRange(m.Values.Select(v => v.Copy()));
                return row;
            }));
            await context.SaveChangesAsync();
        }

        character.Container(InventoryType.Bag).Load(held ?? stored);
        await AdmittedCharacter.BindAsync(_db, character);
        return character;
    }

    /// <summary>A new entity for the stored character, its containers and modifiers loaded from the database as the select loads them.</summary>
    private async Task<CharacterEntity> LoadAsync(uint id)
    {
        CharacterId owner = new(id);
        CharacterEntity character = New(id);
        character.Data!.Gold = (await StoredRowAsync(id)).Gold;
        IReadOnlyList<ItemInstance> instances = await new ItemInstanceRepository(_db).GetByCharacterIdAsync(owner);
        IReadOnlyDictionary<InventoryType, List<InventoryItem>> assembled = InventoryAssembler.Assemble(
            await new CharacterInventoryRepository(_db).GetByCharacterIdAsync(owner), [.. instances], NullLogger.Instance);
        foreach (InventoryType container in new[] { InventoryType.Equipment, InventoryType.Bag, InventoryType.Bank })
            character.Container(container).Load(assembled[container]);
        character.ItemModifiers.Load([.. instances]);
        await AdmittedCharacter.BindAsync(_db, character);
        return character;
    }

    private async Task<ItemInstance?> StoredItemAsync(ItemInstanceId id)
    {
        await using CharacterDbContext read = _db.CreateDbContext();
        return await read.ItemInstances.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
    }

    private async Task<List<CharacterInventory>> StoredSlotsAsync(uint id)
    {
        await using CharacterDbContext read = _db.CreateDbContext();
        return await read.CharacterInventory.AsNoTracking().Where(s => s.CharacterId == new CharacterId(id)).ToListAsync();
    }

    private async Task<Character> StoredRowAsync(uint id)
    {
        await using CharacterDbContext read = _db.CreateDbContext();
        return await read.Characters.AsNoTracking().SingleAsync(c => c.Id == new CharacterId(id));
    }

    /// <summary>Holds every write until <see cref="Open" />, so a test can act while a save is in flight.</summary>
    private sealed class GatedRepository(ICharacterSaveRepository inner) : ICharacterSaveRepository
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _gate.TrySetResult();

        public async Task WriteAsync(IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken = default)
        {
            await _gate.Task.WaitAsync(s_limit, cancellationToken);
            await inner.WriteAsync(batches, cancellationToken);
        }

        public async Task WriteTradeAsync(IReadOnlyList<CharacterSaveBatch> batches, TradeRecord record, CancellationToken cancellationToken = default)
        {
            await _gate.Task.WaitAsync(s_limit, cancellationToken);
            await inner.WriteTradeAsync(batches, record, cancellationToken);
        }

        public Task<bool> TradeRecordExistsAsync(Guid tradeId, AccountId first, AccountId second, CancellationToken cancellationToken = default) =>
            inner.TradeRecordExistsAsync(tradeId, first, second, cancellationToken);
    }

    /// <summary>
    /// Counts the writes that have started, signals <see cref="FirstStarted" /> when the first one
    /// does, and holds it until <see cref="Release" />.
    /// </summary>
    private sealed class RecordingRepository : ICharacterSaveRepository
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public int Started => Volatile.Read(ref _started);

        /// <summary>Completes once the first write has started, so a test waits for it instead of sleeping (#599).</summary>
        public Task FirstStarted => _firstStarted.Task;

        public void Release() => _gate.TrySetResult();

        public async Task WriteAsync(IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _started) == 1)
            {
                _firstStarted.TrySetResult();
                await _gate.Task.WaitAsync(s_limit, cancellationToken);
            }
        }

        public Task WriteTradeAsync(IReadOnlyList<CharacterSaveBatch> batches, TradeRecord record, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TradeRecordExistsAsync(Guid tradeId, AccountId first, AccountId second, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
