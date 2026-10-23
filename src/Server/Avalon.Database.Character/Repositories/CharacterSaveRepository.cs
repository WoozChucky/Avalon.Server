using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Avalon.Database.Character.Repositories;

/// <summary>
/// A character's quest rows to write (#433). Every quest in <paramref name="Rewrite" /> has its active row and
/// objective rows deleted, and then <paramref name="Active" /> and <paramref name="Objectives" /> are inserted
/// (only quests still active are there); <paramref name="Completed" /> rows are inserted when missing, else their last
/// turn-in time and count are updated (#710), by column. Idempotent: the values written are absolute, never increments.
/// </summary>
public sealed record CharacterQuestWrite(
    IReadOnlyList<uint> Rewrite,
    IReadOnlyList<CharacterQuest> Active,
    IReadOnlyList<CharacterQuestObjective> Objectives,
    IReadOnlyList<CharacterCompletedQuest> Completed);

/// <summary>
/// The modifier rows of some of a batch's items (#1179), as flat rows. Each item in <paramref name="Items" /> ends up
/// with exactly <paramref name="Modifiers" />' rows for it: its other modifiers are deleted with their values, each one
/// here is updated when its id exists and inserted otherwise, and its value rows are replaced by
/// <paramref name="Values" />' rows for it. A modifier here carries no values of its own (its <c>Values</c> is empty):
/// every value row is in <paramref name="Values" />. Every item named must be one its batch upserts.
/// </summary>
/// <remarks>
/// Single use, like the batch that carries it. While the write tracks these rows, EF's relationship fix-up adds the
/// modifiers to the batch's <see cref="ItemInstance.Modifiers" /> and the values to each
/// <see cref="ItemInstanceModifier.Values" />. A second write of the same objects would then reach those rows through a
/// navigation, and the repository's one-row rule refuses that. Build fresh rows for every save, as the snapshot does.
/// </remarks>
public sealed record ItemModifierWrite(
    IReadOnlyList<ItemInstanceId> Items,
    IReadOnlyList<ItemInstanceModifier> Modifiers,
    IReadOnlyList<ItemInstanceModifierValue> Values);

/// <summary>
/// What one character's save writes: its whole row, and only the items and slots whose save state
/// was not Unchanged. Items and slots to upsert carry their current values; the rest are deleted.
/// <paramref name="Stats" />, when present, is the character's derived-stats row, upserted.
/// <paramref name="Quests" />, when present, is the quest rows to rewrite (#433).
/// <paramref name="Ignores" />, when present, is the ignore entries to rewrite (#723).
/// <paramref name="Auras" />, when present, replaces every aura row of the character.
/// <paramref name="Modifiers" />, when present, rewrites the modifier rows of some of the upserted items (#1179).
/// </summary>
public sealed record CharacterSaveBatch(
    Domain.Characters.Character Row,
    IReadOnlyList<ItemInstance> UpsertItems,
    IReadOnlyList<ItemInstanceId> DeleteItems,
    IReadOnlyList<CharacterInventory> UpsertSlots,
    IReadOnlyList<(InventoryType Container, byte Slot)> DeleteSlots,
    CharacterStats? Stats = null,
    CharacterQuestWrite? Quests = null,
    CharacterIgnoreWrite? Ignores = null,
    CharacterAuraWrite? Auras = null,
    ItemModifierWrite? Modifiers = null)
{
    public GameplayWriteAuthority? Authority { get; init; }
}

