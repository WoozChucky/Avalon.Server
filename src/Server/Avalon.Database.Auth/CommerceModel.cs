using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avalon.Database.Auth;

internal static class CommerceModel
{
    public static void Configure(ModelBuilder model)
    {
        var order = model.Entity<PurchaseOrder>();
        Base(order);
        order.Property(x => x.AccountId).HasConversion(x => x.Value, x => new AccountId(x));
        order.Property(x => x.OriginalPurchaserAccountId).HasConversion(x => x.Value, x => new AccountId(x));
        order.Property(x => x.Product).HasMaxLength(128);
        order.Property(x => x.OfferId).HasMaxLength(128);
        order.Property(x => x.Currency).HasMaxLength(3);
        order.Property(x => x.Provider).HasMaxLength(32);
        order.Property(x => x.ProviderAccountId).HasMaxLength(128);
        order.Property(x => x.PaymentEnvironment).HasMaxLength(32);
        order.Property(x => x.LicenseEnvironment).HasMaxLength(32);
        order.Property(x => x.ReconciliationIssue).HasMaxLength(64);
        order.HasIndex(x => new { x.AccountId, x.Product, x.LicenseEnvironment }).IsUnique().HasFilter("\"Unresolved\" = TRUE");
        order.HasIndex(x => x.LicenseId).IsUnique();
        order.HasIndex(x => x.FundingAttemptId).IsUnique();
        order.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        order.HasOne<Account>().WithMany().HasForeignKey(x => x.OriginalPurchaserAccountId).OnDelete(DeleteBehavior.Restrict);
        order.HasOne<GameLicense>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
        order.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.FundingAttemptId).OnDelete(DeleteBehavior.Restrict);
        order.ToTable(t => t.HasCheckConstraint("CK_PurchaseOrders_Amount", "\"AmountMinor\" > 0 AND length(\"Currency\") = 3 AND \"Currency\" = lower(\"Currency\")"));

        var attempt = model.Entity<PaymentAttempt>();
        Scoped(attempt);
        attempt.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        attempt.Property(x => x.OperationKey).HasMaxLength(128);
        attempt.Property(x => x.ProviderPriceId).HasMaxLength(128);
        attempt.Property(x => x.ProviderCatalogProductId).HasMaxLength(256);
        attempt.Property(x => x.PaymentMethods).HasMaxLength(512);
        attempt.Property(x => x.CheckoutEmail).HasMaxLength(254);
        attempt.Property(x => x.SuccessUrl).HasMaxLength(2048);
        attempt.Property(x => x.CancelUrl).HasMaxLength(2048);
        attempt.Property(x => x.CheckoutUrl).HasMaxLength(2048);
        attempt.Property(x => x.CheckoutReference).HasMaxLength(256);
        attempt.Property(x => x.PaymentReference).HasMaxLength(256);
        attempt.HasIndex(x => new { x.Provider, x.ProviderAccountId, x.Environment, x.CheckoutReference }).IsUnique();
        attempt.HasIndex(x => new { x.Provider, x.ProviderAccountId, x.Environment, x.PaymentReference }).IsUnique();
        attempt.HasIndex(x => new { x.Provider, x.ProviderAccountId, x.Environment, x.OperationKey }).IsUnique();
        attempt.HasIndex(x => new { x.OrderId, x.Sequence }).IsUnique();
        attempt.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);

        var refund = model.Entity<PaymentRefund>();
        Scoped(refund);
        refund.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        refund.Property(x => x.RequestedBy).HasConversion(x => x.Value, x => new AccountId(x));
        refund.Property(x => x.Reason).HasMaxLength(500);
        refund.Property(x => x.OperationKey).HasMaxLength(128);
        refund.Property(x => x.ExternalReference).HasMaxLength(256);
        refund.Property(x => x.FailureCode).HasMaxLength(64);
        refund.HasIndex(x => new { x.Provider, x.ProviderAccountId, x.Environment, x.ExternalReference }).IsUnique();
        refund.HasIndex(x => new { x.Provider, x.ProviderAccountId, x.Environment, x.OperationKey }).IsUnique();
        refund.HasIndex(x => x.PaymentAttemptId).IsUnique().HasFilter("\"Unresolved\" = TRUE");
        refund.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        refund.HasOne<Account>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        refund.ToTable(t => t.HasCheckConstraint("CK_PaymentRefunds_Amount", "\"AmountMinor\" > 0 AND length(trim(\"Reason\")) BETWEEN 1 AND 500"));

        var dispute = model.Entity<PaymentDispute>();
        Scoped(dispute);
        dispute.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        dispute.Property(x => x.ExternalReference).HasMaxLength(256);
        dispute.HasIndex(x => new { x.Provider, x.ProviderAccountId, x.Environment, x.ExternalReference }).IsUnique();
        dispute.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);

        var notification = model.Entity<PaymentEvent>();
        Scoped(notification);
        notification.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        notification.Property(x => x.ExternalReference).HasMaxLength(256);
        notification.Property(x => x.Type).HasMaxLength(128);
        notification.Property(x => x.ResourceReference).HasMaxLength(256);
        notification.Property(x => x.ResourceKind).HasMaxLength(32);
        notification.Property(x => x.PaymentReference).HasMaxLength(256);
        notification.Property(x => x.FailureCode).HasMaxLength(64);
        notification.HasIndex(x => new { x.Provider, x.ProviderAccountId, x.Environment, x.ExternalReference }).IsUnique();
        notification.HasIndex(x => new { x.State, x.NextAttemptAt });
        notification.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        notification.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void Base<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.HasKey("Id");
        builder.Property<Guid>("Id").ValueGeneratedNever();
        builder.Property<long>("Version").IsConcurrencyToken();
        builder.ToTable(t => t.HasCheckConstraint($"CK_{typeof(T).Name}_Version", "\"Version\" > 0"));
    }

    private static void Scoped<T>(EntityTypeBuilder<T> builder) where T : class
    {
        Base(builder);
        builder.Property<string>("Provider").HasMaxLength(32);
        builder.Property<string>("ProviderAccountId").HasMaxLength(128);
        builder.Property<string>("Environment").HasMaxLength(32);
    }
}
