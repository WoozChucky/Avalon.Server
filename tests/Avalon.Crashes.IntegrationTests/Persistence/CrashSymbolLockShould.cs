using Avalon.Database.Crashes;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Persistence;

[Trait("Category", "CrashesPostgres")]
public sealed class CrashSymbolLockShould(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [IntegrationFact]
    public async Task Should_serialize_same_identity_release_on_disposal_and_preserve_cancellation()
    {
        var first = new CrashSymbolLock(fixture.Factory);
        var second = new CrashSymbolLock(fixture.Factory);
        await using IAsyncDisposable held = await first.AcquireAsync("v1", "module-identity", default);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.AcquireAsync("v1", "module-identity", cancellation.Token));
        await using IAsyncDisposable different = await second.AcquireAsync("v2", "module-identity", default).WaitAsync(TimeSpan.FromSeconds(3));
        Task<IAsyncDisposable> pending = second.AcquireAsync("v1", "module-identity", default);
        await Task.Delay(100);
        Assert.False(pending.IsCompleted);
        await held.DisposeAsync();
        await using IAsyncDisposable acquired = await pending.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
