using Avalon.Database.Crashes;
using Npgsql;
using Xunit;

namespace Avalon.Crashes.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _database = "avalon_crashes_test_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private bool _created;

    public CrashesDbContextFactory Factory { get; private set; } = null!;
    public CrashQueue Queue => new(Factory);

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("AVALON_CRASHES_INTEGRATION") != "1")
            return;

        string configured = Environment.GetEnvironmentVariable("AVALON_CRASHES_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Explicit integration mode requires AVALON_CRASHES_TEST_POSTGRES.");
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Database != "postgres" || builder.Host is not ("127.0.0.1" or "localhost") || builder.Port == 5432)
            throw new InvalidOperationException("Tests require the postgres maintenance database on an isolated localhost port other than 5432.");

        _adminConnection = builder.ConnectionString;
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
        await command.ExecuteNonQueryAsync();
        _created = true;
        builder.Database = _database;
        builder.Pooling = false;
        Factory = new CrashesDbContextFactory(new CrashDatabaseOptions { ConnectionString = builder.ConnectionString });
        await new CrashMigrator(Factory).MigrateAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (!_created)
            return;

        // The only interpolation is a private generated UUID name, never the supplied database.
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
