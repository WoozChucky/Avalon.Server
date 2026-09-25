using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.UnitTests;

/// <summary>
/// A character save is one Character-DB transaction: the row, then only the items and slots that
/// changed. Writes are idempotent so that two saves carrying the same entry, one of them already
/// committed, cannot fail each other on a duplicate key.
/// </summary>
public sealed class CharacterSaveRepositoryShould : IDisposable
{
    private readonly SqliteDatabase<CharacterDbContext> _database = SqliteDatabase.Characters();
    private readonly CharacterSaveRepository _saves;

    public CharacterSaveRepositoryShould()
    {
        using (CharacterDbContext context = _database.CreateDbContext())
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        _saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(_database));
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Write_the_row_and_insert_new_items_before_the_slots_that_hold_them()
    {
        CharacterRow row = await SeedCharacterAsync(1);
        row.Money = 500;
        ItemInstance sword = Item(row.Id, count: 1);

        await _saves.WriteAsync([Batch(row, upsertItems: [sword], upsertSlots: [Slot(row.Id, 0, sword.Id)])]);

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(500UL, (await read.Characters.AsNoTracking().SingleAsync()).Money);
        Assert.Equal(sword.Id, (await read.CharacterInventory.AsNoTracking().SingleAsync()).ItemId);
        Assert.Equal(1u, (await read.ItemInstances.AsNoTracking().SingleAsync()).Count);
    }

    [Fact]
    public async Task Update_an_item_or_slot_that_already_exists_instead_of_inserting_it_twice()
    {
        CharacterRow row = await SeedCharacterAsync(1);
        ItemInstance potion = Item(row.Id, count: 5);
        await _saves.WriteAsync([Batch(row, upsertItems: [potion], upsertSlots: [Slot(row.Id, 0, potion.Id)])]);

        potion.Count = 9;
        await _saves.WriteAsync([Batch(row, upsertItems: [potion], upsertSlots: [Slot(row.Id, 0, potion.Id)])]);

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(9u, (await read.ItemInstances.AsNoTracking().SingleAsync()).Count);
        Assert.Single(await read.CharacterInventory.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Delete_slots_before_the_items_they_hold_and_ignore_rows_already_gone()
    {
        CharacterRow row = await SeedCharacterAsync(1);
        ItemInstance potion = Item(row.Id, count: 5);
        await _saves.WriteAsync([Batch(row, upsertItems: [potion], upsertSlots: [Slot(row.Id, 0, potion.Id)])]);

        CharacterSaveBatch removal = Batch(row, deleteItems: [potion.Id], deleteSlots: [(InventoryType.Bag, (ushort)0)]);
        await _saves.WriteAsync([removal]);
        await _saves.WriteAsync([removal]);

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Empty(await read.ItemInstances.AsNoTracking().ToListAsync());
        Assert.Empty(await read.CharacterInventory.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Commit_every_batch_or_none()
    {
        CharacterRow saved = await SeedCharacterAsync(1);
        saved.Money = 100;
        var neverCreated = new CharacterRow { Id = new CharacterId(99), AccountId = new AccountId(1), Name = "Ghost" };
        ItemInstance sword = Item(saved.Id, count: 1);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _saves.WriteAsync(
        [
            Batch(saved, upsertItems: [sword], upsertSlots: [Slot(saved.Id, 0, sword.Id)]),
            Batch(neverCreated),
        ]));

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(0UL, (await read.Characters.AsNoTracking().SingleAsync()).Money);
        Assert.Empty(await read.ItemInstances.AsNoTracking().ToListAsync());
    }

    /// <summary>The shape a future trade takes: one side removes an item, the other adds it.</summary>
    [Fact]
    public async Task Keep_an_item_one_batch_removes_and_another_adds()
    {
        CharacterRow giver = await SeedCharacterAsync(1);
        CharacterRow taker = await SeedCharacterAsync(2);
        ItemInstance gem = Item(giver.Id, count: 1);
        await _saves.WriteAsync([Batch(giver, upsertItems: [gem], upsertSlots: [Slot(giver.Id, 0, gem.Id)])]);

        ItemInstance moved = Item(taker.Id, count: 1, id: gem.Id);
        await _saves.WriteAsync(
        [
            Batch(taker, upsertItems: [moved], upsertSlots: [Slot(taker.Id, 4, gem.Id)]),
            Batch(giver, deleteItems: [gem.Id], deleteSlots: [(InventoryType.Bag, (ushort)0)]),
        ]);

        await using CharacterDbContext read = _database.CreateDbContext();
        ItemInstance stored = await read.ItemInstances.AsNoTracking().SingleAsync();
        Assert.Equal(taker.Id, stored.CharacterId);
        CharacterInventory slot = await read.CharacterInventory.AsNoTracking().SingleAsync();
        Assert.Equal(taker.Id, slot.CharacterId);
        Assert.Equal((ushort)4, slot.Slot);
    }

    private async Task<CharacterRow> SeedCharacterAsync(uint id)
    {
        var row = new CharacterRow
        {
            Id = new CharacterId(id), AccountId = new AccountId(1), Name = $"Saver{id}", CreationDate = DateTime.UtcNow,
        };
        await using CharacterDbContext context = _database.CreateDbContext();
        context.Characters.Add(row);
        await context.SaveChangesAsync();
        context.Entry(row).State = EntityState.Detached;
        return row;
    }

    private static ItemInstance Item(CharacterId owner, uint count, ItemInstanceId? id = null) => new()
    {
        Id = id ?? new ItemInstanceId(Guid.CreateVersion7()),
        TemplateId = new ItemTemplateId(1),
        CharacterId = owner,
        Count = count,
        Flags = ItemInstanceFlags.None,
        UpdatedAt = DateTime.UtcNow,
    };

    private static CharacterInventory Slot(CharacterId owner, ushort slot, ItemInstanceId item) =>
        new() { CharacterId = owner, Container = InventoryType.Bag, Slot = slot, ItemId = item };

    private static CharacterSaveBatch Batch(
        CharacterRow row,
        IReadOnlyList<ItemInstance>? upsertItems = null,
        IReadOnlyList<ItemInstanceId>? deleteItems = null,
        IReadOnlyList<CharacterInventory>? upsertSlots = null,
        IReadOnlyList<(InventoryType Container, ushort Slot)>? deleteSlots = null) =>
        new(row, upsertItems ?? [], deleteItems ?? [], upsertSlots ?? [], deleteSlots ?? []);
}
