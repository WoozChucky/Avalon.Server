using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

/// <summary>
/// What one character's save writes: its whole row, and only the items and slots whose save state
/// was not Unchanged. Items and slots to upsert carry their current values; the rest are deleted.
/// <paramref name="Stats" />, when present, is the character's derived-stats row, upserted.
/// </summary>
public sealed record CharacterSaveBatch(
    Domain.Characters.Character Row,
    IReadOnlyList<ItemInstance> UpsertItems,
    IReadOnlyList<ItemInstanceId> DeleteItems,
    IReadOnlyList<CharacterInventory> UpsertSlots,
    IReadOnlyList<(InventoryType Container, ushort Slot)> DeleteSlots,
    CharacterStats? Stats = null);

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
    /// </remarks>
    Task WriteAsync(IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken = default);
}

public class CharacterSaveRepository(IDbTransactionRunner<CharacterDbContext> transactions) : ICharacterSaveRepository
{
    public Task WriteAsync(IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken = default) =>
        transactions.ExecuteAsync(async (context, token) =>
        {
            // Every delete, for every batch, before any upsert: a row one batch removes and another
            // adds (a trade) must end up present, whichever order the batches came in.
            foreach (CharacterSaveBatch batch in batches)
            {
                CharacterId owner = batch.Row.Id;
                foreach ((InventoryType container, ushort slot) in batch.DeleteSlots)
                {
                    await context.CharacterInventory
                        .Where(r => r.CharacterId == owner && r.Container == container && r.Slot == slot)
                        .ExecuteDeleteAsync(token);
                }
            }

            // After the slots, which reference items by foreign key.
            foreach (CharacterSaveBatch batch in batches)
            {
                foreach (ItemInstanceId id in batch.DeleteItems)
                {
                    await context.ItemInstances.Where(i => i.Id == id).ExecuteDeleteAsync(token);
                }
            }

            foreach (CharacterSaveBatch batch in batches)
            {
                context.TrackForUpdate(batch.Row);

                if (batch.UpsertItems.Count > 0)
                {
                    // One query per batch for which of its items already exist, not one per item.
                    List<ItemInstanceId> ids = batch.UpsertItems.Select(i => i.Id).ToList();
                    HashSet<ItemInstanceId> existingItems = (await context.ItemInstances
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
            }

            // One SaveChanges: EF orders the item inserts ahead of the slot inserts that reference them.
            await context.SaveChangesAsync(token);
        }, cancellationToken);
}
