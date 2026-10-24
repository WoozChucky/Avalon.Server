using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        row.Gold = 500;
        ItemInstance sword = Item(row.Id, count: 1);

        await _saves.WriteAsync([Batch(row, upsertItems: [sword], upsertSlots: [Slot(row.Id, 0, sword.Id)])]);

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(500UL, (await read.Characters.AsNoTracking().SingleAsync()).Gold);
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

        CharacterSaveBatch removal = Batch(row, deleteItems: [potion.Id], deleteSlots: [(InventoryType.Bag, (byte)0)]);
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
        (uint Owner, InventoryType Container, byte Slot)[] expected =
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
        saved.Gold = 100;
        var neverCreated = new CharacterRow { Id = new CharacterId(99), AccountId = new AccountId(1), Name = "Ghost" };
        ItemInstance sword = Item(saved.Id, count: 1);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _saves.WriteAsync(
        [
            Batch(saved, upsertItems: [sword], upsertSlots: [Slot(saved.Id, 0, sword.Id)]),
            Batch(neverCreated),
        ]));

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(0UL, (await read.Characters.AsNoTracking().SingleAsync()).Gold);
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
            Batch(giver, deleteItems: [gem.Id], deleteSlots: [(InventoryType.Bag, (byte)0)]),
        ]);

        await using CharacterDbContext read = _database.CreateDbContext();
        ItemInstance stored = await read.ItemInstances.AsNoTracking().SingleAsync();
        Assert.Equal(taker.Id, stored.CharacterId);
        CharacterInventory slot = await read.CharacterInventory.AsNoTracking().SingleAsync();
        Assert.Equal(taker.Id, slot.CharacterId);
        Assert.Equal((byte)4, slot.Slot);
    }

    /// <summary>
    /// A trade is one paired save carrying its record, and its outcome is one of three (#1175): committed; refused or
    /// failed before the commit, which propagates as it came and means nothing was written; or unknown, because the
    /// commit itself failed or its acknowledgement was lost. Only the last throws <see cref="SaveOutcomeUnknownException" />,
    /// and the record settles it: it exists exactly when the swap was committed.
    /// </summary>
    [Theory]
    [InlineData("committed", null, true)]
    [InlineData("acknowledgement lost", typeof(SaveOutcomeUnknownException), true)]
    [InlineData("commit failed", typeof(SaveOutcomeUnknownException), false)]
    [InlineData("fence refused", typeof(GameplayWriteRejectedException), false)]
    public async Task Report_a_trade_write_as_committed_refused_or_unknown_and_find_its_record(string row, Type? thrown, bool stored)
    {
        var accountA = new AccountId(3);
        var accountB = new AccountId(4);
        var authorityA = new GameplayWriteAuthority(accountA, Guid.NewGuid(), 1);
        var authorityB = new GameplayWriteAuthority(accountB, Guid.NewGuid(), 1);
        CharacterRow a = await SeedCharacterAsync(7, authorityA);
        CharacterRow b = await SeedCharacterAsync(8, authorityB);
        ItemInstance sword = Item(a.Id, count: 1);
        await _saves.WriteAsync([Batch(a, upsertItems: [sword], upsertSlots: [Slot(a.Id, 0, sword.Id)], authority: authorityA)]);
        if (row == "fence refused")
            Assert.True(await new GameplayFenceRepository(_database).EndAsync(authorityB, CancellationToken.None));

        CharacterSaveBatch[] batches =
        [
            Batch(a, deleteItems: [sword.Id], deleteSlots: [(InventoryType.Bag, (byte)0)], authority: authorityA),
            Batch(b, upsertItems: [Item(b.Id, count: 1, id: sword.Id)], upsertSlots: [Slot(b.Id, 3, sword.Id)], authority: authorityB),
        ];
        var tradeId = Guid.CreateVersion7();
        var modifierId = Guid.CreateVersion7();
        var record = new TradeRecord
        {
            Id = tradeId,
            FirstCharacterId = a.Id,
            SecondCharacterId = b.Id,
            FirstGold = 0,
            SecondGold = 0,
            CompletedAt = DateTime.UtcNow,
            Items =
            [
                new TradeRecordItem
                {
                    TradeId = tradeId,
                    ItemInstanceId = sword.Id,
                    GiverCharacterId = a.Id,
                    TemplateId = sword.TemplateId,
                    Count = 1,
                    Rarity = ItemRarity.Rare,
                    ItemLevel = 9,
                    RareName = "Thorn Bite",
                    Modifiers =
                    [
                        new TradeRecordItemModifier
                        {
                            TradeId = tradeId,
                            ItemInstanceModifierId = modifierId,
                            ItemInstanceId = sword.Id,
                            Ordinal = 0,
                            ModifierId = 202,
                            Values =
                            [
                                new TradeRecordItemModifierValue
                                { TradeId = tradeId, ItemInstanceModifierId = modifierId, Index = 0, Stat = StatType.AddedFireDamage, Value = 2, Value2 = 5 },
                                new TradeRecordItemModifierValue
                                { TradeId = tradeId, ItemInstanceModifierId = modifierId, Index = 1, Stat = StatType.Strength, Value = 4 },
                            ],
                        },
                    ],
                },
            ],
        };
        var saves = new CharacterSaveRepository(new ScriptedTransactions(_database, row));

        Exception? e = await Record.ExceptionAsync(() => saves.WriteTradeAsync(batches, record));

        Assert.Equal(thrown, e?.GetType());
        Assert.Equal(stored, await _saves.TradeRecordExistsAsync(record.Id, accountA, accountB));
        // The record and the swap stand or fall together.
        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(stored ? 8L : 7L,
            (await read.ItemInstances.AsNoTracking().SingleAsync(i => i.Id == sword.Id)).CharacterId.Value);
        if (stored)
        {
            TradeRecordItem moved = await read.TradeRecordItems.AsNoTracking()
                .Include(i => i.Modifiers).ThenInclude(m => m.Values)
                .SingleAsync(i => i.TradeId == record.Id);
            Assert.Equal((sword.Id, ItemRarity.Rare, (ushort)9, "Thorn Bite"),
                (moved.ItemInstanceId, moved.Rarity, moved.ItemLevel, moved.RareName));
            TradeRecordItemModifier modifier = Assert.Single(moved.Modifiers);
            Assert.Equal((modifierId, (byte)0, 202), (modifier.ItemInstanceModifierId, modifier.Ordinal, modifier.ModifierId));
            Assert.Equal([(StatType.AddedFireDamage, 2, (int?)5), (StatType.Strength, 4, null)],
                modifier.Values.OrderBy(v => v.Index).Select(v => (v.Stat, v.Value, v.Value2)));
        }
    }

    /// <summary>
    /// An item's modifiers change over its life (crafting, #1180): its save leaves exactly the set it carries, a row kept
    /// by id updated in place under that id, and every other row gone with its values. An item the save does not rewrite
    /// keeps its modifiers and their values. A rare item keeps the name its roll gave it (#1179), and only a rare one may
    /// carry a name (CK_ItemInstances_RareName).
    /// </summary>
    [Fact]
    public async Task Replace_an_items_modifier_rows_by_id_in_its_save()
    {
        CharacterRow row = await SeedCharacterAsync(1);
        ItemInstance item = Item(row.Id, count: 1, rarity: ItemRarity.Rare, itemLevel: 7, rareName: "Thorn Bite");
        ItemInstance untouched = Item(row.Id, count: 1, rarity: ItemRarity.Magic, itemLevel: 4);
        ItemInstanceModifier a = Modifier(item.Id, 202, 0, Value(0, StatType.AddedFireDamage, 2, 5));
        ItemInstanceModifier b = Modifier(item.Id, 10103, 1, Value(0, StatType.Strength, 8, null));
        ItemInstanceModifier other = Modifier(untouched.Id, 10103, 0, Value(0, StatType.Strength, 5, null));
        await _saves.WriteAsync([Batch(row, upsertItems: [item, untouched],
            upsertSlots: [Slot(row.Id, 0, item.Id), Slot(row.Id, 1, untouched.Id)], modifiers: Write(a, b, other))]);

        ItemInstance saved = await SavedAsync(row.Id, item.Id);
        Assert.Equal((ItemRarity.Rare, (ushort)7, "Thorn Bite"), (saved.Rarity, saved.ItemLevel, saved.RareName));
        Assert.Equal(Describe(a, b), Describe([.. saved.Modifiers]));

        ItemInstanceModifier kept = Modifier(item.Id, 202, 1, Value(0, StatType.AddedFireDamage, 3, 6), a.Id);
        ItemInstanceModifier added = Modifier(item.Id, 203, 0, Value(0, StatType.AddedColdDamage, 1, 4));
        ItemInstance again = Item(row.Id, count: 1, item.Id, ItemRarity.Rare, 7, "Thorn Bite");
        await _saves.WriteAsync([Batch(row, upsertItems: [again], modifiers: Write(kept, added))]);

        Assert.Equal(Describe(added, kept), Describe([.. (await SavedAsync(row.Id, item.Id)).Modifiers]));
        Assert.Equal(Describe(other), Describe([.. (await SavedAsync(row.Id, untouched.Id)).Modifiers]));
        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(new[] { added.Id, kept.Id, other.Id }.Order(), (await read.Set<ItemInstanceModifierValue>().AsNoTracking().ToListAsync())
            .Select(v => v.ItemInstanceModifierId).Order());

        ItemInstance named = Item(row.Id, count: 1, rarity: ItemRarity.Magic, itemLevel: 7, rareName: "Thorn Bite");
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _saves.WriteAsync(
            [Batch(row, upsertItems: [named], upsertSlots: [Slot(row.Id, 2, named.Id)])]));
    }

    /// <summary>
    /// One save mark per item keeps an item and its modifiers in the same save, so a modifier write for an item the batch
    /// does not upsert, another character's or one of its own left unchanged, is refused, and nothing is written. So are a
    /// modifier on no item the write names, a value row for a modifier it does not carry, and a modifier id already stored
    /// under another item, which would otherwise move that row.
    /// </summary>
    [Fact]
    public async Task Refuse_a_modifier_write_for_an_item_its_batch_does_not_upsert()
    {
        CharacterRow row = await SeedCharacterAsync(1);
        CharacterRow other = await SeedCharacterAsync(2);
        ItemInstance own = Item(row.Id, count: 1);
        ItemInstance theirs = Item(other.Id, count: 1);
        ItemInstanceModifier stored = Modifier(theirs.Id, 202, 0, Value(0, StatType.AddedFireDamage, 2, 5));
        await _saves.WriteAsync([Batch(row, upsertItems: [own], upsertSlots: [Slot(row.Id, 0, own.Id)])]);
        await _saves.WriteAsync([Batch(other, upsertItems: [theirs], upsertSlots: [Slot(other.Id, 0, theirs.Id)], modifiers: Write(stored))]);
        row.Gold = 99;

        ItemModifierWrite onOwn = Write(Modifier(own.Id, 203, 0, Value(0, StatType.Strength, 4, null)));
        CharacterSaveBatch[] refused =
        [
            Batch(row, modifiers: Write(Modifier(theirs.Id, 202, 0, Value(0, StatType.AddedFireDamage, 2, 5)))),
            Batch(row, modifiers: onOwn),
            Batch(row, upsertItems: [Item(row.Id, count: 1, own.Id)], modifiers: onOwn with
            {
                Modifiers = [new ItemInstanceModifier { Id = onOwn.Modifiers[0].Id, ItemInstanceId = theirs.Id, ModifierId = 203 }],
            }),
            Batch(row, upsertItems: [Item(row.Id, count: 1, own.Id)], modifiers: onOwn with
            {
                Values = [new ItemInstanceModifierValue { ItemInstanceModifierId = Guid.CreateVersion7(), Stat = StatType.Strength, Value = 4 }],
            }),
            Batch(row, upsertItems: [Item(row.Id, count: 1, own.Id)],
                modifiers: Write(Modifier(own.Id, 202, 0, Value(0, StatType.AddedFireDamage, 2, 5), stored.Id))),
        ];

        foreach (CharacterSaveBatch batch in refused)
            await Assert.ThrowsAsync<GameplayWriteRejectedException>(() => _saves.WriteAsync([batch]));

        await using CharacterDbContext read = _database.CreateDbContext();
        Assert.Equal(0UL, (await read.Characters.AsNoTracking().SingleAsync(c => c.Id == row.Id)).Gold);
        ItemInstanceModifier left = Assert.Single(await read.Set<ItemInstanceModifier>().AsNoTracking().ToListAsync());
        Assert.Equal((stored.Id, theirs.Id), (left.Id, left.ItemInstanceId));
        Assert.Equal(stored.Id, Assert.Single(await read.Set<ItemInstanceModifierValue>().AsNoTracking().ToListAsync()).ItemInstanceModifierId);
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

    private async Task<CharacterRow> SeedCharacterAsync(uint id, GameplayWriteAuthority? authority = null)
    {
        authority ??= s_authority;
        var row = new CharacterRow
        {
            Id = new CharacterId(id),
            AccountId = authority.AccountId,
            Name = "Saver" + (char)('a' + id),
            CreationDate = DateTime.UtcNow,
        };
        await using CharacterDbContext context = _database.CreateDbContext();
        context.Characters.Add(row);
        await context.SaveChangesAsync();
        var fences = new GameplayFenceRepository(_database);
        Assert.NotNull(await fences.AdvanceAsync(authority, false, DateTime.UtcNow.AddSeconds(44), CancellationToken.None));
        Assert.NotNull(await fences.ActivateAsync(authority, DateTime.UtcNow.AddSeconds(44), CancellationToken.None));
        context.Entry(row).State = EntityState.Detached;
        return row;
    }

    private static ItemInstance Item(CharacterId owner, uint count, ItemInstanceId? id = null,
        ItemRarity rarity = ItemRarity.Common, ushort itemLevel = 1, string? rareName = null) =>
        new()
        {
            Id = id ?? new ItemInstanceId(Guid.CreateVersion7()),
            TemplateId = new ItemTemplateId(1),
            CharacterId = owner,
            Count = count,
            Flags = ItemInstanceFlags.None,
            Rarity = rarity,
            ItemLevel = itemLevel,
            RareName = rareName,
            UpdatedAt = DateTime.UtcNow,
        };

    /// <summary>A modifier with its one value row, under a new id unless one is given.</summary>
    private static ItemInstanceModifier Modifier(ItemInstanceId item, ushort modifierId, byte ordinal,
        ItemInstanceModifierValue value, Guid? id = null)
    {
        Guid modifier = id ?? Guid.CreateVersion7();
        value.ItemInstanceModifierId = modifier;
        return new ItemInstanceModifier { Id = modifier, ItemInstanceId = item, ModifierId = modifierId, Ordinal = ordinal, Values = [value] };
    }

    private static ItemInstanceModifierValue Value(byte index, StatType stat, int value, int? value2) =>
        new() { Index = index, Stat = stat, Value = value, Value2 = value2 };

    /// <summary>The flat rows a save writes for <paramref name="modifiers" />: each item's whole set, then every value row.</summary>
    private static ItemModifierWrite Write(params ItemInstanceModifier[] modifiers) => new(
        [.. modifiers.Select(m => m.ItemInstanceId).Distinct()],
        [.. modifiers.Select(m => new ItemInstanceModifier { Id = m.Id, ItemInstanceId = m.ItemInstanceId, ModifierId = m.ModifierId, Ordinal = m.Ordinal })],
        [.. modifiers.SelectMany(m => m.Values)]);

    /// <summary>The saved item, with its modifiers and their values, as the character's items are read.</summary>
    private async Task<ItemInstance> SavedAsync(CharacterId owner, ItemInstanceId id) =>
        (await new ItemInstanceRepository(_database).GetByCharacterIdAsync(owner)).Single(i => i.Id == id);

    /// <summary>Modifiers in ordinal order, each with its values in index order.</summary>
    private static string Describe(params ItemInstanceModifier[] modifiers) =>
        string.Join("; ", modifiers.OrderBy(m => m.Ordinal).Select(m => $"{m.Id} {m.ModifierId}@{m.Ordinal}: " +
            string.Join(", ", m.Values.OrderBy(v => v.Index).Select(v => $"{v.Index} {v.Stat} {v.Value}-{v.Value2}"))));

    private static CharacterInventory Slot(CharacterId owner, byte slot, ItemInstanceId item,
        InventoryType container = InventoryType.Bag) =>
        new() { CharacterId = owner, Container = container, Slot = slot, ItemId = item };

    /// <summary>Saves one new item into each of <paramref name="slots" />.</summary>
    private async Task FillAsync(CharacterRow owner, params (InventoryType Container, byte Slot)[] slots)
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
        IReadOnlyList<(InventoryType Container, byte Slot)>? deleteSlots = null,
        CharacterStats? stats = null,
        GameplayWriteAuthority? authority = null,
        ItemModifierWrite? modifiers = null) =>
        new(row, upsertItems ?? [], deleteItems ?? [], upsertSlots ?? [], deleteSlots ?? [], stats, Modifiers: modifiers)
        { Authority = authority ?? s_authority };

    /// <summary>
    /// Runs the work in a real transaction over the test database, then ends it as <c>row</c> says: the commit lands
    /// but its acknowledgement is lost, the commit fails (the transaction rolls back), or it commits.
    /// </summary>
    private sealed class ScriptedTransactions(IDbContextFactory<CharacterDbContext> factory, string row)
        : IDbTransactionRunner<CharacterDbContext>
    {
        public Task ExecuteAsync(Func<CharacterDbContext, CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
            ExecuteAsync<object?>(async (context, token) =>
            {
                await work(context, token);
                return null;
            }, cancellationToken);

        public async Task<TResult> ExecuteAsync<TResult>(Func<CharacterDbContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken = default)
        {
            await using CharacterDbContext context = await factory.CreateDbContextAsync(cancellationToken);
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            TResult result = await work(context, cancellationToken);
            switch (row)
            {
                case "acknowledgement lost":
                    await transaction.CommitAsync(cancellationToken);
                    throw new TimeoutException("The commit's acknowledgement was lost.");
                case "commit failed":
                    await transaction.RollbackAsync(cancellationToken);
                    throw new TimeoutException("The commit failed.");
                default:
                    await transaction.CommitAsync(cancellationToken);
                    return result;
            }
        }
    }
}
