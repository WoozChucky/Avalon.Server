using Avalon.World.Persistence;

namespace Avalon.Server.World.UnitTests.GameAuth;

internal sealed class InlineDatabaseWork : IWorldDatabaseWork
{
    public static readonly InlineDatabaseWork Instance = new();
    public Task<T> Run<T>(Func<Task<T>> operation) => operation();
}
