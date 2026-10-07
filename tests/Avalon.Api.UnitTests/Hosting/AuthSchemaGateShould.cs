using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Worlds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using SqliteAuthDatabase = Avalon.Api.UnitTests.Services.SqliteAuthDatabase;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// The auth schema at startup (#794, design section 5.2): the service that owns it migrates it; a reader waits until
/// no migration its build knows of is pending, and fails, naming the setting, once its wait is spent.
/// </summary>
public sealed class AuthSchemaGateShould : IDisposable
{
    private readonly SqliteAuthDatabase _auth = new();
    private readonly FakeTimeProvider _time = new();
    private int _migrations;

    public void Dispose() => _auth.Dispose();

    private ApiDatabaseMigrator Migrator() => new(NullLogger<ApiDatabaseMigrator>.Instance,
        migrate: (_, _) =>
        {
            _migrations++;
            return Task.CompletedTask;
        });

    [Fact]
    public async Task Migrate_the_auth_database_as_its_owner()
    {
        var gate = new AuthSchemaGate(AuthSchemaRole.Owner, TimeSpan.FromSeconds(30), _time,
            pending: (_, _) => throw new InvalidOperationException("an owner does not wait"));

        await gate.PassAsync(_auth, Migrator(), CancellationToken.None);

        Assert.Equal(1, _migrations);
    }

    [Fact]
    public async Task Pass_a_reader_at_once_when_nothing_is_pending_and_never_migrate()
    {
        var gate = new AuthSchemaGate(AuthSchemaRole.Reader, TimeSpan.FromSeconds(30), _time,
            pending: (_, _) => Task.FromResult(false));

        await gate.PassAsync(_auth, Migrator(), CancellationToken.None);

        Assert.Equal(0, _migrations);
    }

    [Fact]
    public async Task Hold_a_reader_until_the_owners_migrations_are_applied()
    {
        int checks = 0;
        var gate = new AuthSchemaGate(AuthSchemaRole.Reader, TimeSpan.FromSeconds(30), _time,
            pending: (_, _) => Task.FromResult(++checks < 3));

        Task passing = gate.PassAsync(_auth, Migrator(), CancellationToken.None);
        await PollAsync(passing);

        await passing;
        Assert.Equal(3, checks);
        Assert.Equal(0, _migrations);
    }

    [Fact]
    public async Task Fail_a_reader_naming_the_setting_once_its_wait_is_spent()
    {
        var gate = new AuthSchemaGate(AuthSchemaRole.Reader, TimeSpan.FromSeconds(12), _time,
            pending: (_, _) => Task.FromResult(true));

        Task passing = gate.PassAsync(_auth, Migrator(), CancellationToken.None);
        await PollAsync(passing);

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => passing);
        Assert.Contains(AuthSchemaGate.WaitSetting, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData("45", 45)]
    public void Read_the_wait_in_seconds_with_a_default(string? value, int seconds)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [AuthSchemaGate.WaitSetting] = value })
            .Build();

        Assert.Equal(TimeSpan.FromSeconds(seconds), AuthSchemaGate.WaitFrom(configuration));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void Refuse_a_wait_that_is_not_a_whole_number_of_seconds_naming_the_setting(string value)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [AuthSchemaGate.WaitSetting] = value })
            .Build();

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => AuthSchemaGate.WaitFrom(configuration));
        Assert.Contains(AuthSchemaGate.WaitSetting, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Moves the clock on a poll at a time until <paramref name="passing"/> ends, for a minute at most.</summary>
    private async Task PollAsync(Task passing)
    {
        for (int poll = 0; poll < 12 && !passing.IsCompleted; poll++)
        {
            _time.Advance(AuthSchemaGate.PollInterval);
            await Task.WhenAny(passing, Task.Delay(TimeSpan.FromMilliseconds(50)));
        }
    }
}
