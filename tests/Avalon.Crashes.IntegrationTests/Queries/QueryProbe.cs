using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Avalon.Crashes.IntegrationTests.Queries;

internal sealed record CapturedQuery(string Sql, NpgsqlParameter[] Parameters, int Timeout, IsolationLevel? Isolation);

internal sealed class QueryProbe : DbCommandInterceptor
{
    public List<CapturedQuery> Queries { get; } = [];
    public Func<Task>? BeforeSecondRead { get; init; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Queries.Add(new(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(x => x.Clone()).ToArray(),
            command.CommandTimeout, command.Transaction?.IsolationLevel));
        if (Queries.Count == 2 && BeforeSecondRead is { } callback)
            await callback();
        return result;
    }
}

internal sealed class ProbedContextFactory(string connection, QueryProbe probe) : IDbContextFactory<Database.Crashes.CrashesDbContext>
{
    public Database.Crashes.CrashesDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<Database.Crashes.CrashesDbContext>().UseNpgsql(connection).AddInterceptors(probe).Options);
}
