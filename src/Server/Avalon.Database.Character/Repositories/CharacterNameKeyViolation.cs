using System.Data.Common;
using Npgsql;

namespace Avalon.Database.Character.Repositories;

/// <summary>
/// Whether a write failed because another character holds the name's key (#757): the unique index on
/// <c>Characters.NameKey</c>, and nothing else. On Postgres that is SQLSTATE 23505 on <c>IX_Characters_NameKey</c>;
/// on SQLite (the tests) a UNIQUE constraint failure naming <c>Characters.NameKey</c>. A tracked SaveChanges wraps the
/// provider's exception in a DbUpdateException, and ExecuteUpdate throws it bare, so both are looked through.
/// </summary>
public static class CharacterNameKeyViolation
{
    private const string SqliteUniqueOnNameKey = "UNIQUE constraint failed: Characters.NameKey";

    public static bool Is(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg)
            {
                return string.Equals(pg.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal) &&
                       string.Equals(pg.ConstraintName, CharacterDbContext.NameKeyIndex, StringComparison.Ordinal);
            }

            // SQLite's exception type lives in a package only the tests reference; its message names the column.
            if (e is DbException db && string.Equals(db.GetType().Name, "SqliteException", StringComparison.Ordinal))
                return db.Message.Contains(SqliteUniqueOnNameKey, StringComparison.Ordinal);
        }

        return false;
    }
}
