using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Testing;

/// <summary>
/// The test databases' starting point: one SQLite database per context type, created by <c>EnsureCreated</c> the first
/// time it is asked for and held open for the life of the process, copied into each new database with SQLite's backup
/// (schema, rows and sequences, page for page). A database so made holds exactly what <c>EnsureCreated</c> writes,
/// the whole <c>HasData</c> seed with it, for a fraction of what writing that seed again costs: writing the world seed
/// was most of what a test opening a world database spent (about 90 ms each; <c>SeedIntegrityShould</c>'s 133 tests went
/// from 12 s to under 1 s).
/// </summary>
/// <remarks>
/// Compiled into each test project that makes such databases (Avalon.World.Testing, Avalon.Api.Testing,
/// Avalon.Database.UnitTests). Only for contexts built over the plain options a caller passes in: a context whose options
/// add interceptors or warnings creates its own database as before.
/// </remarks>
public static class SqliteTemplates
{
    private static readonly ConcurrentDictionary<Type, Lazy<SqliteConnection>> s_templates = new();

    /// <summary>
    /// Fills <paramref name="target" />, an open and empty connection, with what <c>EnsureCreated</c> makes for
    /// <typeparamref name="TContext" />; <paramref name="create" /> builds the context over the given options.
    /// </summary>
    public static void CopyInto<TContext>(SqliteConnection target, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(create);

        SqliteConnection template = s_templates.GetOrAdd(typeof(TContext), _ => new Lazy<SqliteConnection>(() =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            using TContext context = create(new DbContextOptionsBuilder<TContext>().UseSqlite(connection).Options);
            context.Database.EnsureCreated();
            return connection;
        })).Value;

        // A connection serves one caller at a time.
        lock (template)
            template.BackupDatabase(target);
    }
}
