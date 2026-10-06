using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public sealed record PurchaseReservation(AccountId AccountId, int CredentialsVersion, string Product, string OfferId,
    string ProviderPriceId, long AmountMinor, string Currency, string Provider, string ProviderAccountId,
    string PaymentEnvironment, string LicenseEnvironment, string PublicSiteOrigin, string CheckoutEmail);
public sealed record PurchaseReservationResult(string? Error, PurchaseOrder? Order = null, PaymentAttempt? Attempt = null);
public sealed record CheckoutBinding(string CheckoutReference, string CheckoutUrl, DateTime ExpiresAt);
public sealed record PaymentEventClaim(PaymentEvent Event, Guid LeaseId, DateTime LeaseUntil, long Version);
public sealed record PaymentAttemptClaim(PaymentAttempt Attempt, Guid LeaseId, DateTime LeaseUntil, long Version);
public sealed record PaymentProcessingResult(bool Completed, string? FailureCode = null, bool NeedsReview = false);

public interface IPurchaseRepository
{
    Task<PurchaseReservationResult> ReserveAsync(PurchaseReservation reservation, CancellationToken ct = default);
    Task<PurchaseOrder?> FindForAccountAsync(AccountId account, Guid id, CancellationToken ct = default);
    Task<PurchaseOrder?> FindCurrentAsync(AccountId account, string product, string licenseEnvironment, CancellationToken ct = default);
    Task<bool> RecordCheckoutAsync(Guid attemptId, long expectedVersion, CheckoutBinding binding, CancellationToken ct = default);
    Task<bool> AcceptEventAsync(PaymentEvent notification, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentEventClaim>> ClaimEventsAsync(DateTime now, int count, TimeSpan lease, CancellationToken ct = default);
    Task<bool> CompleteEventAsync(Guid eventId, Guid leaseId, PaymentProcessingResult result, CancellationToken ct = default);
    Task<PaymentAttemptClaim?> ClaimAttemptAsync(Guid attemptId, DateTime now, TimeSpan lease, CancellationToken ct = default);
    Task<bool> ReleaseAttemptAsync(Guid attemptId, Guid leaseId, CancellationToken ct = default);
}

/// <summary>Short local transactions only. Provider requests happen after durable reservation.</summary>
public sealed class PurchaseRepository(IDbContextFactory<AuthDbContext> factory, TimeProvider clock) : IPurchaseRepository
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PurchaseReservationResult> ReserveAsync(PurchaseReservation reservation, CancellationToken ct = default)
    {
        Validate(reservation);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // This root lock serializes with consolidation and subsequent payment fulfillment.
        if (await db.Accounts.Where(a => a.Id == reservation.AccountId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, a => a.SessionEpoch), ct) != 1)
            return new("ACCOUNT_UNAVAILABLE");
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == reservation.AccountId, ct);
        if (account.Status != AccountStatus.Active || (account.AccessLevel & AccountAccessLevel.Player) == 0 ||
            account.IsLockedAt(Now) || account.GameplayConsolidationId is not null || account.CredentialsVersion != reservation.CredentialsVersion)
            return new("ACCOUNT_UNAVAILABLE");
        if (account.EmailVerifiedAt is null || string.IsNullOrWhiteSpace(account.Email)) return new("EMAIL_NOT_VERIFIED");
        var order = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account.Id &&
            x.Product == reservation.Product && x.LicenseEnvironment == reservation.LicenseEnvironment && x.Unresolved, ct);
        if (order is not null)
        {
            var existing = await db.PaymentAttempts.AsNoTracking().Where(x => x.OrderId == order.Id)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstAsync(ct);
            await transaction.CommitAsync(ct);
            return new(null, order, existing);
        }
        var now = Now;
        order = new PurchaseOrder { Id = Guid.NewGuid(), AccountId = account.Id, OriginalPurchaserAccountId = account.Id,
            Product = reservation.Product, OfferId = reservation.OfferId, AmountMinor = reservation.AmountMinor, Currency = reservation.Currency,
            Provider = reservation.Provider, ProviderAccountId = reservation.ProviderAccountId, PaymentEnvironment = reservation.PaymentEnvironment,
            LicenseEnvironment = reservation.LicenseEnvironment, CreatedAt = now };
        var returnUrl = reservation.PublicSiteOrigin.TrimEnd('/') + $"/account/purchases/{order.Id:D}";
        var attempt = new PaymentAttempt { Id = Guid.NewGuid(), OrderId = order.Id, Provider = order.Provider,
            ProviderAccountId = order.ProviderAccountId, Environment = order.PaymentEnvironment, OperationKey = Guid.NewGuid().ToString("N"),
            ProviderPriceId = reservation.ProviderPriceId, CheckoutEmail = account.Email,
            SuccessUrl = returnUrl, CancelUrl = returnUrl + "?canceled=true", CreatedAt = now };
        db.PurchaseOrders.Add(order);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(null, order, attempt);
    }

    public async Task<PurchaseOrder?> FindForAccountAsync(AccountId account, Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AccountId == account, ct);
    }

    public async Task<PurchaseOrder?> FindCurrentAsync(AccountId account, string product, string licenseEnvironment, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PurchaseOrders.AsNoTracking().Where(x => x.AccountId == account && x.Product == product && x.LicenseEnvironment == licenseEnvironment)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> RecordCheckoutAsync(Guid attemptId, long expectedVersion, CheckoutBinding binding, CancellationToken ct = default)
    {
        if (!Text(binding.CheckoutReference, 256) || !Uri.TryCreate(binding.CheckoutUrl, UriKind.Absolute, out var url) ||
            url.Scheme != "https" || url.UserInfo.Length != 0 || !url.IsDefaultPort || binding.CheckoutUrl.Length > 2048 ||
            binding.ExpiresAt.Kind != DateTimeKind.Utc || binding.ExpiresAt <= Now) throw new ArgumentException("Invalid checkout binding.", nameof(binding));
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PaymentAttempts.Where(x => x.Id == attemptId && x.Version == expectedVersion && x.Version < long.MaxValue &&
                (x.LeaseUntil == null || x.LeaseUntil > Now) && (x.CheckoutReference == null || x.CheckoutReference == binding.CheckoutReference) &&
                (x.State == PaymentAttemptState.Reserved || x.State == PaymentAttemptState.ProviderUnknown || x.State == PaymentAttemptState.CheckoutOpen))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CheckoutReference, binding.CheckoutReference)
                .SetProperty(x => x.CheckoutUrl, binding.CheckoutUrl).SetProperty(x => x.ExpiresAt, binding.ExpiresAt)
                .SetProperty(x => x.State, PaymentAttemptState.CheckoutOpen).SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
    }

    public async Task<bool> AcceptEventAsync(PaymentEvent notification, CancellationToken ct = default)
    {
        if (notification.Id == Guid.Empty || !Text(notification.Provider, 32) || !Text(notification.ProviderAccountId, 128) ||
            !Text(notification.Environment, 32) || !Text(notification.ExternalReference, 256) || !Text(notification.Type, 128) ||
            !Text(notification.ResourceReference, 256) || notification.CreatedAt.Kind != DateTimeKind.Utc ||
            notification.NextAttemptAt.Kind != DateTimeKind.Utc || notification.State != PaymentEventState.Pending ||
            notification.Version != 1 || notification.LeaseId is not null || notification.LeaseUntil is not null || notification.RetryCount != 0)
            throw new ArgumentException("Invalid normalized notification.", nameof(notification));
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await EventExists(db, notification, ct)) return false;
        db.PaymentEvents.Add(notification);
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await EventExists(db, notification, ct)) return false;
            throw;
        }
    }

    public async Task<IReadOnlyList<PaymentEventClaim>> ClaimEventsAsync(DateTime now, int count, TimeSpan lease, CancellationToken ct = default)
    {
        ValidateLease(now, lease);
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        await using var db = await factory.CreateDbContextAsync(ct);
        var candidates = await db.PaymentEvents.AsNoTracking().Where(x => x.NextAttemptAt <= now &&
                (x.State == PaymentEventState.Pending || x.State == PaymentEventState.Processing) && (x.LeaseUntil == null || x.LeaseUntil <= now))
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.Id).Take(count).ToListAsync(ct);
        var claims = new List<PaymentEventClaim>();
        foreach (var row in candidates)
        {
            var id = Guid.NewGuid();
            var until = now.Add(lease);
            if (row.Version == long.MaxValue || await db.PaymentEvents.Where(x => x.Id == row.Id && x.Version == row.Version)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.LeaseId, (Guid?)id).SetProperty(x => x.LeaseUntil, (DateTime?)until)
                    .SetProperty(x => x.State, PaymentEventState.Processing).SetProperty(x => x.Version, x => x.Version + 1), ct) != 1) continue;
            row.LeaseId = id; row.LeaseUntil = until; row.State = PaymentEventState.Processing; row.Version++;
            claims.Add(new(row, id, until, row.Version));
        }
        return claims;
    }

    public async Task<bool> CompleteEventAsync(Guid eventId, Guid leaseId, PaymentProcessingResult result, CancellationToken ct = default)
    {
        if (result.FailureCode is { } code && (!Text(code, 64) || code.Any(c => !char.IsAsciiLetterUpper(c) && c != '_')))
            throw new ArgumentException("Failure must use a safe reason code.", nameof(result));
        await using var db = await factory.CreateDbContextAsync(ct);
        var state = result.NeedsReview ? PaymentEventState.NeedsReview : result.Completed ? PaymentEventState.Completed : PaymentEventState.Pending;
        return await db.PaymentEvents.Where(x => x.Id == eventId && x.LeaseId == leaseId && x.LeaseUntil > Now &&
                x.State == PaymentEventState.Processing && x.Version < long.MaxValue && x.RetryCount < int.MaxValue)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.State, state).SetProperty(x => x.FailureCode, result.FailureCode)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.NextAttemptAt, Now.AddSeconds(5)).SetProperty(x => x.RetryCount, x => x.RetryCount + 1)
                .SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
    }

    public async Task<PaymentAttemptClaim?> ClaimAttemptAsync(Guid attemptId, DateTime now, TimeSpan lease, CancellationToken ct = default)
    {
        ValidateLease(now, lease);
        await using var db = await factory.CreateDbContextAsync(ct);
        var id = Guid.NewGuid();
        var until = now.Add(lease);
        if (await db.PaymentAttempts.Where(x => x.Id == attemptId && x.Version < long.MaxValue && (x.LeaseUntil == null || x.LeaseUntil <= now))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LeaseId, (Guid?)id).SetProperty(x => x.LeaseUntil, (DateTime?)until)
                .SetProperty(x => x.Version, x => x.Version + 1), ct) != 1) return null;
        var row = await db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == attemptId, ct);
        // A contender may have replaced an expired claim between update and read.
        return row.LeaseId == id ? new(row, id, until, row.Version) : null;
    }

    public async Task<bool> ReleaseAttemptAsync(Guid attemptId, Guid leaseId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.PaymentAttempts.Where(x => x.Id == attemptId && x.LeaseId == leaseId && x.LeaseUntil > Now && x.Version < long.MaxValue)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
    }

    private static Task<bool> EventExists(AuthDbContext db, PaymentEvent row, CancellationToken ct) => db.PaymentEvents.AnyAsync(x =>
        x.Provider == row.Provider && x.ProviderAccountId == row.ProviderAccountId && x.Environment == row.Environment && x.ExternalReference == row.ExternalReference, ct);
    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= max;
    private static void ValidateLease(DateTime now, TimeSpan lease)
    {
        if (now.Kind != DateTimeKind.Utc || lease <= TimeSpan.Zero || lease > TimeSpan.FromMinutes(5)) throw new ArgumentException("Invalid worker lease.");
    }
    private static void Validate(PurchaseReservation reservation)
    {
        if (reservation.AccountId is null || reservation.AccountId.Value <= 0 || reservation.CredentialsVersion < 0 ||
            !Text(reservation.Product, 128) || !Text(reservation.OfferId, 128) || !Text(reservation.ProviderPriceId, 128) ||
            reservation.AmountMinor <= 0 || reservation.Currency.Length != 3 || reservation.Currency.Any(c => !char.IsAsciiLetterLower(c)) ||
            !Text(reservation.Provider, 32) || !Text(reservation.ProviderAccountId, 128) || !Text(reservation.PaymentEnvironment, 32) ||
            !Text(reservation.LicenseEnvironment, 32) || !Uri.TryCreate(reservation.PublicSiteOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme != "https" || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.AbsolutePath != "/")
            throw new ArgumentException("Invalid trusted purchase reservation.", nameof(reservation));
    }
}
