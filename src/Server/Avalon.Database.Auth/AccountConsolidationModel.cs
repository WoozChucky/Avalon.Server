using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Avalon.Database.Auth;

internal static class AccountConsolidationModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Account>().Property(a => a.GameplayConsolidationId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        var operation = model.Entity<AccountConsolidation>();
        operation.HasKey(o => o.Id);
        operation.Property(o => o.Id).ValueGeneratedNever();
        operation.Property(o => o.SourceAccountId).HasConversion(v => v.Value, v => new AccountId(v));
        operation.Property(o => o.TargetAccountId).HasConversion(v => v.Value, v => new AccountId(v));
        operation.Property(o => o.Provider).HasMaxLength(32);
        operation.Property(o => o.ProviderSubject).HasMaxLength(128);
        operation.HasIndex(o => o.SourceAccountId);
        operation.HasIndex(o => o.TargetAccountId);
        operation.HasMany(o => o.Worlds).WithOne().HasForeignKey(w => w.ConsolidationId).OnDelete(DeleteBehavior.Cascade);
        var world = model.Entity<AccountConsolidationWorld>();
        world.HasKey(w => new { w.ConsolidationId, w.WorldId });
        // Root ids and the world manifest remain as audit evidence after retirement or removal.
    }
}
