using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Persistence;

/// <summary>
/// Memory is authoritative (spec #459 D3). A save writes the row and only what is not Unchanged,
/// clears states only once the commit reaches the tick thread, keeps them when it fails, and never
/// lets two saves of one character trip over each other.
/// </summary>
public sealed class CharacterSaverShould : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

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
        new CharacterWallet(character, ulong.MaxValue).TryAddMoney(250);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));
        await PumpAsync();

        Assert.Equal(8u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.Equal(50u, (await StoredItemAsync(sword.InstanceId))!.Durability);   // Unchanged: not written
        Assert.Equal(250UL, (await StoredRowAsync(7)).Money);
    }

    [Fact]
    public async Task Insert_a_new_item_and_its_slot()
    {
        CharacterEntity character = await SeedAsync(7);

        InventoryFor(character).TryAdd(Sword.Id, 1);
        InventoryItem sword = At(character, InventoryType.Bag, 0);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));

        Assert.Equal(new CharacterId(7), (await StoredItemAsync(sword.InstanceId))!.CharacterId);
        Assert.Equal(sword.InstanceId, (await StoredSlotsAsync(7)).Single().ItemId);
    }

    [Fact]
    public async Task Delete_a_removed_item_and_its_slot()
    {
        InventoryItem potion = Item(0, Potion, count: 5);
        CharacterEntity character = await SeedAsync(7, stored: [potion]);

        InventoryFor(character).TryRemove(potion.InstanceId, 5);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));

        Assert.Null(await StoredItemAsync(potion.InstanceId));
        Assert.Empty(await StoredSlotsAsync(7));
    }

    [Fact]
    public async Task Clear_the_saved_states_only_once_the_commit_reaches_the_tick()
    {
        CharacterEntity character = await SeedAsync(7);
        InventoryFor(character).TryAdd(Potion.Id, 1);

        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));
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

        var failing = Substitute.For<ICharacterSaveRepository>();
        failing.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("database down")));

        Assert.False(await Saver(failing).Save(_connection, character).WaitAsync(Limit));
        await PumpAsync();
        Assert.Equal(SaveState.New, character.SaveState.ItemState(potion.InstanceId));

        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));
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
        wallet.TryAddMoney(10);                         // 10, in the snapshot
        Task<bool> first = saver.Save(_connection, character);
        InventoryFor(character).TryAdd(Potion.Id, 2);   // 10, after the snapshot
        wallet.TryAddMoney(5);                          // 15, after the snapshot
        gated.Open();
        Assert.True(await first.WaitAsync(Limit));
        await PumpAsync();

        Assert.Equal(8u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.Equal(10UL, (await StoredRowAsync(7)).Money);
        Assert.Equal(SaveState.Changed, character.SaveState.ItemState(potion.InstanceId));
        Assert.True(character.SaveState.MoneyDirty);

        Assert.True(await saver.Save(_connection, character).WaitAsync(Limit));
        await PumpAsync();
        Assert.Equal(10u, (await StoredItemAsync(potion.InstanceId))!.Count);
        Assert.Equal(15UL, (await StoredRowAsync(7)).Money);
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
        Assert.True(await first.WaitAsync(Limit));
        await PumpAsync();

        Assert.NotNull(await StoredItemAsync(sword.InstanceId));   // the insert landed
        Assert.Equal(SaveState.Removed, character.SaveState.ItemState(sword.InstanceId));

        Assert.True(await saver.Save(_connection, character).WaitAsync(Limit));
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

        Assert.True(await insert.WaitAsync(Limit));
        Assert.True(await delete.WaitAsync(Limit));
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

        Assert.True(await periodic.WaitAsync(Limit));
        Assert.True(await despawn.WaitAsync(Limit));
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

        await Task.Delay(50);
        Assert.Equal(1, recording.Started);   // the new session's save has not started yet

        recording.Release();
        Assert.True(await despawn.WaitAsync(Limit));
        Assert.True(await next.WaitAsync(Limit));
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
        await idle.WaitAsync(Limit);
        Assert.True(despawn.IsCompleted);
    }

    [Fact]
    public async Task Report_a_character_idle_after_a_failed_save()
    {
        CharacterEntity character = await SeedAsync(7);
        var failing = Substitute.For<ICharacterSaveRepository>();
        failing.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("database down")));
        CharacterSaver saver = Saver(failing);

        Assert.False(await saver.SaveOnDespawnAsync(character, prepareRow: null, CancellationToken.None).WaitAsync(Limit));

        await saver.WhenIdle(new CharacterId(7)).WaitAsync(Limit);
    }

    [Fact]
    public async Task Save_several_characters_in_one_transaction_or_none()
    {
        CharacterEntity saved = await SeedAsync(7);
        CharacterEntity neverStored = New(8);
        new CharacterWallet(saved, ulong.MaxValue).TryAddMoney(100);
        InventoryFor(saved).TryAdd(Potion.Id, 1);

        Assert.False(await Saver().Save([(_connection, saved), (_connection, neverStored)]).WaitAsync(Limit));
        await PumpAsync();

        Assert.Equal(0UL, (await StoredRowAsync(7)).Money);
        Assert.Empty(await StoredSlotsAsync(7));
        Assert.True(saved.SaveState.MoneyDirty);
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
        var repository = Substitute.For<ICharacterSaveRepository>();

        Assert.Throws<ArgumentException>(() => { _ = Saver(repository).Save([(_connection, first), (_connection, second)]); });

        await repository.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    [Fact]
    public async Task Keep_the_charges_an_item_was_loaded_with()
    {
        InventoryItem wand = Item(0, Potion, count: 1, charges: 3);
        CharacterEntity character = await SeedAsync(7, stored: [wand]);

        InventoryFor(character).TryAdd(Potion.Id, 1);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));

        Assert.Equal(3u, (await StoredItemAsync(wand.InstanceId))!.Charges);
    }

    [Fact]
    public async Task Survive_a_move_to_an_empty_slot_through_a_save()
    {
        InventoryItem potion = Item(0, Potion, count: 5);
        CharacterEntity character = await SeedAsync(7, stored: [potion]);

        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            EquipTemplates.InventoryFor(character).TryMove(EquipTemplates.Bag(0), EquipTemplates.Bag(6), null, false));
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));

        CharacterInventory slot = Assert.Single(await StoredSlotsAsync(7));
        Assert.Equal((ushort)6, slot.Slot);
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
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));

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
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));

        List<CharacterInventory> slots = await StoredSlotsAsync(7);
        Assert.Equal(2, slots.Count);
        Assert.Equal(sword.InstanceId, slots.Single(s => s.Slot == 0).ItemId);
        Assert.Equal(potion.InstanceId, slots.Single(s => s.Slot == 1).ItemId);
    }

    [Fact]
    public async Task Insert_the_stats_row_on_the_first_save_and_update_it_on_the_next()
    {
        CharacterEntity character = await SeedAsync(7);
        DerivedCharacterStats first = new(240, 100, 22, 23, 20, 20, 0, 5f, 3.664f, 5f, 46, 4);

        character.ApplyStats(first, CurrentValues.Refill);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));
        await PumpAsync();
        Assert.False(character.SaveState.StatsDirty);

        character.ApplyStats(first with { MaxHealth = 260, Armor = 8 }, CurrentValues.KeepShare);
        Assert.True(await Saver().Save(_connection, character).WaitAsync(Limit));

        await using CharacterDbContext read = _db.CreateDbContext();
        CharacterStats stored = await read.CharacterStats.AsNoTracking().SingleAsync();
        Assert.Equal((260u, 8u, 46u), (stored.MaxHealth, stored.Armor, stored.AttackDamage));
        Assert.Equal(260, (await StoredRowAsync(7)).Health);
    }

    private CharacterSaveRepository Repository() => new(new DbTransactionRunner<CharacterDbContext>(_db));

    private CharacterSaver Saver(ICharacterSaveRepository? repository = null) =>
        new(repository ?? Repository(), NullLogger<CharacterSaver>.Instance);

    /// <summary>
    /// The tick's continuation drain: WorldConnection runs a callback once its task has completed,
    /// with the result, whatever the result is.
    /// </summary>
    private async Task PumpAsync()
    {
        foreach ((Task<bool> task, Action<bool> callback) in _queued.ToList())
            callback(await task.WaitAsync(Limit));

        _queued.Clear();
    }

    /// <summary>Stores the character and <paramref name="stored" /> in Bag slots, and loads <paramref name="held" /> (default: the same) into memory.</summary>
    private async Task<CharacterEntity> SeedAsync(uint id, InventoryItem[]? stored = null, InventoryItem[]? held = null)
    {
        stored ??= [];
        CharacterEntity character = New(id);
        CharacterId owner = character.Data!.Id;

        await new CharacterRepository(_db).CreateAsync(character.Data!.Copy());
        if (stored.Length > 0)
        {
            await new ItemInstanceRepository(_db).CreateAsync(stored.Select(i => new ItemInstance
            {
                Id = i.InstanceId, TemplateId = i.TemplateId, CharacterId = owner, Count = i.Count,
                Durability = i.Durability, Charges = i.Charges, Flags = i.Flags, UpdatedAt = DateTime.UtcNow,
            }).ToList());
            await new CharacterInventoryRepository(_db).CreateAsync(stored.Select(i => new CharacterInventory
            {
                CharacterId = owner, Container = InventoryType.Bag, Slot = i.Slot, ItemId = i.InstanceId,
            }).ToList());
        }

        character.Container(InventoryType.Bag).Load(held ?? stored);
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
            await _gate.Task.WaitAsync(Limit, cancellationToken);
            await inner.WriteAsync(batches, cancellationToken);
        }
    }

    /// <summary>Counts the writes that have started, and holds the first one until <see cref="Release" />.</summary>
    private sealed class RecordingRepository : ICharacterSaveRepository
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public void Release() => _gate.TrySetResult();

        public async Task WriteAsync(IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _started) == 1)
                await _gate.Task.WaitAsync(Limit, cancellationToken);
        }
    }
}
