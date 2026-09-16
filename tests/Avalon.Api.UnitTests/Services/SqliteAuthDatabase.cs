using Avalon.Database.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// A real relational database over one SQLite connection held open for the life of the fixture.
/// The connection is the database — <c>:memory:</c> is dropped when the last connection to it
/// closes — and the repositories open a context per call, so every context gets this one
/// connection and no context's own dispose takes the schema with it.
/// </summary>
public sealed class SqliteAuthDatabase : IDbContextFactory<AuthDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AuthDbContext> _options;

    public SqliteAuthDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(_connection)
            .Options;

        using AuthDbContext context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public AuthDbContext CreateDbContext() => new(_options);

    public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}
