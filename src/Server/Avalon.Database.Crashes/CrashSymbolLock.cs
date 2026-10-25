using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Avalon.Crashes.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Crashes;

/// <summary>A transaction-scoped advisory lock on a dedicated context/connection, released even after cancellation.</summary>
public sealed class CrashSymbolLock(IDbContextFactory<CrashesDbContext> factory) : ICrashSymbolLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string cacheVersion, string moduleIdentity, CancellationToken ct)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(cacheVersion + "\0" + moduleIdentity));
        long key = BinaryPrimitives.ReadInt64BigEndian(digest);
        CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        try
        {
            IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            return new Lease(db, transaction);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(CrashesDbContext db, IDbContextTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}
