using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth;

internal static class GameLicenseModel
{
    public static void Configure(ModelBuilder model)
    {
        var license = model.Entity<GameLicense>();
        license.HasKey(x => x.Id);
        license.Property(x => x.Id).ValueGeneratedNever();
        license.Property(x => x.AccountId).HasConversion(x => x.Value, x => new AccountId(x));
        license.Property(x => x.Product).HasMaxLength(128);
        license.Property(x => x.Provider).HasMaxLength(32);
        license.Property(x => x.Environment).HasMaxLength(32);
        license.Property(x => x.ProviderProductId).HasMaxLength(128);
        license.Property(x => x.ProviderSubject).HasMaxLength(128);
        license.Property(x => x.LicenseReference).HasMaxLength(256);
        license.Property(x => x.AuthorityKind).HasConversion<string>().HasMaxLength(32);
        license.Property(x => x.AuthorityRevision).IsConcurrencyToken();
        license.Property(x => x.LastObservedAt).IsConcurrencyToken();
        license.HasIndex(x => new { x.Provider, x.Environment, x.LicenseReference }).IsUnique();
        license.HasIndex(x => new { x.AccountId, x.Product, x.Provider, x.Environment });
        license.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        license.ToTable(t =>
        {
            t.HasCheckConstraint("CK_GameLicenses_Revision", "\"AuthorityRevision\" > 0 AND \"AccountId\" > 0");
            t.HasCheckConstraint("CK_GameLicenses_Reference", "length(trim(\"LicenseReference\")) > 0 AND \"LicenseReference\" = trim(\"LicenseReference\")");
            t.HasCheckConstraint("CK_GameLicenses_Interval", "(\"ExpiresAt\" IS NULL OR \"ExpiresAt\" > \"GrantedAt\") AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"GrantedAt\")");
        });
    }
}
