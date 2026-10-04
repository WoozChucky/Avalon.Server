namespace Avalon.World.Persistence;

/// <summary>Starts persistence outside the simulation tick. Tests can drive completed reads in deterministic steps.</summary>
public interface IWorldDatabaseWork
{
    Task<T> Run<T>(Func<Task<T>> operation);
}
public sealed class WorldDatabaseWork : IWorldDatabaseWork
{
    public static readonly IWorldDatabaseWork ThreadPool = new WorldDatabaseWork();
    public Task<T> Run<T>(Func<Task<T>> operation) => Task.Run(operation, CancellationToken.None);
}
