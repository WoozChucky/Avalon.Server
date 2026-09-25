using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Persistence;

public interface ICharacterSaver
{
    /// <summary>
    /// Tick thread. Snapshots now, writes on the thread pool behind this character's previous save,
    /// and acknowledges the saved states on the tick thread once committed. The task is true when
    /// the commit landed, and false when the save failed and every state was kept for the next save.
    /// It never faults.
    /// </summary>
    Task<bool> Save(IWorldConnection connection, CharacterEntity character);

    /// <summary>
    /// Tick thread. Several characters in one transaction, for exchanges that must not half-happen
    /// (auction house, trade, mail). Nothing calls it yet. Each character may appear once; a second
    /// entry for the same character throws <see cref="ArgumentException" /> before anything is queued.
    /// </summary>
    Task<bool> Save(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters);

    /// <summary>
    /// Despawn, on the tick, after the character has left its instance. Snapshots and joins the
    /// character's chain before it returns, waits behind any save in flight, and acknowledges
    /// nothing, because the entity is about to be discarded.
    /// </summary>
    /// <param name="character">The despawning character. Read only here, on the calling thread.</param>
    /// <param name="prepareRow">
    /// Optional last changes to the <em>copied</em> row, made inside the chained write on the thread
    /// pool just before it is written: the dead-logout move to the respawn town needs a database
    /// lookup, and the tick must not wait for it. It never sees the entity; a throw fails the save.
    /// </param>
    /// <param name="cancellationToken">Passed to the row preparation and the write.</param>
    Task<bool> SaveOnDespawnAsync(
        CharacterEntity character,
        Func<Character, CancellationToken, Task>? prepareRow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Completes once every save queued so far for <paramref name="id" /> has finished, committed or
    /// failed. Already complete when nothing is queued. Never faults. The character select waits on
    /// it, so a relog reads the database only after the previous session's saves are done.
    /// </summary>
    Task WhenIdle(CharacterId id);
}

/// <summary>
/// The one path a character's inventory, money and row reach the database. Saves are chained per
/// <see cref="CharacterId" />, not per entity: a relog builds a new entity for the same character,
/// and its saves must still queue behind the old entity's despawn save.
/// </summary>
public sealed class CharacterSaver(ICharacterSaveRepository repository, ILogger<CharacterSaver> logger) : ICharacterSaver
{
    private readonly Lock _gate = new();

    /// <summary>The latest save queued for each character with one still unfinished. Guarded by <see cref="_gate" />.</summary>
    private readonly Dictionary<CharacterId, Task<bool>> _latest = [];

    public Task<bool> Save(IWorldConnection connection, CharacterEntity character) =>
        Save([(connection, character)]);

    public Task<bool> Save(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters)
    {
        var snapshots = new CharacterSaveSnapshot[characters.Count];
        for (int i = 0; i < characters.Count; i++)
            snapshots[i] = CharacterSaveSnapshot.Take(characters[i].Character);

        EnsureOneBatchPerCharacter(snapshots);

        Task<bool> write = Enqueue(snapshots, prepareRow: null, CancellationToken.None);

        for (int i = 0; i < characters.Count; i++)
        {
            (IWorldConnection connection, CharacterEntity character) = characters[i];
            SaveMarks marks = snapshots[i].Marks;
            connection.EnqueueContinuation(write, committed =>
            {
                if (committed)
                    character.SaveState.Acknowledge(marks);
            });
        }

        return write;
    }

    public Task<bool> SaveOnDespawnAsync(
        CharacterEntity character,
        Func<Character, CancellationToken, Task>? prepareRow,
        CancellationToken cancellationToken) =>
        Enqueue([CharacterSaveSnapshot.Take(character)], prepareRow, cancellationToken);

    public Task WhenIdle(CharacterId id)
    {
        lock (_gate)
            return _latest.TryGetValue(id, out Task<bool>? latest) ? latest : Task.CompletedTask;
    }

