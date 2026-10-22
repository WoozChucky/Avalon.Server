using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Persistence;

public interface ICharacterSaver
{
    /// <summary>
    /// Tick thread. Snapshots now, writes on the thread pool behind this character's previous save,
    /// and acknowledges the saved states on the tick thread once committed. The task is true when
    /// the commit landed, and false when the save failed and every state was kept for the next save.
    /// It never faults. While the character is held by a trade (<see cref="SaveTrade" />), it takes
    /// no snapshot: the save is snapshotted and queued at <see cref="ReleaseHold" />, and the task
    /// completes with that save's result.
    /// </summary>
    Task<bool> Save(IWorldConnection connection, CharacterEntity character);

    /// <summary>
    /// Tick thread. Several characters in one transaction, for exchanges that must not half-happen.
    /// Each character may appear once; a second entry for the same character throws
    /// <see cref="ArgumentException" /> before anything is queued. With more than one character, one
    /// held by a trade throws <see cref="InvalidOperationException" />; a single held character is
    /// held as <see cref="Save(IWorldConnection, CharacterEntity)" /> holds it.
    /// </summary>
    Task<bool> Save(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters);

    /// <summary>
    /// Despawn, on the tick, after the character has left its instance. Snapshots and joins the
    /// character's chain before it returns, waits behind any save in flight, and acknowledges
    /// nothing, because the entity is about to be discarded. While the character is held by a trade,
    /// it takes no snapshot: the save is snapshotted and queued at <see cref="ReleaseHold" />, after the
    /// trade has applied its outcome to the entity, and the task completes with that save's result.
    /// </summary>
    /// <param name="character">The despawning character. Read only on the tick: here, or at the release of a hold.</param>
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
    /// Tick thread. Save first, then swap (#1175): the paired save of a completed trade. Each character is
    /// snapshotted now with its side of the trade applied (<see cref="TradeSaveBatch" />), and both are written with
    /// <paramref name="record" /> in one transaction, behind every earlier save of either character. Memory does not
    /// change: the trade applies the swap once it knows the outcome. Marks are acknowledged on a commit only.
    /// </summary>
    /// <remarks>
    /// <para>The task never faults: <see cref="SaveOutcome.Committed" />, <see cref="SaveOutcome.Refused" /> (nothing
    /// was written), or <see cref="SaveOutcome.Unknown" /> (the commit may have landed; <see cref="TradeCommittedAsync" />
    /// settles it).</para>
    /// <para>The hold: before it returns, each character is held, whatever the outcome, until <see cref="ReleaseHold" />.
    /// While held, its <see cref="Save(IWorldConnection, CharacterEntity)" /> and <see cref="SaveOnDespawnAsync" /> take
    /// no snapshot and wait, in the order asked for, to be snapshotted at the release, once the trade has applied its
    /// outcome to memory: a snapshot taken before the swap would write the pre-trade row back whole, wherever it sat in
    /// the chain. <see cref="WhenIdle" /> of a held character completes only after the release and after every save the
    /// hold kept waiting has finished, and <see cref="WhenAllIdle" /> waits for every hold.</para>
    /// <para>Every exception is thrown before the write is queued: a throw means nothing was written and nothing is held.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">A character appears twice, or one item under two characters.</exception>
    /// <exception cref="InvalidOperationException">A character is already held, or cannot be snapshotted.</exception>
    /// <exception cref="OverflowException">A side gives more gold than its character holds.</exception>
    Task<SaveOutcome> SaveTrade(
        IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character, TradeSidePlan Side)> sides,
        TradeRecord record);

    /// <summary>
    /// Whether the trade <paramref name="tradeId" /> committed, from its record, read on the thread pool and not chained
    /// behind any save. Under a bounded deadline (<see cref="CharacterSaver.TradeLookupDeadline" />). Faults when the
    /// lookup fails or runs out of time, never guesses; the caller retries.
    /// </summary>
    Task<bool> TradeCommittedAsync(Guid tradeId, AccountId first, AccountId second);

    /// <summary>
    /// Tick thread (or, at shutdown, once the tick has stopped). Ends the trade's hold on <paramref name="id" />: every
    /// save the hold kept waiting is now snapshotted and queued, in the order it was asked for. Nothing when not held.
    /// </summary>
    void ReleaseHold(CharacterId id);

    /// <summary>
    /// Completes once every save queued so far for <paramref name="id" /> has finished, committed or
    /// failed, and, while a trade holds the character, once the hold is released and every save it
    /// kept waiting has finished. Already complete when nothing is queued or held. Never faults. The
    /// character select waits on it, so a relog reads the database only after the previous session's
    /// saves are done.
    /// </summary>
    Task WhenIdle(CharacterId id);

    /// <summary>
    /// Completes once every save queued so far, for any character, has finished, committed or
    /// failed, and every hold has been released and drained. Never faults. Shutdown waits on it: a
    /// despawn is started fire-and-forget by the tick, so one begun on an earlier tick may still be
    /// writing when the host stops.
    /// </summary>
    Task WhenAllIdle();
}

