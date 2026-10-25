using System.Security.Cryptography;
using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Avalon.Crashes.IntegrationTests;

internal sealed class VerticalDatabase : IAsyncDisposable
{
    private readonly PostgresFixture _postgres = new();
    private readonly string _role = "identity_" + Guid.NewGuid().ToString("N");
    public CrashesDbContextFactory Factory => _postgres.Factory;
    public string Owner { get; private set; } = "";
    public string Identity { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await using CrashesDbContext db = Factory.CreateDbContext();
        Owner = db.Database.GetConnectionString()!;
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await ExecuteAsync(Owner, $"CREATE ROLE {_role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT");
        await ExecuteAsync(Owner, $"GRANT USAGE ON SCHEMA public TO {_role}; GRANT SELECT ON crash_reports, crash_groups, \"__EFMigrationsHistory\" TO {_role}; GRANT UPDATE (status, fixed_in_version, issue_url, notes, is_regression, triage_revision, triaged_at, fixed_at) ON crash_groups TO {_role}");
        Identity = new NpgsqlConnectionStringBuilder(Owner) { Username = _role, Password = password }.ConnectionString;
    }

    public async Task AssertRestrictedRoleAsync()
    {
        await ExecuteAsync(Identity, "SELECT * FROM crash_reports; SELECT * FROM crash_groups; SELECT * FROM \"__EFMigrationsHistory\"");
        foreach (string sql in new[] { "INSERT INTO crash_reports DEFAULT VALUES", "UPDATE crash_groups SET report_count = report_count + 1",
            "DELETE FROM crash_reports", "CREATE TABLE forbidden_migration (id integer)", "DELETE FROM \"__EFMigrationsHistory\"" })
        {
            PostgresException denied = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Identity, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Owner.Length > 0)
        {
            string maintenance = new NpgsqlConnectionStringBuilder(Owner) { Database = "postgres" }.ConnectionString;
            await ExecuteAsync(maintenance, $"DROP ROLE {_role}");
        }
    }
}
