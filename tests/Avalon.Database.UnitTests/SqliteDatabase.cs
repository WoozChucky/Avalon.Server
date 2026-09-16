using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.UnitTests;

/// <summary>
/// A real relational database for the write paths, over one SQLite connection held open for the
/// life of the fixture. The connection <i>is</i> the database — <c>:memory:</c> is dropped when
/// the last connection to it closes — and the repositories open a context per call, so every
/// context is handed this same connection and no context's own dispose takes the schema with it.
/// </summary>
public sealed class SqliteDatabase<TContext> : IDbContextFactory<TContext>, IDisposable
    where TContext : DbContext
{
    private readonly SqliteConnection _connection;
    private readonly Func<DbContextOptions<TContext>, TContext> _create;
    private readonly DbContextOptions<TContext> _options;

    public SqliteDatabase(Func<DbContextOptions<TContext>, TContext> create)
    {
        _create = create;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connection)
            .Options;

        using TContext context = _create(_options);
        context.Database.EnsureCreated();
    }

    public int ContextsCreated { get; private set; }

    public TContext CreateDbContext()
    {
        ContextsCreated++;
        return _create(_options);
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}

public static class SqliteDatabase
{
    public static SqliteDatabase<AuthDbContext> Auth() => new(options => new AuthDbContext(options));

    public static SqliteDatabase<CharacterDbContext> Characters() =>
        new(options => new CharacterDbContext(options));

    public static SqliteDatabase<WorldDbContext> World() => new(options => new WorldDbContext(options));
}