/// <summary>
/// The one path a character's inventory, gold and row reach the database. Saves are chained per
/// <see cref="CharacterId" />, not per entity: a relog builds a new entity for the same character,
/// and its saves must still queue behind the old entity's despawn save.
/// </summary>
/// <param name="meter">Where the save duration is recorded; the world server's meter when omitted.</param>
/// <param name="clock">Times the trade record lookup's deadline; the system clock when omitted.</param>
public sealed class CharacterSaver(
    ICharacterSaveRepository repository,
    ILogger<CharacterSaver> logger,
    Meter? meter = null,
    TimeProvider? clock = null) : ICharacterSaver
{
    /// <summary>
    /// The longest one trade record lookup may take. The lookup waits on both accounts' fence locks, and a paired write
    /// whose connection stays idle in its transaction can hold them indefinitely; at the deadline the lookup faults, and
    /// the trade retries it with backoff. A commit holds them for milliseconds, so this is generous.
    /// </summary>
    public static readonly TimeSpan TradeLookupDeadline = TimeSpan.FromSeconds(5);

    private static readonly double[] s_saveBuckets = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000];

    private static readonly KeyValuePair<string, object?> s_committed = new("outcome", "committed");
    private static readonly KeyValuePair<string, object?> s_failed = new("outcome", "failed");
    private static readonly KeyValuePair<string, object?> s_periodic = new("kind", "periodic");
    private static readonly KeyValuePair<string, object?> s_despawn = new("kind", "despawn");

    /// <summary>
    /// One save batch, from the moment it stops waiting behind the character's previous save until
    /// it committed or failed: the despawn row preparation and the repository write, not the queue.
    /// </summary>
    private readonly Histogram<double> _duration = (meter ?? DiagnosticsConfig.World.Meter).CreateHistogram<double>(
        "world.character.save.duration", "ms", "Time one save batch took to write, by outcome and kind", tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = s_saveBuckets });

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private readonly Lock _gate = new();

    /// <summary>The latest save queued for each character with one still unfinished. Guarded by <see cref="_gate" />.</summary>
    private readonly Dictionary<CharacterId, Task> _latest = [];

    /// <summary>
    /// The characters a trade holds (#1175). Changed on the tick only (or at shutdown, after it stopped), and read by
    /// <see cref="WhenIdle" /> and <see cref="WhenAllIdle" /> from any thread. Guarded by <see cref="_gate" />.
    /// </summary>
    private readonly Dictionary<CharacterId, SaveHold> _holds = [];

    public Task<bool> Save(IWorldConnection connection, CharacterEntity character) =>
        Save([(connection, character)]);

    public Task<bool> Save(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters)
    {
        if (characters.Count == 1)
        {
            if (TryDefer(characters[0].Character, characters, SaveNow, out Task<bool>? held))
                return held;
        }
        else
        {
            foreach ((_, CharacterEntity character) in characters)
                RefuseHeld(character);
        }

        return SaveNow(characters);
    }

    public Task<bool> SaveOnDespawnAsync(
        CharacterEntity character,
        Func<Character, CancellationToken, Task>? prepareRow,
        CancellationToken cancellationToken) =>
        TryDefer(character, (character, prepareRow, cancellationToken), DespawnNow, out Task<bool>? held)
            ? held
            : DespawnNow((character, prepareRow, cancellationToken));

    public Task<SaveOutcome> SaveTrade(
        IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character, TradeSidePlan Side)> sides,
        TradeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        foreach ((_, CharacterEntity character, _) in sides)
            RefuseHeld(character);

        var snapshots = new CharacterSaveSnapshot[sides.Count];
        for (int i = 0; i < sides.Count; i++)
        {
            (IWorldConnection connection, CharacterEntity character, TradeSidePlan side) = sides[i];
            var snapshot = CharacterSaveSnapshot.Take(character, connection.GameplayAuthority);
            DateTime now = character.Clock.GetUtcNow().UtcDateTime;
            snapshots[i] = snapshot with { Batch = TradeSaveBatch.Apply(snapshot.Batch, side, now) };
        }

        EnsureOneBatchPerCharacter(snapshots);

        Task<SaveOutcome> write = Enqueue(snapshots, after => WriteAfterAsync(after, snapshots, prepareRow: null, record, despawn: false, CancellationToken.None));

        // On the same tick call as the enqueue, before anything else can ask for a save of either character.
        lock (_gate)
        {
            foreach (CharacterSaveSnapshot snapshot in snapshots)
                _holds.Add(snapshot.CharacterId, new SaveHold());
        }

        Task<bool> committed = IsCommittedAsync(write);
        for (int i = 0; i < sides.Count; i++)
            Acknowledge(sides[i].Connection, sides[i].Character, snapshots[i].Marks, committed);

        return write;
    }

    public Task<bool> TradeCommittedAsync(Guid tradeId, AccountId first, AccountId second) =>
        Task.Run(() => LookUpTradeAsync(tradeId, first, second), CancellationToken.None);

    public void ReleaseHold(CharacterId id)
    {
        SaveHold? hold;
        lock (_gate)
        {
            if (!_holds.TryGetValue(id, out hold))
                return;
        }

        // The hold stays listed until every save it kept waiting is in the chain, so a WhenIdle or WhenAllIdle taken
        // meanwhile still answers the hold's Drained and cannot miss one of them. Read by index under the lock: only
        // the tick adds requests, but nothing here relies on that.
        Task tail;
        for (int i = 0; ; i++)
        {
            DeferredSave request;
            lock (_gate)
            {
                if (i == hold.Deferred.Count)
                {
                    _holds.Remove(id);
                    tail = _latest.GetValueOrDefault(id) ?? Task.CompletedTask;
                    break;
                }

                request = hold.Deferred[i];
            }

            Start(id, request);
        }

        // The tail of the chain once every held save is in it: everything before it, the paired save included,
        // finishes first.
        _ = tail.ContinueWith(
            _ => hold.Drained.TrySetResult(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public Task WhenIdle(CharacterId id)
    {
        lock (_gate)
        {
            if (_holds.TryGetValue(id, out SaveHold? hold))
                return hold.Drained.Task;

            return _latest.GetValueOrDefault(id) ?? Task.CompletedTask;
        }
    }

    public Task WhenAllIdle()
    {
        Task[] pending;
        lock (_gate)
            pending = [.. _latest.Values, .. _holds.Values.Select(h => h.Drained.Task)];

        // Only the tail of each chain: every earlier save of that character finishes before it. A
        // multi-character save is the tail of several chains, so it may be listed more than once.
        return pending.Length == 0 ? Task.CompletedTask : Task.WhenAll(pending);
    }

    private Task<bool> SaveNow(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters)
    {
        var snapshots = new CharacterSaveSnapshot[characters.Count];
        for (int i = 0; i < characters.Count; i++)
            snapshots[i] = CharacterSaveSnapshot.Take(characters[i].Character, characters[i].Connection.GameplayAuthority);

        EnsureOneBatchPerCharacter(snapshots);

        Task<bool> write = Enqueue(snapshots, after => CommittedAfterAsync(after, snapshots, prepareRow: null, despawn: false, CancellationToken.None));

        for (int i = 0; i < characters.Count; i++)
            Acknowledge(characters[i].Connection, characters[i].Character, snapshots[i].Marks, write);

        return write;
    }

    private Task<bool> DespawnNow(
        (CharacterEntity Character, Func<Character, CancellationToken, Task>? PrepareRow, CancellationToken Token) despawn)
    {
        CharacterSaveSnapshot[] snapshots = [CharacterSaveSnapshot.Take(despawn.Character)];
        return Enqueue(snapshots, after => CommittedAfterAsync(after, snapshots, despawn.PrepareRow, despawn: true, despawn.Token));
    }

    private static async Task<bool> IsCommittedAsync(Task<SaveOutcome> write) =>
        await write.ConfigureAwait(false) == SaveOutcome.Committed;

    private static void Acknowledge(IWorldConnection connection, CharacterEntity character, SaveMarks marks, Task<bool> write) =>
        connection.EnqueueContinuation(write, committed =>
        {
            if (committed)
                character.SaveState.Acknowledge(marks);
        });

    /// <summary>
    /// When a trade holds <paramref name="character" />, queues the save on its hold and gives the task that will carry
    /// its result; otherwise false. <paramref name="start" /> is called at the release with <paramref name="state" />.
    /// </summary>
    private bool TryDefer<TState>(
        CharacterEntity character, TState state, Func<TState, Task<bool>> start, [NotNullWhen(true)] out Task<bool>? held)
    {
        held = null;
        if (character.Data is not { } row)
            return false;   // the snapshot refuses it

        lock (_gate)
        {
            if (!_holds.TryGetValue(row.Id, out SaveHold? hold))
                return false;

            var request = DeferredSave.Of(start, state);
            hold.Deferred.Add(request);
            held = request.Result.Task;
            return true;
        }
    }

    private void RefuseHeld(CharacterEntity character)
    {
        if (character.Data is not { } row)
            return;   // the snapshot refuses it

        lock (_gate)
        {
            if (_holds.ContainsKey(row.Id))
                throw new InvalidOperationException($"Character {row.Id.Value} is held by a trade's paired save.");
        }
    }

    /// <summary>
    /// Snapshots and queues one held save, and hands its result to the task its caller has. A snapshot that throws
    /// (a connection replaced meanwhile) would have thrown to the caller; it is logged, and the save is false.
    /// </summary>
    private void Start(CharacterId id, DeferredSave request)
    {
        Task<bool> save;
        try
        {
            save = request.Start();
        }
        catch (Exception e)
        {
            logger.LogError(e, "A save of character {CharacterId} held by a trade could not be queued at its release", id.Value);
            request.Result.TrySetResult(false);
            return;
        }

        _ = CompleteAsync(save, request.Result);
    }

    /// <summary>The held caller's task completes with the real save's result. A save never faults.</summary>
    private static async Task CompleteAsync(Task<bool> save, TaskCompletionSource<bool> result) =>
        result.TrySetResult(await save.ConfigureAwait(false));

    private async Task<bool> LookUpTradeAsync(Guid tradeId, AccountId first, AccountId second)
    {
        using var deadline = new CancellationTokenSource(TradeLookupDeadline, _clock);
        try
        {
            return await repository.TradeRecordExistsAsync(tradeId, first, second, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (deadline.IsCancellationRequested)
        {
            // A fault, not a cancellation: the caller retries a lookup that failed, whatever made it fail.
            throw new TimeoutException($"The record lookup of trade {tradeId} ran past {TradeLookupDeadline.TotalSeconds} s.", e);
        }
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
            {
                throw new ArgumentException(
                    $"Character {snapshot.CharacterId.Value} appears more than once in one save.", nameof(snapshots));
            }
        }

        HashSet<ItemInstanceId> items = [];
        foreach (CharacterSaveSnapshot snapshot in snapshots)
        {
            foreach (ItemInstance item in snapshot.Batch.UpsertItems)
            {
                if (!items.Add(item.Id))
                {
                    throw new ArgumentException(
                        $"Item {item.Id.Value} is written by more than one character in one save.", nameof(snapshots));
                }
            }
        }
    }

    /// <summary>
    /// Chains the write behind the latest save of every character it carries, and makes it the
    /// latest for each of them. Choosing what to wait for and publishing the new tail happen under
    /// one lock, so two saves of one character can never both see the same predecessor.
    /// </summary>
    private Task<T> Enqueue<T>(CharacterSaveSnapshot[] snapshots, Func<Task, Task<T>> writeAfter)
    {
        Task<T> write;
        lock (_gate)
        {
            List<Task> previous = [];
            foreach (CharacterSaveSnapshot snapshot in snapshots)
            {
                if (_latest.TryGetValue(snapshot.CharacterId, out Task? latest))
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
            write = Task.Run(() => writeAfter(after), CancellationToken.None);

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
    private void Forget(CharacterSaveSnapshot[] snapshots, Task finished)
    {
        lock (_gate)
        {
            foreach (CharacterSaveSnapshot snapshot in snapshots)
            {
                if (_latest.TryGetValue(snapshot.CharacterId, out Task? latest) && ReferenceEquals(latest, finished))
                    _latest.Remove(snapshot.CharacterId);
            }
        }
    }

    /// <summary>A save that is not a trade: anything but a commit is false, an unknown commit included.</summary>
    private async Task<bool> CommittedAfterAsync(
        Task previous,
        CharacterSaveSnapshot[] snapshots,
        Func<Character, CancellationToken, Task>? prepareRow,
        bool despawn,
        CancellationToken cancellationToken) =>
        await WriteAfterAsync(previous, snapshots, prepareRow, record: null, despawn, cancellationToken).ConfigureAwait(false)
            == SaveOutcome.Committed;

    private async Task<SaveOutcome> WriteAfterAsync(
        Task previous,
        CharacterSaveSnapshot[] snapshots,
        Func<Character, CancellationToken, Task>? prepareRow,
        TradeRecord? record,
        bool despawn,
        CancellationToken cancellationToken)
    {
        // Only orders: a save never faults, it comes back false.
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Timed from here, so the wait behind the previous save is not counted.
        long started = Stopwatch.GetTimestamp();
        try
        {
            // The copies only: the snapshot owns them, and the live row stays with the tick.
            if (prepareRow is not null)
            {
                foreach (CharacterSaveSnapshot snapshot in snapshots)
                    await prepareRow(snapshot.Batch.Row, cancellationToken).ConfigureAwait(false);
            }

            var batches = snapshots.Select(s => s.Batch).ToList();
            if (record is null)
                await repository.WriteAsync(batches, cancellationToken).ConfigureAwait(false);
            else
                await repository.WriteTradeAsync(batches, record, cancellationToken).ConfigureAwait(false);

            Record(started, committed: true, despawn);
            return SaveOutcome.Committed;
        }
        catch (SaveOutcomeUnknownException e) when (record is not null)
        {
            Record(started, committed: false, despawn);
            logger.LogWarning(e,
                "The paired save of trade {TradeId} for characters {CharacterIds} may or may not have committed; its record settles it",
                record.Id, string.Join(", ", snapshots.Select(s => s.CharacterId.Value)));
            return SaveOutcome.Unknown;
        }
        catch (Exception e)
        {
            Record(started, committed: false, despawn);
            logger.LogError(e,
                "Saving character(s) {CharacterIds} failed; their changes stay pending and the next save retries them",
                string.Join(", ", snapshots.Select(s => s.CharacterId.Value)));
            return SaveOutcome.Refused;
        }
    }

    private void Record(long started, bool committed, bool despawn) =>
        _duration.Record(
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            committed ? s_committed : s_failed,
            despawn ? s_despawn : s_periodic);

    /// <summary>A save asked for while its character was held, and the task its caller holds.</summary>
    private sealed record DeferredSave(Func<Task<bool>> Start, TaskCompletionSource<bool> Result)
    {
        /// <summary>Its own method, so a save that is not held never builds the closure.</summary>
        public static DeferredSave Of<TState>(Func<TState, Task<bool>> start, TState state) =>
            new(() => start(state), new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <summary>
    /// A trade's hold on one character: the saves it keeps waiting, in the order asked for, and <see cref="Drained" />,
    /// which completes once the release has queued them and every one has finished.
    /// </summary>
    private sealed class SaveHold
    {
        public List<DeferredSave> Deferred { get; } = [];

        public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
