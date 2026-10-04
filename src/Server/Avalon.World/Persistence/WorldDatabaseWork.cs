namespace Avalon.World.Persistence;

/// <summary>Starts bounded work outside the simulation tick. Saturation refuses admission rather than growing a backlog.</summary>
public interface IWorldDatabaseWork
{
    Task<T> Run<T>(Func<Task<T>> operation);
}
public sealed class WorldWorkUnavailableException : Exception;
public sealed class WorldDatabaseWork : IWorldDatabaseWork
{
    public const int DatabaseConcurrency = 64;
    public const int DatabaseOutstandingLimit = 256;
    public const int AdmissionConcurrency = 32;
    public const int AdmissionOutstandingLimit = 128;
    public static readonly IWorldDatabaseWork ThreadPool = new WorldDatabaseWork();
    public static readonly IWorldDatabaseWork Admission = new WorldDatabaseWork(AdmissionConcurrency, AdmissionOutstandingLimit);
    private readonly SemaphoreSlim _slots;
    private readonly int _maximumOutstanding;
    private int _outstanding;
    public WorldDatabaseWork(int maximumConcurrency = DatabaseConcurrency, int maximumOutstanding = DatabaseOutstandingLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrency);
        if (maximumOutstanding < maximumConcurrency) throw new ArgumentOutOfRangeException(nameof(maximumOutstanding));
        _slots = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        _maximumOutstanding = maximumOutstanding;
    }
    public Task<T> Run<T>(Func<Task<T>> operation)
    {
        if (Interlocked.Increment(ref _outstanding) > _maximumOutstanding)
        {
            Interlocked.Decrement(ref _outstanding);
            return Task.FromException<T>(new WorldWorkUnavailableException());
        }
        return Task.Run(async () =>
        {
            await _slots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try { return await operation().ConfigureAwait(false); }
            finally { _slots.Release(); Interlocked.Decrement(ref _outstanding); }
        }, CancellationToken.None);
    }
}
