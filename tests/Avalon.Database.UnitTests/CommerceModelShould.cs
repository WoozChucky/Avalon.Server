using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class CommerceModelShould
{
    [Fact]
    public async Task External_references_are_provider_and_environment_scoped()
    {
        using var database = SqliteDatabase.Auth();
        var account = await PurchaseRepositoryShould.Account(database, "SCOPE");
        var result = await new PurchaseRepository(database, TimeProvider.System).ReserveAsync(PurchaseRepositoryShould.Reservation(account.Id));
        await using var db = database.CreateDbContext();
        var original = await db.PaymentAttempts.SingleAsync();
        original.CheckoutReference = "checkout-shared";
        await db.SaveChangesAsync();
        var sequence = 2;
        foreach (var scope in new[] { ("alternate", "sandbox"), ("stripe", "other-sandbox") })
        {
            db.PaymentAttempts.Add(new PaymentAttempt { Id = Guid.NewGuid(), OrderId = result.Order!.Id, Sequence = sequence++, Provider = scope.Item1,
                ProviderAccountId = "merchant-test", Environment = scope.Item2, OperationKey = Guid.NewGuid().ToString("N"),
                ProviderPriceId = "price-test", CheckoutEmail = "test@example.test", SuccessUrl = "https://example.test", CancelUrl = "https://example.test",
                CheckoutReference = "checkout-shared", CreatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        db.PaymentAttempts.Add(new PaymentAttempt { Id = Guid.NewGuid(), OrderId = result.Order!.Id, Sequence = sequence, Provider = "stripe", ProviderAccountId = "merchant-test",
            Environment = "sandbox", OperationKey = Guid.NewGuid().ToString("N"), ProviderPriceId = "price-test", CheckoutEmail = "test@example.test",
            SuccessUrl = "https://example.test", CancelUrl = "https://example.test", CheckoutReference = "checkout-shared", CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_refuses_two_unresolved_orders_even_without_repository()
    {
        using var database = SqliteDatabase.Auth();
        var account = await PurchaseRepositoryShould.Account(database, "UNIQUE");
        var result = await new PurchaseRepository(database, TimeProvider.System).ReserveAsync(PurchaseRepositoryShould.Reservation(account.Id));
        await using var db = database.CreateDbContext();
        var duplicate = result.Order!;
        duplicate.Id = Guid.NewGuid();
        db.PurchaseOrders.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
