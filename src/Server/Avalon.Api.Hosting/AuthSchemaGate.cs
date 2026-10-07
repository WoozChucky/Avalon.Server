using System.Globalization;
using Avalon.Api.Hosting.Worlds;
using Avalon.Database.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Hosting;

/// <summary>Which part an API process plays in the auth database's schema (#794, design section 5.2).</summary>
public enum AuthSchemaRole
{
    /// <summary>Waits at startup until the auth database has no migration its build knows of pending.</summary>
    Reader,

    /// <summary>Migrates the auth database at startup. One service owns the schema: identity.</summary>
    Owner,
}

/// <summary>
/// The auth database's schema at startup (#794, design section 5.2): the process that owns it migrates it, as the
/// api always has; a reader waits until <c>GetPendingMigrationsAsync</c> is empty, checking every
/// <see cref="PollInterval"/>, and fails, naming <see cref="WaitSetting"/>, once the wait is spent, so a reader
/// started before the owner's migration never serves against an older schema than its build expects.
/// </summary>
public sealed class AuthSchemaGate
{
    public const string WaitSetting = "Application:Startup:AuthSchemaWaitSeconds";
    public const int DefaultWaitSeconds = 300;

    /// <summary>How often a reader asks whether migrations are still pending.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time;
    private readonly Func<DbContext, CancellationToken, Task<bool>> _pending;

    /// <param name="pending">Whether a context has migrations pending; <c>GetPendingMigrationsAsync</c> unless a test says otherwise.</param>
    public AuthSchemaGate(AuthSchemaRole role, TimeSpan wait, TimeProvider time,
        Func<DbContext, CancellationToken, Task<bool>>? pending = null)
    {
        Role = role;
        Wait = wait;
        _time = time;
        _pending = pending ?? (async (context, cancellationToken) =>
            (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any());
    }

    public AuthSchemaRole Role { get; }

    /// <summary>How long a reader waits for the owner's migrations.</summary>
    public TimeSpan Wait { get; }

    /// <summary>
    /// The wait under <see cref="WaitSetting"/>, in whole seconds, <see cref="DefaultWaitSeconds"/> when it is not set;
    /// anything but a whole number of at least 1 is refused, naming the setting.
    /// </summary>
    public static TimeSpan WaitFrom(IConfiguration configuration)
    {
        string? value = configuration[WaitSetting];
        if (value is null)
            return TimeSpan.FromSeconds(DefaultWaitSeconds);

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds < 1)
            throw new InvalidOperationException($"{WaitSetting} must be a whole number of seconds, at least 1.");

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Migrates the auth database for the owner; for a reader, returns once no migration is pending, and throws once
    /// <see cref="Wait"/> has passed with some still pending.
    /// </summary>
    public async Task PassAsync(IDbContextFactory<AuthDbContext> auth, ApiDatabaseMigrator migrator,
        CancellationToken cancellationToken)
    {
        if (Role == AuthSchemaRole.Owner)
        {
            await migrator.MigrateAuthAsync(auth, cancellationToken);
            return;
        }

        DateTimeOffset deadline = _time.GetUtcNow() + Wait;
        while (true)
        {
            await using (AuthDbContext context = await auth.CreateDbContextAsync(cancellationToken))
            {
                if (!await _pending(context, cancellationToken))
                    return;
            }

            if (_time.GetUtcNow() >= deadline)
            {
                throw new InvalidOperationException(
                    "The auth database still has migrations this build knows of pending after " +
                    $"{Wait.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds " +
                    $"({WaitSetting}). The service that owns the schema (identity) applies them when it starts.");
            }

            await Task.Delay(PollInterval, _time, cancellationToken);
        }
    }
}