    /// <summary>
    /// The repository's contract: at most one batch per character in a call, and each item id and
    /// slot key at most once among its upserts. A batch is built from its own marks, so it never
    /// repeats a key; two batches can only collide if they are for one character, or if one item is
    /// held by two characters at once.
    /// </summary>
    private static void EnsureOneBatchPerCharacter(CharacterSaveSnapshot[] snapshots)
    {
        if (snapshots.Length < 2)
            return;

        HashSet<CharacterId> characters = [];
        foreach (CharacterSaveSnapshot snapshot in snapshots)
        {
            if (!characters.Add(snapshot.CharacterId))
                throw new ArgumentException(
                    $"Character {snapshot.CharacterId.Value} appears more than once in one save.", nameof(snapshots));
        }

        HashSet<ItemInstanceId> items = [];
        foreach (CharacterSaveSnapshot snapshot in snapshots)
        {
            foreach (var item in snapshot.Batch.UpsertItems)
            {
                if (!items.Add(item.Id))
                    throw new ArgumentException(
                        $"Item {item.Id.Value} is written by more than one character in one save.", nameof(snapshots));
            }
        }
    }

    /// <summary>
    /// Chains the write behind the latest save of every character it carries, and makes it the
    /// latest for each of them. Choosing what to wait for and publishing the new tail happen under
    /// one lock, so two saves of one character can never both see the same predecessor.
    /// </summary>
    private Task<bool> Enqueue(
        CharacterSaveSnapshot[] snapshots,
        Func<Character, CancellationToken, Task>? prepareRow,
        CancellationToken cancellationToken)
    {
        Task<bool> write;
        lock (_gate)
        {
            List<Task> previous = [];
            foreach (CharacterSaveSnapshot snapshot in snapshots)
            {
                if (_latest.TryGetValue(snapshot.CharacterId, out Task<bool>? latest))
                    previous.Add(latest);
            }

            Task after = previous.Count switch
            {
                0 => Task.CompletedTask,
                1 => previous[0],
                _ => Task.WhenAll(previous),
            };

            // Off the tick thread from the first line: the snapshot is all the tick owes a save. The
            // token goes to the write, not to Task.Run, so a cancelled save still comes back false
            // instead of cancelled, and whatever is queued behind it still runs.
            write = Task.Run(() => WriteAfterAsync(after, snapshots, prepareRow, cancellationToken), CancellationToken.None);

            foreach (CharacterSaveSnapshot snapshot in snapshots)
                _latest[snapshot.CharacterId] = write;
        }

        _ = write.ContinueWith(
            finished => Forget(snapshots, finished),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return write;
    }

    /// <summary>Drops a finished save from the chain, unless a later one has already replaced it.</summary>
    private void Forget(CharacterSaveSnapshot[] snapshots, Task<bool> finished)
    {
        lock (_gate)
        {
            foreach (CharacterSaveSnapshot snapshot in snapshots)
            {
                if (_latest.TryGetValue(snapshot.CharacterId, out Task<bool>? latest) && ReferenceEquals(latest, finished))
                    _latest.Remove(snapshot.CharacterId);
            }
        }
    }

    private async Task<bool> WriteAfterAsync(
        Task previous,
        CharacterSaveSnapshot[] snapshots,
        Func<Character, CancellationToken, Task>? prepareRow,
        CancellationToken cancellationToken)
    {
        // Only orders: a save never faults, it comes back false.
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        try
        {
            // The copies only: the snapshot owns them, and the live row stays with the tick.
            if (prepareRow is not null)
            {
                foreach (CharacterSaveSnapshot snapshot in snapshots)
                    await prepareRow(snapshot.Batch.Row, cancellationToken).ConfigureAwait(false);
            }

            await repository.WriteAsync(snapshots.Select(s => s.Batch).ToList(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e,
                "Saving character(s) {CharacterIds} failed; their changes stay pending and the next save retries them",
                string.Join(", ", snapshots.Select(s => s.CharacterId.Value)));
            return false;
        }
    }
}
