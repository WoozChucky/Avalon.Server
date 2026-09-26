using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Persistence;

/// <summary>
/// Sell, then buy back, through the real save path on SQLite (spec #432). A whole sale is a
/// Removed item; the buyback re-adds the same id as New, and the save upserts it, so the row comes
/// back whether or not the delete had already committed.
/// </summary>
public sealed class VendorSaveRoundTripShould : IDisposable
{
    private readonly SqliteDatabase<CharacterDbContext> _database = SqliteDatabase.Characters();
    private readonly CharacterSaveRepository _saves;

    public VendorSaveRoundTripShould()
    {
        using (CharacterDbContext context = _database.CreateDbContext())
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        _saves = new CharacterSaveRepository(new DbTransactionRunner<CharacterDbContext>(_database));
    }

    public void Dispose() => _database.Dispose();

    private static VendorTrade Trade(CharacterEntity character) =>
        new(character, TestCharacters.InventoryFor(character, Find), new CharacterWallet(character, 1_000_000), Find, 1_000_000);

    /// <summary>A character whose row and items are already in the database, loaded as select would.</summary>
    private async Task<CharacterEntity> LoadedWithAsync(params InventoryItem[] items)
    {
        CharacterEntity character = TestCharacters.New(id: 1, money: 100);
        await using (CharacterDbContext context = _database.CreateDbContext())
        {
            context.Characters.Add(character.Data!);
            foreach (InventoryItem item in items)
            {
                context.ItemInstances.Add(new ItemInstance
                {
                    Id = item.InstanceId, TemplateId = item.TemplateId, CharacterId = character.Data!.Id,
                    Count = item.Count, Durability = item.Durability, Flags = item.Flags, UpdatedAt = DateTime.UtcNow,
                });
                context.CharacterInventory.Add(new CharacterInventory
                {
                    CharacterId = character.Data!.Id, Container = InventoryType.Bag, Slot = item.Slot, ItemId = item.InstanceId,
                });
            }

            await context.SaveChangesAsync();
        }

        character.Container(InventoryType.Bag).Load(items);
        return character;
    }

    private async Task SaveAsync(CharacterEntity character)
    {
        CharacterSaveSnapshot snapshot = CharacterSaveSnapshot.Take(character);
        await _saves.WriteAsync([snapshot.Batch]);
        character.SaveState.Acknowledge(snapshot.Marks);
    }

    private async Task<List<ItemInstance>> ItemsAsync()
    {
        await using CharacterDbContext read = _database.CreateDbContext();
        return await read.ItemInstances.AsNoTracking().ToListAsync();
    }

