using Avalon.Api.Commerce;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Avalon.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using StackExchange.Redis;

try
{
    string raw = Environment.GetEnvironmentVariable("AVALON_COMMERCE_TEST_POSTGRES") ?? throw new CheckFailure("Set the disposable PostgreSQL connection.");
    var connection = new NpgsqlConnectionStringBuilder(raw);
    Check(connection.Host is "127.0.0.1" or "localhost" or "::1" && connection.Database == "avalon_commerce_test_auth", "Refusing non-loopback or non-test storage.");
    var redisOptions = ConfigurationOptions.Parse(Environment.GetEnvironmentVariable("AVALON_COMMERCE_TEST_REDIS") ?? throw new CheckFailure("Set the disposable Redis endpoint."));
    Check(redisOptions.EndPoints.Count == 1 && (redisOptions.EndPoints[0] is System.Net.DnsEndPoint { Host: "127.0.0.1" or "localhost" or "::1" } ||
        redisOptions.EndPoints[0] is System.Net.IPEndPoint address && System.Net.IPAddress.IsLoopback(address.Address)), "Refusing non-loopback Redis.");
    foreach (string? name in new[] { "avalon_commerce_test_auth", "avalon_commerce_test_world", "avalon_commerce_test_characters" })
    {
        var master = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Database = "postgres" };
        await using var control = new NpgsqlConnection(master.ConnectionString); await control.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", control); exists.Parameters.AddWithValue("name", name);
        Check(Convert.ToInt64(await exists.ExecuteScalarAsync()) == 0, "Test databases must not already exist; this checker never overwrites storage.");
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", control); await create.ExecuteNonQueryAsync();
    }
    var factory = new FixtureFactory(connection.ConnectionString);
    await using (AuthDbContext db = factory.CreateDbContext()) { await db.Database.MigrateAsync(); Check(!db.Database.HasPendingModelChanges(), "Auth migration snapshot does not match its model."); }
    var worldConnection = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Database = "avalon_commerce_test_world" };
    await using (var db = new WorldDbContext(new DbContextOptionsBuilder<WorldDbContext>().UseNpgsql(worldConnection.ConnectionString).Options)) { await db.Database.MigrateAsync(); Check(!db.Database.HasPendingModelChanges(), "World migration snapshot does not match its model."); }
    var characterConnection = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Database = "avalon_commerce_test_characters" };
    await using (var db = new CharacterDbContext(new DbContextOptionsBuilder<CharacterDbContext>().UseNpgsql(characterConnection.ConnectionString).Options)) { await db.Database.MigrateAsync(); Check(!db.Database.HasPendingModelChanges(), "Character migration snapshot does not match its model."); }
    Console.WriteLine("PASS disposable Auth/world/character PostgreSQL migrations and model snapshots");
    TimeProvider clock = TimeProvider.System;
    var repo = new PurchaseRepository(factory, clock);
    var accounts = new AccountRepository(factory);
    DateTime now = DateTime.UtcNow;
    Account buyer = await accounts.CreateAsync(NewAccount("COMMERCERACE", now));
    PurchaseReservationResult[] reservations = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repo.ReserveAsync(Reservation(buyer.Id, now))));
    Check(reservations.All(x => x.Error == null) && reservations.Select(x => x.Order!.Id).Distinct().Count() == 1 && reservations.Select(x => x.Attempt!.OperationKey).Distinct().Count() == 1,
        "Concurrent reservations did not share one durable order and operation.");
    PurchaseReservationResult first = reservations[0];
    PaymentAttemptClaim?[] leases = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repo.ClaimAttemptAsync(first.Attempt!.Id, DateTime.UtcNow, TimeSpan.FromMinutes(2))));
    Check(leases.Count(x => x is not null) == 1, "Attempt claims did not have exactly one winner.");
    await repo.ReleaseAttemptAsync(first.Attempt!.Id, leases.Single(x => x is not null)!.LeaseId);
    Console.WriteLine("PASS real cross-connection reservation and attempt-claim races");

    string eventReference = Guid.NewGuid().ToString("N");
    PaymentEvent Event() => new()
    {
        Id = Guid.NewGuid(),
        Provider = "fixture",
        ProviderAccountId = "merchant",
        Environment = "sandbox",
        ExternalReference = eventReference,
        Type = "fixture-payment",
        ResourceKind = PaymentResourceKinds.Checkout,
        ResourceReference = "checkout-first",
        OrderId = first.Order!.Id,
        PaymentAttemptId = first.Attempt.Id,
        CreatedAt = now,
        NextAttemptAt = now
    };
    bool[] accepted = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repo.AcceptEventAsync(Event())));
    Check(accepted.Count(x => x) == 1, "Duplicate verified events were not durably deduplicated.");
    IReadOnlyList<PaymentEventClaim>[] eventClaims = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => repo.ClaimEventsAsync(DateTime.UtcNow, 1, TimeSpan.FromMinutes(2))));
    Check(eventClaims.Sum(x => x.Count) == 1, "Event leases did not have exactly one winner.");
    PaymentEventClaim eventClaim = eventClaims.SelectMany(x => x).Single();
    Check(await repo.CompleteEventAsync(eventClaim.Event.Id, eventClaim.LeaseId, new(true)), "Event completion failed.");
    Check(!await repo.CompleteEventAsync(eventClaim.Event.Id, eventClaim.LeaseId, new(true)), "A stale event worker completed the row again.");
    Console.WriteLine("PASS duplicate event and event-claim races, stale completion fencing");

    await using (AuthDbContext db = factory.CreateDbContext())
    {
        await db.PaymentAttempts.Where(x => x.Id == first.Attempt.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.State, PaymentAttemptState.Expired));
    }
    PurchaseReservationResult second = await repo.ReserveAsync(Reservation(buyer.Id, now));
    Check(second.Error == null && second.Attempt!.Id != first.Attempt.Id, "A confirmed expired checkout did not reserve its replacement.");
    PaymentAttemptClaim claim1 = (await repo.ClaimAttemptAsync(first.Attempt.Id, DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
    PaymentAttemptClaim claim2 = (await repo.ClaimAttemptAsync(second.Attempt!.Id, DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
    long version = (await repo.FindOrderAsync(first.Order!.Id))!.Version;
    PaymentSnapshot[] snapshots = new[] { Snapshot(first.Order.Id, claim1.Attempt.Id, "one", now), Snapshot(first.Order.Id, claim2.Attempt.Id, "two", now) };
    PurchaseReconciliationCommand[] commands = new[] { new PurchaseReconciliationCommand(claim1, version, snapshots[0]), new PurchaseReconciliationCommand(claim2, version, snapshots[1]) };
    PurchaseReconciliationResult[] fulfilled = await Task.WhenAll(commands.Select(x => repo.ApplySnapshotAsync(x)));
    Check(fulfilled.Count(x => x.Applied) == 1, "Concurrent distinct successes did not fence the order version.");
    for (int i = 0; i < fulfilled.Length; i++) if (!fulfilled[i].Applied) Check((await repo.ApplySnapshotAsync(commands[i] with { ExpectedOrderVersion = (await repo.FindOrderAsync(first.Order.Id))!.Version })).Applied, "Fresh duplicate evidence did not reconcile.");
    await repo.ReleaseAttemptAsync(claim1.Attempt.Id, claim1.LeaseId); await repo.ReleaseAttemptAsync(claim2.Attempt.Id, claim2.LeaseId);
    await using (AuthDbContext db = factory.CreateDbContext())
    {
        Check(await db.GameLicenses.CountAsync(x => x.AccountId == buyer.Id) == 1, "Duplicate payments created multiple licenses.");
        Check((await db.PurchaseOrders.SingleAsync(x => x.Id == first.Order.Id)).ReconciliationIssue == "DUPLICATE_PAYMENT", "Duplicate payment was not recorded for support.");
    }
    Console.WriteLine("PASS concurrent payment successes create exactly one development grant and flag the duplicate");

    Account source = await accounts.CreateAsync(NewAccount("COMMERCESTORE", now, true));
    Account target = await accounts.CreateAsync(NewAccount("COMMERCEWEB", now));
    const string subject = "76561198000000123";
    await new ExternalIdentityRepository(factory, clock).LinkAsync(source.Id, "steam", subject, now);
    PurchaseReservationResult moving = await repo.ReserveAsync(Reservation(source.Id, now));
    PaymentAttemptClaim movingClaim = (await repo.ClaimAttemptAsync(moving.Attempt!.Id, DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
    var consolidation = new AccountConsolidationRepository(factory, clock);
    var request = new AccountConsolidationRequest(Guid.NewGuid(), target.Id, subject, target.CredentialsVersion, target.SessionEpoch, null,
        [new WorldId(1)], now.AddMinutes(2));
    Task<PurchaseReconciliationResult> paymentTask = repo.ApplySnapshotAsync(new(movingClaim, moving.Order!.Version, Snapshot(moving.Order.Id, moving.Attempt.Id, "moving", now)));
    Task<AccountConsolidationResult> consolidationTask = consolidation.BeginAsync(request, default);
    await Task.WhenAll(paymentTask, consolidationTask);
    Check((await consolidationTask).Error == null, "Consolidation failed during payment reconciliation.");
    Check(await consolidation.RecordTransferAsync(request.OperationId, new WorldId(1), 0, default), "Empty character transfer was not recorded.");
    Check(await consolidation.FinalizeAsync(request.OperationId, default), "Consolidation did not finalize.");
    await repo.ReleaseAttemptAsync(moving.Attempt.Id, movingClaim.LeaseId);
    PaymentAttemptClaim freshClaim = (await repo.ClaimAttemptAsync(moving.Attempt.Id, DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
    Check((await repo.ApplySnapshotAsync(new(freshClaim, (await repo.FindOrderAsync(moving.Order.Id))!.Version, Snapshot(moving.Order.Id, moving.Attempt.Id, "moving", now)))).Applied, "Post-consolidation evidence did not reconcile.");
    await repo.ReleaseAttemptAsync(moving.Attempt.Id, freshClaim.LeaseId);
    await using (AuthDbContext db = factory.CreateDbContext())
    {
        PurchaseOrder order = await db.PurchaseOrders.SingleAsync(x => x.Id == moving.Order.Id);
        GameLicense license = await db.GameLicenses.SingleAsync(x => x.Id == order.LicenseId);
        Check(order.AccountId == target.Id && order.OriginalPurchaserAccountId == source.Id && license.AccountId == target.Id, "Consolidation lost beneficiary or purchaser provenance.");
        Check(license.Authorizes(target.Id, "avalon.base", "development", DateTime.UtcNow) && !license.Authorizes(target.Id, "avalon.base", "production", DateTime.UtcNow), "Development grant crossed the production boundary.");
    }
    Console.WriteLine("PASS payment/consolidation race preserves original purchaser, current beneficiary and environment isolation");

    using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
    Check((long)await redis.GetDatabase().ExecuteAsync("DBSIZE") == 0, "Disposable Redis must be empty; no existing keys are overwritten.");
    IReplicatedCache cache = Substitute.For<IReplicatedCache>(); cache.Database.Returns(redis.GetDatabase());
    var budget = new CheckoutBudget(cache);
    var sharedOperation = Guid.NewGuid();
    bool[] reused = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => budget.TryTakeAsync("development", buyer.Id, "shared-source", sharedOperation, default)));
    Check(reused.All(x => x), "Replaying an operation consumed additional Redis budget.");
    bool[] bounded = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => budget.TryTakeAsync("development", buyer.Id, "shared-source", Guid.NewGuid(), default)));
    Check(bounded.Count(x => x) == 4, "Atomic account budget did not allow exactly five operations.");
    bool[] sourceBounded = await Task.WhenAll(Enumerable.Range(100, 40).Select(x => budget.TryTakeAsync("development", new AccountId(x), "other-source", Guid.NewGuid(), default)));
    Check(sourceBounded.Count(x => x) == 20, "Atomic shared-source budget was not twenty operations.");
    Console.WriteLine("PASS real Redis once-per-operation and concurrent account/source budget limits");
    if (args.Contains("--serve-stripe")) await SandboxHost.RunAsync(factory, cache, clock);
    else Console.WriteLine("All local storage checks passed. These normalized-evidence checks did not contact Stripe.");
}
catch (CheckFailure ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
catch (Exception ex) { Console.Error.WriteLine($"Commerce checker failed ({ex.GetType().Name}); diagnostic bodies and credentials were not logged."); Environment.ExitCode = 1; }

static void Check(bool condition, string message) { if (!condition) throw new CheckFailure(message); }
static Account NewAccount(string username, DateTime now, bool store = false) => new()
{
    Username = username,
    Email = username.ToLowerInvariant() + "@example.test",
    EmailVerifiedAt = now,
    Salt = [1],
    Verifier = [2],
    JoinDate = now,
    AccessLevel = AccountAccessLevel.Player,
    IsStoreGenerated = store
};
static PurchaseReservation Reservation(AccountId account, DateTime now) => new(account, 0, "avalon.base", "fixture-offer", "fixture-price", 800, "eur", "fixture", "merchant",
    "sandbox", "development", "https://example.test", "ignored@example.test", "fixture-product", "card", now.AddMinutes(30));
static PaymentSnapshot Snapshot(Guid order, Guid attempt, string reference, DateTime now) => new("fixture", "merchant", "sandbox", order, attempt,
    "checkout-" + reference, "payment-" + reference, "fixture-price", "fixture-product", 1, 800, "eur", 150, 800, true, true, PaymentAttemptState.Paid, now.AddMinutes(30), [], []);
sealed class CheckFailure(string message) : Exception(message);
sealed class FixtureFactory(string connection) : IDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(connection).Options);
}
