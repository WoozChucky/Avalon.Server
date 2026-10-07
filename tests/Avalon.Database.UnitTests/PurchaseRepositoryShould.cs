using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class PurchaseRepositoryShould
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private readonly Clock _clock = new();

    [Fact]
    public async Task One_unresolved_order_per_account_product_environment()
    {
        using var database = SqliteDatabase.Auth();
        Account account = await Account(database, "PURCHASER");
        var repo = new PurchaseRepository(database, _clock);
        PurchaseReservationResult first = await repo.ReserveAsync(Reservation(account.Id));
        PurchaseReservationResult second = await repo.ReserveAsync(Reservation(account.Id) with { AmountMinor = 900, ProviderPriceId = "changed" });
        Assert.Null(first.Error);
        Assert.Equal(first.Order!.Id, second.Order!.Id);
        Assert.Equal(first.Attempt!.OperationKey, second.Attempt!.OperationKey);
        Assert.Equal(800, second.Order.AmountMinor);
        Assert.Equal("eur", second.Order.Currency);
        Assert.Equal("price-test", second.Attempt.ProviderPriceId);
        await using AuthDbContext db = database.CreateDbContext();
        Assert.Single(await db.PurchaseOrders.ToListAsync());
        Assert.Single(await db.PaymentAttempts.ToListAsync());
    }

    [Fact]
    public async Task Orders_preserve_original_price_and_purchaser()
    {
        using var database = SqliteDatabase.Auth();
        Account one = await Account(database, "ONE");
        Account two = await Account(database, "TWO");
        var repo = new PurchaseRepository(database, _clock);
        PurchaseReservationResult reserved = await repo.ReserveAsync(Reservation(one.Id));
        Assert.Null(await repo.FindForAccountAsync(two.Id, reserved.Order!.Id));
        PurchaseOrder? saved = await repo.FindForAccountAsync(one.Id, reserved.Order.Id);
        Assert.Equal(one.Id, saved!.OriginalPurchaserAccountId);
        Assert.Equal(800, saved.AmountMinor);
        Assert.Equal("eur", saved.Currency);
    }

    [Theory]
    [InlineData("unverified")]
    [InlineData("credentials")]
    [InlineData("locked")]
    [InlineData("consolidation")]
    public async Task Ineligible_accounts_cannot_reserve(string condition)
    {
        using var database = SqliteDatabase.Auth();
        Account account = await Account(database, "DENIED");
        await using (AuthDbContext db = database.CreateDbContext())
        {
            Account row = await db.Accounts.SingleAsync(x => x.Id == account.Id);
            if (condition == "unverified") row.EmailVerifiedAt = null;
            if (condition == "credentials") row.CredentialsVersion++;
            if (condition == "locked") row.Locked = true;
            await db.SaveChangesAsync();
            if (condition == "consolidation")
                await db.Accounts.Where(x => x.Id == account.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.GameplayConsolidationId, (Guid?)Guid.NewGuid()));
        }
        Assert.NotNull((await new PurchaseRepository(database, _clock).ReserveAsync(Reservation(account.Id))).Error);
        await using AuthDbContext read = database.CreateDbContext();
        Assert.Empty(await read.PurchaseOrders.ToListAsync());
    }

    [Fact]
    public async Task Claim_expiry_fences_old_worker()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new PurchaseRepository(database, _clock);
        PaymentEvent row = Event("event-one");
        Assert.True(await repo.AcceptEventAsync(row));
        Assert.False(await repo.AcceptEventAsync(Event("event-one")));
        PaymentEventClaim first = Assert.Single(await repo.ClaimEventsAsync(Now, 10, TimeSpan.FromMinutes(2)));
        Assert.Empty(await repo.ClaimEventsAsync(Now, 10, TimeSpan.FromMinutes(2)));
        _clock.Now = Now.AddMinutes(3);
        Assert.False(await repo.CompleteEventAsync(row.Id, first.LeaseId, new(true)));
        PaymentEventClaim second = Assert.Single(await repo.ClaimEventsAsync(_clock.Now, 10, TimeSpan.FromMinutes(2)));
        Assert.NotEqual(first.LeaseId, second.LeaseId);
        Assert.False(await repo.CompleteEventAsync(row.Id, first.LeaseId, new(true)));
        Assert.True(await repo.CompleteEventAsync(row.Id, second.LeaseId, new(true)));
        Assert.Empty(await repo.ClaimEventsAsync(_clock.Now, 10, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task Attempt_leases_cannot_be_released_by_expired_workers()
    {
        using var database = SqliteDatabase.Auth();
        Account account = await Account(database, "LEASE");
        var repo = new PurchaseRepository(database, _clock);
        PurchaseReservationResult reservation = await repo.ReserveAsync(Reservation(account.Id));
        PaymentAttemptClaim? first = await repo.ClaimAttemptAsync(reservation.Attempt!.Id, Now, TimeSpan.FromMinutes(2));
        Assert.NotNull(first);
        Assert.Null(await repo.ClaimAttemptAsync(first.Attempt.Id, Now, TimeSpan.FromMinutes(2)));
        _clock.Now = Now.AddMinutes(3);
        PaymentAttemptClaim? second = await repo.ClaimAttemptAsync(first.Attempt.Id, _clock.Now, TimeSpan.FromMinutes(2));
        Assert.NotNull(second);
        Assert.False(await repo.ReleaseAttemptAsync(first.Attempt.Id, first.LeaseId));
        Assert.True(await repo.ReleaseAttemptAsync(second.Attempt.Id, second.LeaseId));
    }

    internal static PurchaseReservation Reservation(AccountId account) => new(account, 0, "avalon.base", "base", "price-test", 800, "eur",
        "stripe", "merchant-test", "sandbox", "development", "https://avalon.example.test", "purchaser@example.test", "catalog", "card", Now.AddMinutes(30));

    [Fact]
    public async Task Retry_backoff_grows_without_losing_fencing()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new PurchaseRepository(database, _clock);
        PaymentEvent row = Event("retry");
        await repo.AcceptEventAsync(row);
        PaymentEventClaim first = Assert.Single(await repo.ClaimEventsAsync(Now, 10, TimeSpan.FromMinutes(2)));
        await repo.CompleteEventAsync(row.Id, first.LeaseId, new(false, "PROVIDER_UNAVAILABLE"));
        _clock.Now = Now.AddSeconds(5);
        PaymentEventClaim second = Assert.Single(await repo.ClaimEventsAsync(_clock.Now, 10, TimeSpan.FromMinutes(2)));
        await repo.CompleteEventAsync(row.Id, second.LeaseId, new(false, "PROVIDER_UNAVAILABLE"));
        _clock.Now = Now.AddSeconds(14);
        Assert.Empty(await repo.ClaimEventsAsync(_clock.Now, 10, TimeSpan.FromMinutes(2)));
        _clock.Now = Now.AddSeconds(15);
        Assert.Single(await repo.ClaimEventsAsync(_clock.Now, 10, TimeSpan.FromMinutes(2)));
    }

    private static PaymentEvent Event(string reference) => new()
    {
        Id = Guid.NewGuid(),
        Provider = "stripe",
        ProviderAccountId = "merchant-test",
        Environment = "sandbox",
        ExternalReference = reference,
        Type = "checkout",
        ResourceReference = "checkout-one",
        CreatedAt = Now,
        NextAttemptAt = Now
    };

    internal static async Task<Avalon.Domain.Auth.Account> Account(SqliteDatabase<AuthDbContext> database, string name)
    {
        Account row = StoreAuthenticationModelShould.Account(name);
        row.EmailVerifiedAt = Now;
        return await new AccountRepository(database).CreateAsync(row);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = PurchaseRepositoryShould.Now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