public interface ICharacterSaveRepository
{
    /// <summary>
    /// Writes every batch in one Character-DB transaction. Throws on any failure, and then nothing
    /// is committed. Idempotent: an upsert of a row that exists updates it, and a delete of a row
    /// that does not exist does nothing, so a save may safely carry an entry an earlier save has
    /// already written.
    /// </summary>
    /// <remarks>
    /// Caller contracts, which this method does not check:
    /// <list type="bullet">
    /// <item>At most one batch per character in a call, and each item id and each slot key at most
    /// once among the upserts of a call. A repeated upsert is tracked twice, EF refuses the second,
    /// and the whole call fails, every time it is retried. A delete in one batch and an upsert of the
    /// same item in another (a trade) is allowed: every delete runs first.</item>
    /// <item>Saves for one character are serialised by the caller: a call for a character starts only
    /// once the previous call for that character has finished. Otherwise an insert still in flight
    /// can land after a later delete of the same row and leave it behind.</item>
    /// </list>
    /// <c>CharacterSaver</c> in Avalon.World honours both: it builds one batch per character from
    /// marks keyed by item and slot, refuses a multi-character save that names a character twice,
    /// and chains every save behind the previous one for the same character.
    /// <para>Three outcomes (#1175): it returns once committed; it throws <see cref="SaveOutcomeUnknownException" />
    /// when the commit itself failed, or succeeded with its acknowledgement lost; and any other exception (a fence or
    /// lease refusal <see cref="GameplayWriteRejectedException" />, the deadline, a constraint, a connection that never
    /// opened) came before the commit, rolled the transaction back, and means nothing was written.</para>
    /// </remarks>
    Task WriteAsync(IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="WriteAsync" /> for a trade (#1175): the same write, with <paramref name="record" /> inserted in the same
    /// transaction, so the record exists exactly when the swap committed. The same three outcomes; on
    /// <see cref="SaveOutcomeUnknownException" />, <see cref="TradeRecordExistsAsync" /> tells which it was.
    /// </summary>
    Task WriteTradeAsync(IReadOnlyList<CharacterSaveBatch> batches, TradeRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the trade <paramref name="tradeId" /> was committed (#1175). In its own transaction it first takes both
    /// accounts' gameplay fence locks, the lower account id first as a save takes them, and only then reads: a paired
    /// write still committing holds those locks, so the read waits for it to finish and never answers "absent" for a
    /// write that then commits. Any failure throws; it never guesses.
    /// </summary>
    Task<bool> TradeRecordExistsAsync(Guid tradeId, AccountId first, AccountId second, CancellationToken cancellationToken = default);
}

public partial class CharacterSaveRepository(IDbTransactionRunner<CharacterDbContext> transactions, TimeProvider? clock = null) : ICharacterSaveRepository
{
    public Task WriteAsync(IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken = default) =>
        WriteCoreAsync(batches, record: null, cancellationToken);

    public Task WriteTradeAsync(IReadOnlyList<CharacterSaveBatch> batches, TradeRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return WriteCoreAsync(batches, record, cancellationToken);
    }

    public Task<bool> TradeRecordExistsAsync(Guid tradeId, AccountId first, AccountId second, CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync(async (context, token) =>
        {
            // The order every save takes them in (GuardAsync), so this read and a paired save never deadlock. On Postgres
            // the read runs at Read Committed, so a statement after the wait sees what the write it waited for committed.
            (AccountId lower, AccountId higher) = first.Value <= second.Value ? (first, second) : (second, first);
            await GameplayFenceRepository.LockAsync(context, lower, token);
            if (higher.Value != lower.Value)
                await GameplayFenceRepository.LockAsync(context, higher, token);
            return await context.TradeRecords.AnyAsync(r => r.Id == tradeId, token);
        }, cancellationToken);

    /// <summary>
    /// Runs the write and sorts its failure. <c>workDone</c> is set on the work's last line, after the one SaveChanges
    /// and the deadline check, and the runner's only step after the work is the commit (and disposing the transaction
    /// and context). So an exception with it set came from the commit or after it, and the outcome is unknown; one
    /// without it came before the commit, and the runner's transaction, never committed, rolled back.
    /// </summary>
    private async Task WriteCoreAsync(IReadOnlyList<CharacterSaveBatch> batches, TradeRecord? record, CancellationToken cancellationToken)
    {
        bool workDone = false;
        try
        {
            await transactions.ExecuteAsync(async (context, token) =>
            {
                await WriteBatchesAsync(context, batches, record, token);
                workDone = true;
            }, cancellationToken);
        }
        catch (Exception e) when (workDone)
        {
            throw new SaveOutcomeUnknownException(e);
        }
    }

    private async Task WriteBatchesAsync(CharacterDbContext context, IReadOnlyList<CharacterSaveBatch> batches, TradeRecord? record,
        CancellationToken token)
    {
        AccountGameplayFence[] guards = await GuardAsync(context, batches, token);
        context.ValidatedGameplaySave = true;
        // Every delete, for every batch, before any upsert: a row one batch removes and another
        // adds (a trade) must end up present, whichever order the batches came in.
        foreach (CharacterSaveBatch batch in batches)
        {
            CharacterId owner = batch.Row.Id;
            if (batch.DeleteSlots.Count > 0)
                await DeleteSlotsAsync(context, owner, batch.DeleteSlots, token);

            if (batch.Quests is { Rewrite.Count: > 0 } quests)
            {
                // Two statements per batch, not two per rewritten quest (#883): the objectives, then the quests.
                var rewrite = quests.Rewrite.ToList();
                await context.CharacterQuestObjectives
                    .Where(o => o.CharacterId == owner && rewrite.Contains(o.QuestId))
                    .ExecuteDeleteAsync(token);
                await context.CharacterQuests
                    .Where(q => q.CharacterId == owner && rewrite.Contains(q.QuestId))
                    .ExecuteDeleteAsync(token);
            }

            if (batch.Ignores is { } ignores)
                await DeleteIgnoresAsync(context, owner, ignores, token);

            if (batch.Auras is not null)
                await context.CharacterAuras.Where(a => a.CharacterId == owner).ExecuteDeleteAsync(token);
        }

        // After the slots, which reference items by foreign key.
        foreach (CharacterSaveBatch batch in batches)
        {
            if (batch.DeleteItems.Count == 0)
                continue;

            // One statement per batch, not one per item (#883).
            var removed = batch.DeleteItems.ToList();
            await context.ItemInstances.Where(i => removed.Contains(i.Id)).ExecuteDeleteAsync(token);
        }

        // An item whose modifiers are rewritten loses every modifier not in its set, the values going by cascade, and
        // the value rows of those in it, which are inserted again below: one statement each per batch.
        foreach (CharacterSaveBatch batch in batches)
        {
            if (batch.Modifiers is not { Items.Count: > 0 } modifiers)
                continue;

            var rewritten = modifiers.Items.ToList();
            var kept = modifiers.Modifiers.Select(m => m.Id).ToList();
            await context.ItemInstanceModifiers
                .Where(m => rewritten.Contains(m.ItemInstanceId) && !kept.Contains(m.Id))
                .ExecuteDeleteAsync(token);
            if (kept.Count > 0)
            {
                await context.ItemInstanceModifierValues
                    .Where(v => kept.Contains(v.ItemInstanceModifierId))
                    .ExecuteDeleteAsync(token);
            }
        }

        foreach (CharacterSaveBatch batch in batches)
        {
            context.TrackForUpdate(batch.Row);

            if (batch.UpsertItems.Count > 0)
            {
                // One query per batch for which of its items already exist, not one per item.
                var ids = batch.UpsertItems.Select(i => i.Id).ToList();
                var existingItems = (await context.ItemInstances
                        .Where(i => ids.Contains(i.Id))
                        .Select(i => i.Id)
                        .ToListAsync(token))
                    .ToHashSet();

                foreach (ItemInstance item in batch.UpsertItems)
                {
                    if (existingItems.Contains(item.Id))
                        context.TrackForUpdate(item);
                    else
                        context.TrackForInsert(item);
                }
            }

            if (batch.Modifiers is { Modifiers.Count: > 0 } modifierWrite)
                await UpsertModifiersAsync(context, modifierWrite, token);

            if (batch.UpsertSlots.Count > 0)
            {
                CharacterId owner = batch.Row.Id;
                var existingSlots = (await context.CharacterInventory
                        .Where(r => r.CharacterId == owner)
                        .Select(r => new { r.Container, r.Slot })
                        .ToListAsync(token))
                    .Select(key => (key.Container, key.Slot))
                    .ToHashSet();

                foreach (CharacterInventory slot in batch.UpsertSlots)
                {
                    if (existingSlots.Contains((slot.Container, slot.Slot)))
                        context.TrackForUpdate(slot);
                    else
                        context.TrackForInsert(slot);
                }
            }

            if (batch.Stats is { } stats)
            {
                // Every character has had a stats row since creation, but upserting costs one
                // query and survives a row lost to an old bug.
                CharacterId statsOwner = stats.CharacterId;
                bool exists = await context.CharacterStats.AnyAsync(s => s.CharacterId == statsOwner, token);
                if (exists)
                    context.TrackForUpdate(stats);
                else
                    context.TrackForInsert(stats);
            }

            if (batch.Quests is { } write)
            {
                foreach (CharacterQuest quest in write.Active)
                    context.TrackForInsert(quest);
                foreach (CharacterQuestObjective objective in write.Objectives)
                    context.TrackForInsert(objective);

                if (write.Completed.Count > 0)
                {
                    CharacterId who = batch.Row.Id;
                    var ids = write.Completed.Select(c => c.QuestId).ToList();
                    var stored = (await context.CharacterCompletedQuests
                            .Where(c => c.CharacterId == who && ids.Contains(c.QuestId))
                            .Select(c => c.QuestId)
                            .ToListAsync(token))
                        .ToHashSet();
                    foreach (CharacterCompletedQuest completed in write.Completed)
                    {
                        if (!stored.Contains(completed.QuestId))
                        {
                            context.TrackForInsert(completed);
                            continue;
                        }

                        // Written by column, never whole: the first turn-in time stays as stored. Raw Attach, not
                        // TrackForUpdate, which marks every column; the row has no navigation to drag a graph in.
                        EntityEntry<CharacterCompletedQuest> entry = context.CharacterCompletedQuests.Attach(completed);
                        entry.Property(c => c.LastCompletedAt).IsModified = true;
                        entry.Property(c => c.CompletionCount).IsModified = true;
                    }
                }
            }

            if (batch.Ignores is { } ignoreWrite)
                await InsertIgnoresAsync(context, ignoreWrite, token);

            if (batch.Auras is { } auras)
            {
                foreach (CharacterAura aura in auras.Rows)
                    context.TrackForInsert(aura);
            }
        }

        // A trade's record goes in with the swap, so it exists exactly when the swap committed. Add, not
        // TrackForInsert: the record and its item rows are one new graph, written together on this context.
        if (record is not null)
            context.TradeRecords.Add(record);

        // One SaveChanges: EF orders the item inserts ahead of the slot inserts that reference them.
        await context.SaveChangesAsync(token);
        await CheckDeadlineAsync(context, guards, token);
    }

    /// <summary>
    /// Each modifier is updated when its id exists and inserted otherwise, after one query per batch for which exist, and
    /// its value rows, deleted already, are inserted. One SaveChanges orders item, modifier and value inserts by foreign
    /// key. A modifier never moves to another item: one stored under a different item refuses the save.
    /// </summary>
    private static async Task UpsertModifiersAsync(CharacterDbContext context, ItemModifierWrite write, CancellationToken token)
    {
        var ids = write.Modifiers.Select(m => m.Id).ToList();
        var stored = (await context.ItemInstanceModifiers
                .Where(m => ids.Contains(m.Id))
                .Select(m => new { m.Id, m.ItemInstanceId })
                .ToListAsync(token))
            .ToDictionary(m => m.Id, m => m.ItemInstanceId);

        foreach (ItemInstanceModifier modifier in write.Modifiers)
        {
            if (!stored.TryGetValue(modifier.Id, out ItemInstanceId? item))
                context.TrackForInsert(modifier);
            else if (item == modifier.ItemInstanceId)
                context.TrackForUpdate(modifier);
            else
                throw new GameplayWriteRejectedException();
        }

        foreach (ItemInstanceModifierValue value in write.Values)
            context.TrackForInsert(value);
    }

    /// <summary>
    /// One statement per container that has emptied slots, not one per slot (#883): a slot's key is the owner, the
    /// container and the slot number, so the slots of one container go in one list.
    /// </summary>
    private static async Task DeleteSlotsAsync(CharacterDbContext context, CharacterId owner,
        IReadOnlyList<(InventoryType Container, byte Slot)> emptied, CancellationToken token)
    {
        foreach (IGrouping<InventoryType, byte> group in emptied.GroupBy(e => e.Container, e => e.Slot))
        {
            InventoryType container = group.Key;
            var slots = group.ToList();
            await context.CharacterInventory
                .Where(r => r.CharacterId == owner && r.Container == container && slots.Contains(r.Slot))
                .ExecuteDeleteAsync(token);
        }
    }

    private static async Task DeleteIgnoresAsync(CharacterDbContext context, CharacterId owner, CharacterIgnoreWrite ignores,
        CancellationToken token)
    {
        if (ignores.Rewrite.Count == 0)
            return;

        var rewrite = ignores.Rewrite.ToList();
        await context.CharacterIgnores
            .Where(i => i.CharacterId == owner && rewrite.Contains(i.IgnoredCharacterId))
            .ExecuteDeleteAsync(token);
    }

    /// <summary>
    /// Each entry is inserted only while the ignored character exists, in one statement, so a character deleted since
    /// it was ignored, or by a delete running alongside this save, skips its row instead of failing the whole save on
    /// the foreign key. On Postgres the existence read takes <c>FOR KEY SHARE</c> on the character: a delete that
    /// committed first is seen and the row skipped, and one that has not waits for this save and then cascades the
    /// row. SQLite (the tests) runs one writer at a time, so the plain read is enough there.
    /// </summary>
    private static async Task InsertIgnoresAsync(CharacterDbContext context, CharacterIgnoreWrite ignores,
        CancellationToken token)
    {
        bool postgres = context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
        foreach (CharacterIgnore ignore in ignores.Insert)
        {
            long owner = ignore.CharacterId.Value;
            long ignored = ignore.IgnoredCharacterId.Value;
            DateTime createdAt = ignore.CreatedAt;
            if (postgres)
            {
                await context.Database.ExecuteSqlAsync($"""
                    INSERT INTO "CharacterIgnores" ("CharacterId", "IgnoredCharacterId", "CreatedAt")
                    SELECT {owner}, c."Id", {createdAt} FROM "Characters" AS c WHERE c."Id" = {ignored}
                    FOR KEY SHARE
                    """, token);
            }
            else
            {
                await context.Database.ExecuteSqlAsync($"""
                    INSERT INTO "CharacterIgnores" ("CharacterId", "IgnoredCharacterId", "CreatedAt")
                    SELECT {owner}, c."Id", {createdAt} FROM "Characters" AS c WHERE c."Id" = {ignored}
                    """, token);
            }
        }
    }
}
