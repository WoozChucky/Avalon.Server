using Avalon.Database.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// The Auth model over one in-memory SQLite connection held open for the fixture's life; the
/// connection is the database, and every context a repository opens is handed it.
/// </summary>
internal sealed class AuthSqlite : IDbContextFactory<AuthDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AuthDbContext> _options;

    public AuthSqlite()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;
        using AuthDbContext context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public AuthDbContext CreateDbContext() => new(_options);

    public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}
