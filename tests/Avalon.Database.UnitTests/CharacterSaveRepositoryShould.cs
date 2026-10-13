using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
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
    private static readonly GameplayWriteAuthority s_authority = new(new AccountId(1), Guid.NewGuid(), 1);

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

    /// <summary>
    /// An emptied slot is named by its owner, its container and its number, and one statement deletes every emptied
    /// slot of a container (#883). Each row kept here shares a slot number with one that goes, in another container
    /// or under another character. A slot emptied and filled again in the same save holds the new item.
    /// </summary>
    [Fact]
    public async Task Delete_only_the_emptied_slots_and_keep_one_filled_again_in_the_same_save()
    {
        CharacterRow saved = await SeedCharacterAsync(1);
        CharacterRow other = await SeedCharacterAsync(2);
        await FillAsync(saved,
            (InventoryType.Equipment, 0), (InventoryType.Equipment, 2),
            (InventoryType.Bag, 0), (InventoryType.Bag, 1), (InventoryType.Bag, 2),
            (InventoryType.Bank, 0), (InventoryType.Bank, 1), (InventoryType.Bank, 2));
        await FillAsync(other, (InventoryType.Bag, 0), (InventoryType.Bag, 2), (InventoryType.Bank, 1));
        ItemInstance refill = Item(saved.Id, count: 1);

        await _saves.WriteAsync([Batch(saved,
            upsertItems: [refill],
            upsertSlots: [Slot(saved.Id, 2, refill.Id)],
            deleteSlots: [(InventoryType.Bag, 0), (InventoryType.Bank, 1), (InventoryType.Bag, 2)])]);

        await using CharacterDbContext read = _database.CreateDbContext();
        List<CharacterInventory> left = await read.CharacterInventory.AsNoTracking().ToListAsync();
        (uint Owner, InventoryType Container, ushort Slot)[] expected =
        [
            (1, InventoryType.Equipment, 0), (1, InventoryType.Equipment, 2),
            (1, InventoryType.Bag, 1), (1, InventoryType.Bag, 2),
            (1, InventoryType.Bank, 0), (1, InventoryType.Bank, 2),
            (2, InventoryType.Bag, 0), (2, InventoryType.Bag, 2), (2, InventoryType.Bank, 1),
        ];
        Assert.Equal(expected, left.Select(r => (r.CharacterId.Value, r.Container, r.Slot)).Order());
        Assert.Equal(refill.Id, left.Single(r => r.CharacterId == saved.Id && r.Container == InventoryType.Bag && r.Slot == 2).ItemId);
    }

    /// <summary>
    /// One statement deletes the rows of every quest a save rewrites (#883), so the quests it does not name, and
    /// another character's rows for the quests it does, stay as they are.
    /// </summary>
    [Fact]
    public async Task Rewrite_only_the_quests_named_and_only_for_that_character()
    {
        CharacterRow saved = await SeedCharacterAsync(1);
        CharacterRow other = await SeedCharacterAsync(2);
        static CharacterQuest Quest(CharacterRow row, uint id, int stage) =>
            new() { CharacterId = row.Id, QuestId = id, State = CharacterQuestState.Active, Stage = stage, AcceptedAt = DateTime.UtcNow };
        static CharacterQuestObjective Objective(CharacterRow row, uint quest, uint id, uint progress) =>
            new() { CharacterId = row.Id, QuestId = quest, ObjectiveId = id, Progress = progress };
        static CharacterSaveBatch Write(CharacterRow row, uint[] rewrite, CharacterQuest[] active, CharacterQuestObjective[] objectives) =>
            new(row, [], [], [], [], Quests: new CharacterQuestWrite(rewrite, active, objectives, [])) { Authority = s_authority };
        await _saves.WriteAsync(
        [
            Write(saved, [], [Quest(saved, 5, 0), Quest(saved, 6, 0), Quest(saved, 7, 0)],
                [Objective(saved, 5, 51, 2), Objective(saved, 6, 61, 4), Objective(saved, 7, 71, 9)]),
            Write(other, [], [Quest(other, 5, 0), Quest(other, 6, 0)], [Objective(other, 5, 51, 3)]),
        ]);

        // Quest 5 moves on a stage, and quest 6 is abandoned.
        await _saves.WriteAsync([Write(saved, [5, 6], [Quest(saved, 5, 1)], [Objective(saved, 5, 52, 1)])]);

        await using CharacterDbContext read = _database.CreateDbContext();
        (uint Owner, uint Quest, int Stage)[] quests = [(1, 5, 1), (1, 7, 0), (2, 5, 0), (2, 6, 0)];
        (uint Owner, uint Quest, uint Objective, uint Progress)[] objectives = [(1, 5, 52, 1), (1, 7, 71, 9), (2, 5, 51, 3)];
        Assert.Equal(quests, (await read.CharacterQuests.AsNoTracking().ToListAsync())
            .Select(q => (q.CharacterId.Value, q.QuestId, q.Stage)).Order());
        Assert.Equal(objectives, (await read.CharacterQuestObjectives.AsNoTracking().ToListAsync())
            .Select(o => (o.CharacterId.Value, o.QuestId, o.ObjectiveId, o.Progress)).Order());
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

    [Fact]
    public async Task Insert_the_stats_row_when_there_is_none_and_update_it_when_there_is()
    {
        CharacterRow row = await SeedCharacterAsync(1);

        await _saves.WriteAsync([Batch(row, stats: new CharacterStats { CharacterId = row.Id, MaxHealth = 240 })]);
        await _saves.WriteAsync([Batch(row, stats: new CharacterStats { CharacterId = row.Id, MaxHealth = 260, Armor = 8 })]);

        await using CharacterDbContext read = _database.CreateDbContext();
        CharacterStats stored = await read.CharacterStats.AsNoTracking().SingleAsync();
        Assert.Equal((260u, 8u), (stored.MaxHealth, stored.Armor));
    }

    [Fact]
    public async Task Replace_every_aura_row_of_the_character_with_the_ones_written()
    {
        CharacterRow row = await SeedCharacterAsync(1);
        CharacterAura Aura(int slot, uint auraId) => new()
        {
            CharacterId = row.Id,
            Slot = slot,
            AuraId = auraId,
            CasterGuid = 5,
            Stacks = 1,
            RemainingMs = 4000,
            DurationMs = 6000,
            TicksLeft = 1,
            TickAmount = 2.5f,
            CritPct = 1f,
            CasterLevel = 3,
            PeriodicCarry = 0.75d,
            AppliedAt = DateTime.UtcNow,
        };

        await _saves.WriteAsync([new CharacterSaveBatch(row, [], [], [], [], Auras: new CharacterAuraWrite([Aura(0, 1), Aura(1, 2)])) { Authority = s_authority }]);
        await _saves.WriteAsync([new CharacterSaveBatch(row, [], [], [], [], Auras: new CharacterAuraWrite([Aura(0, 7)])) { Authority = s_authority }]);

        await using CharacterDbContext read = _database.CreateDbContext();
        CharacterAura only = Assert.Single(await read.CharacterAuras.AsNoTracking().ToListAsync());
        Assert.Equal((0, 7u, 4000u, 2.5f, 0.75d), (only.Slot, only.AuraId, only.RemainingMs, only.TickAmount, only.PeriodicCarry));
    }

    private async Task<CharacterRow> SeedCharacterAsync(uint id)
    {
        var row = new CharacterRow
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = $"Saver{id}",
            CreationDate = DateTime.UtcNow,
        };
        await using CharacterDbContext context = _database.CreateDbContext();
        context.Characters.Add(row);
        await context.SaveChangesAsync();
        var fences = new GameplayFenceRepository(_database);
        Assert.True(await fences.AdvanceAsync(s_authority, false, DateTime.UtcNow.AddSeconds(44), CancellationToken.None));
        Assert.True(await fences.ActivateAsync(s_authority, DateTime.UtcNow.AddSeconds(44), CancellationToken.None));
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

    private static CharacterInventory Slot(CharacterId owner, ushort slot, ItemInstanceId item,
        InventoryType container = InventoryType.Bag) =>
        new() { CharacterId = owner, Container = container, Slot = slot, ItemId = item };

    /// <summary>Saves one new item into each of <paramref name="slots" />.</summary>
    private async Task FillAsync(CharacterRow owner, params (InventoryType Container, ushort Slot)[] slots)
    {
        var items = slots.Select(_ => Item(owner.Id, count: 1)).ToList();
        var rows = slots.Select((s, i) => Slot(owner.Id, s.Slot, items[i].Id, s.Container)).ToList();
        await _saves.WriteAsync([Batch(owner, upsertItems: items, upsertSlots: rows)]);
    }

    private static CharacterSaveBatch Batch(
        CharacterRow row,
        IReadOnlyList<ItemInstance>? upsertItems = null,
        IReadOnlyList<ItemInstanceId>? deleteItems = null,
        IReadOnlyList<CharacterInventory>? upsertSlots = null,
        IReadOnlyList<(InventoryType Container, ushort Slot)>? deleteSlots = null,
        CharacterStats? stats = null) =>
        new(row, upsertItems ?? [], deleteItems ?? [], upsertSlots ?? [], deleteSlots ?? [], stats) { Authority = s_authority };
}
