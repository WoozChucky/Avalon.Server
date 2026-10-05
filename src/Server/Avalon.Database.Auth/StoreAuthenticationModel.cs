using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth;

internal static class StoreAuthenticationModel
{
    public static void Configure(ModelBuilder model)
    {
        var creations = model.Entity<StoreAccountCreation>();
        creations.HasKey(x => x.Id);
        creations.Property(x => x.Id).ValueGeneratedNever();
        creations.Property(x => x.AccountId).HasConversion(v => v.Value, v => new AccountId(v));
        creations.Property(x => x.ProviderSubject).HasMaxLength(128);
        creations.Property(x => x.Provider).HasMaxLength(32);
        // An audit/idempotency receipt, not a dependent account row: deletion must not erase it.
        creations.HasIndex(x => x.AccountId);

        var identities = model.Entity<ExternalIdentity>();
        identities.HasKey(x => x.Id);
        identities.Property(x => x.Id).ValueGeneratedNever();
        identities.Property(x => x.AccountId).HasConversion(v => v.Value, v => new AccountId(v));
        identities.Property(x => x.Provider).HasMaxLength(32);
        identities.Property(x => x.ProviderSubject).HasMaxLength(128);
        identities.HasIndex(x => new { x.Provider, x.ProviderSubject }).IsUnique();
        identities.HasIndex(x => new { x.AccountId, x.Provider }).IsUnique();
        identities.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);

        var licenses = model.Entity<LicenseObservation>();
        licenses.HasKey(x => x.Id);
        licenses.HasOne<GameLicense>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
        licenses.Property(x => x.Id).ValueGeneratedNever();
        licenses.Property(x => x.AccountId).HasConversion(v => v.Value, v => new AccountId(v));
        licenses.Property(x => x.Provider).HasMaxLength(32);
        licenses.Property(x => x.ProviderSubject).HasMaxLength(128);
        licenses.Property(x => x.Environment).HasMaxLength(32);
        licenses.Property(x => x.Product).HasMaxLength(128);
        licenses.Property(x => x.ProviderProductId).HasMaxLength(128);
        licenses.Property(x => x.ProviderOwnerSubject).HasMaxLength(128);
        licenses.HasIndex(x => new { x.AccountId, x.Provider, x.ProviderSubject, x.Environment, x.Product, x.ProviderProductId, x.ObservedAt });
        licenses.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);

        var sessions = model.Entity<GameSession>();
        sessions.HasKey(x => x.AccountId);
        sessions.Property(x => x.AccountId).HasConversion(v => v.Value, v => new AccountId(v)).ValueGeneratedNever();
        sessions.Property(x => x.FencingToken).IsConcurrencyToken();
        sessions.Property(x => x.ServerId).HasMaxLength(128);
        sessions.Property(x => x.PreviousServerId).HasMaxLength(128);
        sessions.Property(x => x.Environment).HasMaxLength(32);
        sessions.HasIndex(x => x.GameSessionId).IsUnique();
        sessions.HasOne<Account>().WithOne().HasForeignKey<GameSession>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        sessions.ToTable(t => t.HasCheckConstraint("CK_GameSessions_FencingToken", "\"FencingToken\" > 0"));
        model.Entity<Account>().ToTable(t => t.HasCheckConstraint("CK_Accounts_SessionEpoch", "\"SessionEpoch\" >= 0"));
    }
}
