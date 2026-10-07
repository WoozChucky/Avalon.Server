using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Database.Character;

/// <summary>
/// What dotnet ef builds a <see cref="CharacterDbContext"/> with. Reads Database:Characters:ConnectionString
/// from the environment (Database__Characters__ConnectionString) or this project's user-secrets only,
/// and refuses without it (#523). Commands that only build the model (migrations add,
/// has-pending-model-changes) never connect, so a placeholder pointing nowhere is enough for them.
/// </summary>
public sealed class CharacterDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CharacterDbContext>
{
    public CharacterDbContext CreateDbContext(string[] args) =>
        CreateDbContext(DesignTimeConnectionString.Sources(typeof(CharacterDbContext).Assembly));

    /// <summary>The same, from a configuration the caller built; what the tests use.</summary>
    public CharacterDbContext CreateDbContext(IConfiguration configuration)
    {
        string connectionString = DesignTimeConnectionString.Require(configuration, "Characters");

        ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddConsole();
        });

        return new CharacterDbContext(loggerFactory, Options.Create(new DatabaseConfiguration
        {
            Characters = new DatabaseConnection { ConnectionString = connectionString },
        }));
    }
}

public partial class CharacterDbContext : DbContext
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly string? _connectionString;
    private readonly bool _sensitiveDataLogging;

    public CharacterDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.Characters!.ConnectionString;
        _sensitiveDataLogging = opts.Value.EnableSensitiveDataLogging;
    }

    /// <summary>Configured by the caller. Lets a test point the same model at another provider.</summary>
    public CharacterDbContext(DbContextOptions<CharacterDbContext> options) : base(options)
    {
    }

    public DbSet<AccountGameplayFence> AccountGameplayFences { get; set; } = null!;
    public DbSet<Domain.Characters.Character> Characters { get; set; } = null!;
    public DbSet<CharacterStats> CharacterStats { get; set; } = null!;
    public DbSet<CharacterInventory> CharacterInventory { get; set; } = null!;
    public DbSet<CharacterAbility> CharacterAbilities { get; set; } = null!;
    public DbSet<ItemInstance> ItemInstances { get; set; } = null!;
    public DbSet<CharacterQuest> CharacterQuests { get; set; } = null!;
    public DbSet<CharacterQuestObjective> CharacterQuestObjectives { get; set; } = null!;
    public DbSet<CharacterCompletedQuest> CharacterCompletedQuests { get; set; } = null!;
    public DbSet<CharacterIgnore> CharacterIgnores { get; set; } = null!;
    public DbSet<CharacterAura> CharacterAuras { get; set; } = null!;
    public DbSet<CharacterConsolidationReceipt> CharacterConsolidationReceipts { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        optionsBuilder.UseLoggerFactory(_loggerFactory);

        // Parameter values in the logs, so Development only (#558): see AddAvalonDatabases.
        if (_sensitiveDataLogging)
        {
            optionsBuilder.EnableSensitiveDataLogging();
        }

        optionsBuilder.UseNpgsql(_connectionString!);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Postgres folds with upper("Name" COLLATE "C"), locale-independent; SQLite (the tests) has no "C" collation,
        // and its upper() folds ASCII only already. The model is cached per provider, so each gets its own.
        Configure(modelBuilder.Entity<Domain.Characters.Character>(), Database.IsNpgsql());
        Configure(modelBuilder.Entity<CharacterStats>());
        var fence = modelBuilder.Entity<AccountGameplayFence>();
        fence.HasKey(f => f.AccountId);
        fence.Property(f => f.AccountId).HasConversion(v => v.Value, v => new AccountId(v)).ValueGeneratedNever();
        Configure(modelBuilder.Entity<CharacterInventory>());
        Configure(modelBuilder.Entity<CharacterAbility>());
        Configure(modelBuilder.Entity<ItemInstance>());
        Configure(modelBuilder.Entity<CharacterQuest>());
        Configure(modelBuilder.Entity<CharacterQuestObjective>());
        Configure(modelBuilder.Entity<CharacterCompletedQuest>());
        Configure(modelBuilder.Entity<CharacterIgnore>());
        Configure(modelBuilder.Entity<CharacterAura>());
        var receipt = modelBuilder.Entity<CharacterConsolidationReceipt>();
        receipt.HasKey(r => r.Id);
        receipt.Property(r => r.Id).ValueGeneratedNever();
        receipt.Property(r => r.SourceAccountId).HasConversion(v => v.Value, v => new AccountId(v));
        receipt.Property(r => r.TargetAccountId).HasConversion(v => v.Value, v => new AccountId(v));
    }

    private static void Configure(EntityTypeBuilder<Domain.Characters.Character> builder, bool postgres)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            )
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(b => b.AccountId)
            .HasConversion(
                v => v.Value,
                v => new AccountId(v)
            );

        // One character per name in a world, whatever the case (#757). NameKey is the upper-cased name every lookup
        // uses; the check constraint holds every writer to it. On Postgres the fold is upper(... COLLATE "C"), which
        // upper-cases ASCII letters only whatever the database's locale, exactly as CharacterName.Key does; SQLite's
        // upper() already folds ASCII only.
        builder.HasIndex(b => b.NameKey).IsUnique().HasDatabaseName(NameKeyIndex);
        builder.ToTable(t => t.HasCheckConstraint(NameKeyConstraint,
            postgres ? NameKeyCheckSqlPostgres : "\"NameKey\" = upper(\"Name\")"));

        // A name is written on insert and by a rename only (CharacterRepository.TryRenameAsync, a conditional UPDATE
        // while the character is offline). Every tracked update (the world's saves, the select-time write, the API's
        // admin patch) leaves both columns out, so a world holding an older name in memory can never write it back
        // over a rename, nor fail its saves on the unique index once another character has taken that older name.
        builder.Property(b => b.Name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(b => b.NameKey).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        // Ownership changes use the guarded transfer operation, never a detached gameplay/admin row.
        builder.Property(b => b.AccountId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }

    /// <summary>The unique index on <c>Characters.NameKey</c> (#757).</summary>
    public const string NameKeyIndex = "IX_Characters_NameKey";

    /// <summary>The check constraint that holds <c>Characters.NameKey</c> to the upper-cased name (#757).</summary>
    public const string NameKeyConstraint = "CK_Characters_NameKey";

    /// <summary>The check constraint's expression on Postgres, and the migration's fill.</summary>
    public const string NameKeyCheckSqlPostgres = "\"NameKey\" = upper(\"Name\" COLLATE \"C\")";

    private static void Configure(EntityTypeBuilder<CharacterStats> builder)
    {
        builder.HasKey(b => b.CharacterId);

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            ).IsRequired();

        builder.HasOne(e => e.Character)
            .WithOne()
            .HasForeignKey<CharacterStats>(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.CharacterId);
    }

    private static void Configure(EntityTypeBuilder<CharacterInventory> builder)
    {
        builder.HasKey(b => new { b.CharacterId, b.Container, b.Slot });

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            ).IsRequired();

        builder.Property(b => b.ItemId)
            .HasConversion(
                v => v.Value,
                v => new ItemInstanceId(v)
            );

        builder.HasOne(e => e.Character)
            .WithMany()
            .HasForeignKey(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        // A real foreign key now that the item lives in the same database. Deleting an item takes
        // the slot that held it with it.
        builder.HasOne<ItemInstance>()
            .WithMany()
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.CharacterId);
    }

    private static void Configure(EntityTypeBuilder<CharacterAbility> builder)
    {
        builder.HasKey(b => new { b.CharacterId, b.AbilityId });

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            ).IsRequired();

        builder.Property(b => b.AbilityId)
            .HasConversion(
                v => v.Value,
                v => new AbilityId(v)
            );

        builder.HasOne(e => e.Character)
            .WithMany()
            .HasForeignKey(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.CharacterId);
    }

    private static void Configure(EntityTypeBuilder<ItemInstance> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new ItemInstanceId(v)
            )
            .IsRequired()
            // The world server allocates ids (IItemIdAllocator): by the owner's ruling they are
            // Guid.CreateVersion7() and the database never generates one. Never letting EF invent one
            // means an item that skipped the allocator fails loudly instead of inserting under a fresh Guid.
            .ValueGeneratedNever();

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            );

        // Characters are hard-deleted. The slots cascade from the character row, and the items must
        // too, or every item a deleted character held is left behind with nothing that owns it.
        builder.HasOne<Domain.Characters.Character>()
            .WithMany()
            .HasForeignKey(i => i.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        // A plain reference into the World database: no navigation and no foreign key.
        builder.Property(b => b.TemplateId)
            .HasConversion(
                v => v.Value,
                v => new ItemTemplateId(v)
            );

        builder.HasIndex(b => b.CharacterId);
    }

    private static void Configure(EntityTypeBuilder<CharacterQuest> builder)
    {
        builder.HasKey(b => new { b.CharacterId, b.QuestId });
        builder.Property(b => b.CharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        builder.HasOne<Domain.Characters.Character>()
            .WithMany()
            .HasForeignKey(b => b.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<CharacterQuestObjective> builder)
    {
        builder.HasKey(b => new { b.CharacterId, b.QuestId, b.ObjectiveId });
        builder.Property(b => b.CharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        // Its quest row owns it: an abandon or a turn-in deletes the quest row and the objectives go with it.
        builder.HasOne<CharacterQuest>()
            .WithMany()
            .HasForeignKey(b => new { b.CharacterId, b.QuestId })
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<CharacterCompletedQuest> builder)
    {
        builder.HasKey(b => new { b.CharacterId, b.QuestId });
        builder.Property(b => b.CharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        builder.HasOne<Domain.Characters.Character>()
            .WithMany()
            .HasForeignKey(b => b.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<CharacterIgnore> builder)
    {
        // The pair is the key, so a character is on one list at most once.
        builder.HasKey(b => new { b.CharacterId, b.IgnoredCharacterId });
        builder.Property(b => b.CharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        builder.Property(b => b.IgnoredCharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();

        // Characters are hard-deleted: the owner's list goes with it, and so does every entry naming it.
        builder.HasOne<Domain.Characters.Character>()
            .WithMany()
            .HasForeignKey(b => b.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Characters.Character>()
            .WithMany()
            .HasForeignKey(b => b.IgnoredCharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.IgnoredCharacterId);
    }

    private static void Configure(EntityTypeBuilder<CharacterAura> builder)
    {
        // Rewritten whole by every save of a character holding one (delete, then insert), so a slot key is enough.
        builder.ToTable("CharacterAuras", t =>
        {
            t.HasCheckConstraint("CK_CharacterAuras_Stacks", "\"Stacks\" >= 1");
            t.HasCheckConstraint("CK_CharacterAuras_TicksLeft", "\"TicksLeft\" >= 0");
        });
        builder.HasKey(b => new { b.CharacterId, b.Slot });
        builder.Property(b => b.CharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();

        // Characters are hard-deleted: their auras go with them.
        builder.HasOne<Domain.Characters.Character>()
            .WithMany()
            .HasForeignKey(b => b.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