    private async Task<List<CharacterInventory>> SlotsAsync()
    {
        await using CharacterDbContext read = _database.CreateDbContext();
        return await read.CharacterInventory.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task Bring_a_sold_item_back_under_its_own_id_after_the_sale_was_saved()
    {
        InventoryItem blade = TestCharacters.Item(0, Blade, durability: 42);
        CharacterEntity character = await LoadedWithAsync(blade);
        VendorTrade trade = Trade(character);

        Assert.Equal(VendorResult.Ok, trade.TrySell(true, 0, null));
        await SaveAsync(character);
        Assert.Empty(await ItemsAsync());

        // Something else takes slot 0 first, so the buyback lands in slot 1.
        Assert.Equal(InventoryAddResult.Ok, TestCharacters.InventoryFor(character, Find).TryAdd(Tonic.Id, 1));
        Assert.Equal(VendorResult.Ok, trade.TryBuyback(true, 0));
        await SaveAsync(character);

        ItemInstance row = Assert.Single(await ItemsAsync(), i => i.Id == blade.InstanceId);
        Assert.Equal((42u, 1u), (row.Durability, row.Count));
        List<CharacterInventory> slots = await SlotsAsync();
        Assert.Equal(2, slots.Count);
        Assert.Equal(blade.InstanceId, slots.Single(s => s.Slot == 1).ItemId);
        Assert.False(character.SaveState.HasChanges);
    }

    /// <summary>
    /// Sold from slot 4, bought back into slot 1 because another item took slot 0 in between, all
    /// before any save. One save must leave slot 4's row deleted, slot 1 naming the original
    /// instance, and slot 0 holding the other item (#432).
    /// </summary>
    [Fact]
    public async Task Move_a_sold_item_to_its_new_slot_when_the_buyback_lands_elsewhere_before_any_save()
    {
        InventoryItem blade = TestCharacters.Item(4, Blade, durability: 42);
        CharacterEntity character = await LoadedWithAsync(blade);
        VendorTrade trade = Trade(character);

        Assert.Equal(VendorResult.Ok, trade.TrySell(true, 4, null));
        Assert.Equal(InventoryAddResult.Ok, TestCharacters.InventoryFor(character, Find).TryAdd(Tonic.Id, 1));
        Assert.Equal(VendorResult.Ok, trade.TryBuyback(true, 0));
        Assert.True(character.Container(InventoryType.Bag).TryGet(0, out InventoryItem tonic));
        Assert.True(character.Container(InventoryType.Bag).TryGet(1, out InventoryItem back));
        Assert.Equal(blade.InstanceId, back.InstanceId);
        await SaveAsync(character);

        List<CharacterInventory> slots = await SlotsAsync();
        Assert.Equal(blade.InstanceId, Assert.Single(slots, s => s.Slot == 1).ItemId);
        Assert.DoesNotContain(slots, s => s.Slot == 4);
        Assert.Equal(tonic.InstanceId, Assert.Single(slots, s => s.Slot == 0).ItemId);
        Assert.Equal(2, slots.Count);
        List<ItemInstance> rows = await ItemsAsync();
        Assert.Equal(42u, Assert.Single(rows, r => r.Id == blade.InstanceId).Durability);
        Assert.Equal(Tonic.Id, Assert.Single(rows, r => r.Id == tonic.InstanceId).TemplateId);
        Assert.Equal(2, rows.Count);
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public async Task Keep_the_row_when_the_buyback_comes_before_any_save()
    {
        InventoryItem blade = TestCharacters.Item(0, Blade, durability: 42);
        CharacterEntity character = await LoadedWithAsync(blade);
        VendorTrade trade = Trade(character);

        Assert.Equal(VendorResult.Ok, trade.TrySell(true, 0, null));
        Assert.Equal(VendorResult.Ok, trade.TryBuyback(true, 0));

        // Back in the slot it left, so the slot's Removed mark became Changed: an update, not a delete.
        Assert.Equal(SaveState.Changed, character.SaveState.SlotState(InventoryType.Bag, 0));
        CharacterSaveSnapshot snapshot = CharacterSaveSnapshot.Take(character);
        Assert.Contains(snapshot.Batch.UpsertItems, i => i.Id == blade.InstanceId);
        Assert.DoesNotContain(blade.InstanceId, snapshot.Batch.DeleteItems);
        Assert.Contains(snapshot.Batch.UpsertSlots, s => s.Slot == 0 && s.ItemId == blade.InstanceId);
        Assert.DoesNotContain((InventoryType.Bag, (ushort)0), snapshot.Batch.DeleteSlots);
        await _saves.WriteAsync([snapshot.Batch]);
        character.SaveState.Acknowledge(snapshot.Marks);

        // The slot row survived, still naming the same instance.
        Assert.Equal(blade.InstanceId, Assert.Single(await SlotsAsync()).ItemId);
        Assert.Equal(42u, Assert.Single(await ItemsAsync()).Durability);
        Assert.Equal(100UL, character.Data!.Money);
    }

    [Fact]
    public async Task Save_a_partial_sale_as_a_smaller_stack_and_the_copy_bought_back_as_a_new_row()
    {
        InventoryItem tonics = TestCharacters.Item(0, Tonic, count: 5);
        CharacterEntity character = await LoadedWithAsync(tonics);
        VendorTrade trade = Trade(character);

        Assert.Equal(VendorResult.Ok, trade.TrySell(true, 0, 2));
        await SaveAsync(character);
        Assert.Equal(3u, Assert.Single(await ItemsAsync()).Count);

        Assert.Equal(VendorResult.Ok, trade.TryBuyback(true, 0));
        await SaveAsync(character);

        List<ItemInstance> rows = await ItemsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(3u, rows.Single(r => r.Id == tonics.InstanceId).Count);
        Assert.Equal(2u, rows.Single(r => r.Id != tonics.InstanceId).Count);
        Assert.Equal(2, (await SlotsAsync()).Count);
    }
}
