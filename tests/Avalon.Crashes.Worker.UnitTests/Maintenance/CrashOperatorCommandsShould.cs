using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Maintenance;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Maintenance;

public sealed class CrashOperatorCommandsShould
{
    [Fact]
    public async Task Should_run_cleanup_without_bucket_credentials_and_drain_all_batches()
    {
        var clock = new FakeTimeProvider();
        var store = new Store();
        var command = new CrashOperatorCommands(store, new CrashRetentionService(store, clock), clock);
        using var output = new StringWriter();
        Assert.Equal(0, await command.ExecuteAsync(["cleanup"], output, default));
        Assert.Equal(3, store.Deletes);
        Assert.Contains("501", output.ToString());
        Assert.Equal(2, await command.ExecuteAsync(["cleanup", "--secret", "sentinel"], output, default));
        Assert.DoesNotContain("sentinel", output.ToString());
    }

    private sealed class Store : ICrashMaintenanceStore
    {
        public int Deletes { get; private set; }
        public Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct)
        {
            Assert.Equal(500, batchSize);
            return Task.FromResult(++Deletes switch { 1 => 500, 2 => 1, _ => 0 });
        }
        public Task<FailurePage> ListFailuresAsync(int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<RetryResult> RetryFailedAsync(CrashReportId id, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
