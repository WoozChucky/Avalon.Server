using Avalon.Common.Telemetry;

namespace Avalon.World.Persistence;

/// <summary>Starts bounded work outside the simulation tick. Saturation refuses admission rather than growing a backlog.</summary>
public interface IWorldDatabaseWork
{
    Task<T> Run<T>(Func<Task<T>> operation);
}
/// <summary>
/// A work queue refused an operation: it was full. One instance is shared by every refusal of every queue (#1050), and
/// every caller that awaits a refusal rethrows it, so its stack trace carries no information: it names whichever caller
/// rethrew it last, possibly at the same moment as another. Never log it with its stack; its type is the whole message.
/// </summary>
public sealed class WorldWorkUnavailableException : Exception;
public sealed class WorldDatabaseWork : IWorldDatabaseWork
{
    public const int DatabaseConcurrency = 64;
    public const int DatabaseOutstandingLimit = 256;
    public const int AdmissionConcurrency = 32;
    public const int AdmissionOutstandingLimit = 128;

    /// <summary>
    /// Lease heartbeats sent at once (#1050). Each player sends one every 15 s
    /// (<c>GameAuthPolicy.HeartbeatInterval</c>), so a world of 5,000 players sends about 333 a second, and 128 at once
    /// keep up with that while one takes up to about 380 ms (128 / 333 s); the lease lasts 45 s, three tries.
    /// </summary>
    public const int HeartbeatConcurrency = 128;

    /// <summary>
    /// Heartbeats running or waiting (#1050). A connection has at most one in flight, and only an admitted one has any,
    /// so this bounds nothing a client can grow: it is set above any player count a world carries.
    /// </summary>
    public const int HeartbeatOutstandingLimit = 8192;

    public static readonly IWorldDatabaseWork ThreadPool = new WorldDatabaseWork(lane: "database");
    public static readonly IWorldDatabaseWork Admission = new WorldDatabaseWork(AdmissionConcurrency, AdmissionOutstandingLimit, "admission");

    /// <summary>
    /// Live players' lease heartbeats, in a lane of their own (#1050): admissions, which any client holding a
    /// well-formed ticket can send, can fill <see cref="Admission" /> (a reconnect storm does), and a heartbeat refused
    /// there waits 15 s for its next try, so three refusals in a row ended a lease the player still held.
    /// </summary>
    public static readonly IWorldDatabaseWork Heartbeat = new WorldDatabaseWork(HeartbeatConcurrency, HeartbeatOutstandingLimit, "heartbeat");

    private static readonly WorldWorkUnavailableException s_unavailable = new();
    private readonly SemaphoreSlim _slots;
    private readonly int _maximumOutstanding;
    private int _outstanding;

    // The lane's tag on world.work.refusals, made once: a refusal allocates nothing.
    private readonly KeyValuePair<string, object?> _laneTag;

    /// <param name="lane">
    /// The lane's name on <c>world.work.refusals</c> (#1050): one of a fixed set, never anything a client sends.
    /// </param>
    public WorldDatabaseWork(int maximumConcurrency = DatabaseConcurrency, int maximumOutstanding = DatabaseOutstandingLimit,
        string lane = "database")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrency);
        if (maximumOutstanding < maximumConcurrency) throw new ArgumentOutOfRangeException(nameof(maximumOutstanding));
        _slots = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        _maximumOutstanding = maximumOutstanding;
        _laneTag = new KeyValuePair<string, object?>("lane", lane);
    }

    /// <summary>
    /// Whether <paramref name="task" /> is a refusal of <see cref="Run{T}" />: the one task, faulted with
    /// <see cref="WorldWorkUnavailableException" />, every saturated queue hands back for a <typeparamref name="T" />.
    /// A reference comparison: a caller that answers a refusal at once reads no exception for it.
    /// </summary>
    public static bool IsRefusal<T>(Task<T> task) => ReferenceEquals(task, Refused<T>.Task);

    public Task<T> Run<T>(Func<Task<T>> operation)
    {
        if (Interlocked.Increment(ref _outstanding) > _maximumOutstanding)
        {
            Interlocked.Decrement(ref _outstanding);
            // Counted here, where it is refused, for every lane alike (#1050).
            DiagnosticsConfig.World.WorkRefusals.Add(1, _laneTag);
            return Refused<T>.Task;
        }
        return Task.Run(async () =>
        {
            await _slots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try { return await operation().ConfigureAwait(false); }
            finally { _slots.Release(); Interlocked.Decrement(ref _outstanding); }
        }, CancellationToken.None);
    }

    /// <summary>
    /// The refusal, made once per result type: a reconnect storm is refused hundreds of times a second, and each refusal
    /// made its own exception and task.
    /// </summary>
    private static class Refused<T>
    {
        public static readonly Task<T> Task = System.Threading.Tasks.Task.FromException<T>(s_unavailable);
    }
}
