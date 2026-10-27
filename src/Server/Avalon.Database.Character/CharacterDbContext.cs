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
    public DbSet<ItemInstanceModifier> ItemInstanceModifiers { get; set; } = null!;
    public DbSet<ItemInstanceModifierValue> ItemInstanceModifierValues { get; set; } = null!;
    public DbSet<ItemInstanceBaseStat> ItemInstanceBaseStats { get; set; } = null!;
    public DbSet<CharacterQuest> CharacterQuests { get; set; } = null!;
    public DbSet<CharacterQuestObjective> CharacterQuestObjectives { get; set; } = null!;
    public DbSet<CharacterCompletedQuest> CharacterCompletedQuests { get; set; } = null!;
    public DbSet<CharacterIgnore> CharacterIgnores { get; set; } = null!;
    public DbSet<CharacterTalent> CharacterTalents { get; set; } = null!;
    public DbSet<CharacterAura> CharacterAuras { get; set; } = null!;
    public DbSet<CharacterConsolidationReceipt> CharacterConsolidationReceipts { get; set; } = null!;
    public DbSet<TradeRecord> TradeRecords { get; set; } = null!;
    public DbSet<TradeRecordItem> TradeRecordItems { get; set; } = null!;

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
        EntityTypeBuilder<AccountGameplayFence> fence = modelBuilder.Entity<AccountGameplayFence>();
        fence.HasKey(f => f.AccountId);
        fence.Property(f => f.AccountId).HasConversion(v => v.Value, v => new AccountId(v)).ValueGeneratedNever();
        Configure(modelBuilder.Entity<CharacterInventory>());
        Configure(modelBuilder.Entity<CharacterAbility>());
        Configure(modelBuilder.Entity<ItemInstance>());
        Configure(modelBuilder.Entity<ItemInstanceModifier>());
        Configure(modelBuilder.Entity<ItemInstanceModifierValue>());
        Configure(modelBuilder.Entity<ItemInstanceBaseStat>());
        Configure(modelBuilder.Entity<CharacterQuest>());
        Configure(modelBuilder.Entity<CharacterQuestObjective>());
        Configure(modelBuilder.Entity<CharacterCompletedQuest>());
        Configure(modelBuilder.Entity<CharacterIgnore>());
        Configure(modelBuilder.Entity<CharacterTalent>());
        Configure(modelBuilder.Entity<CharacterAura>());
        EntityTypeBuilder<CharacterConsolidationReceipt> receipt = modelBuilder.Entity<CharacterConsolidationReceipt>();
        receipt.HasKey(r => r.Id);
        receipt.Property(r => r.Id).ValueGeneratedNever();
        receipt.Property(r => r.SourceAccountId).HasConversion(v => v.Value, v => new AccountId(v));
        receipt.Property(r => r.TargetAccountId).HasConversion(v => v.Value, v => new AccountId(v));
        Configure(modelBuilder.Entity<TradeRecord>());
        Configure(modelBuilder.Entity<TradeRecordItem>());
        Configure(modelBuilder.Entity<TradeRecordItemModifier>());
        Configure(modelBuilder.Entity<TradeRecordItemModifierValue>());
        Configure(modelBuilder.Entity<TradeRecordItemBaseStat>());
    }

    /// <summary>
    /// The trade history (#1175). Permanent: no foreign key to the characters, the items or the templates, so a record
    /// outlives a deleted character, an item used up and a template a reload removed. Child rows rather than a JSON or
    /// array column, so the history is queryable by character and by item instance with plain indexes on both Postgres
    /// and SQLite.
    /// </summary>
    private static void Configure(EntityTypeBuilder<TradeRecord> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.FirstCharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        builder.Property(r => r.SecondCharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        builder.HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.TradeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.FirstCharacterId);
        builder.HasIndex(r => r.SecondCharacterId);
    }

    private static void Configure(EntityTypeBuilder<TradeRecordItem> builder)
    {
        builder.HasKey(i => new { i.TradeId, i.ItemInstanceId });
        builder.Property(i => i.ItemInstanceId).HasConversion(v => v.Value, v => new ItemInstanceId(v)).IsRequired();
        builder.Property(i => i.GiverCharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        builder.Property(i => i.TemplateId).HasConversion(v => v.Value, v => new ItemTemplateId(v)).IsRequired();
        builder.Property(i => i.ItemLevel).HasColumnType("smallint");
        builder.Property(i => i.RareName).HasMaxLength(32);
        builder.Property(i => i.Instability).HasColumnType("smallint");
        // As on the item itself: a rare copy, and only a rare one, carries its two words, a common copy is identified, and
        // a copy is Tainted exactly when its instability reached the brick (#1180).
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_TradeRecordItems_RareName", "(\"Rarity\" = 2) = (\"RareName\" IS NOT NULL)");
            t.HasCheckConstraint("CK_TradeRecordItems_Identified", "\"Rarity\" <> 0 OR \"Identified\"");
            t.HasCheckConstraint("CK_TradeRecordItems_Instability", InstabilityCheckSql);
            t.HasCheckConstraint("CK_TradeRecordItems_Tainted", TaintedCheckSql);
        });
        builder.HasIndex(i => i.ItemInstanceId);
        // The record's own copy of the item's base stats and modifiers (#1179), written with it as one graph.
        builder.HasMany(i => i.BaseStats)
            .WithOne()
            .HasForeignKey(s => new { s.TradeId, s.ItemInstanceId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(i => i.Modifiers)
            .WithOne()
            .HasForeignKey(m => new { m.TradeId, m.ItemInstanceId })
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>A traded item's base stats as they moved (#1179), checked as the item's own are.</summary>
    private static void Configure(EntityTypeBuilder<TradeRecordItemBaseStat> builder)
    {
        builder.ToTable("TradeRecordItemBaseStats", t =>
        {
            t.HasCheckConstraint("CK_TradeRecordItemBaseStats_Stat", "\"Stat\" BETWEEN 0 AND 6");
            t.HasCheckConstraint("CK_TradeRecordItemBaseStats_Value", "\"Value\" >= 0");
        });
        builder.HasKey(s => new { s.TradeId, s.ItemInstanceId, s.Stat });
        builder.Property(s => s.ItemInstanceId).HasConversion(v => v.Value, v => new ItemInstanceId(v)).IsRequired();
        builder.Property(s => s.Stat).HasColumnType("smallint");
    }

    private static void Configure(EntityTypeBuilder<TradeRecordItemModifier> builder)
    {
        // A sealed affix's progress as it was (#1180), under the item's own rule. No one-seal index: the record is history.
        builder.ToTable("TradeRecordItemModifiers", t => t.HasCheckConstraint("CK_TradeRecordItemModifiers_SealProgress",
            "\"SealProgress\" IS NULL OR \"SealProgress\" >= 0"));
        // Keyed by the modifier's own id, not its place: a modifier keeps its id when its item's modifiers are reordered.
        builder.HasKey(m => new { m.TradeId, m.ItemInstanceModifierId });
        builder.Property(m => m.ItemInstanceId).HasConversion(v => v.Value, v => new ItemInstanceId(v)).IsRequired();
        builder.HasMany(m => m.Values)
            .WithOne()
            .HasForeignKey(v => new { v.TradeId, v.ItemInstanceModifierId })
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<TradeRecordItemModifierValue> builder)
    {
        builder.ToTable("TradeRecordItemModifierValues");
        builder.HasKey(v => new { v.TradeId, v.ItemInstanceModifierId, v.Index });
        builder.Property(v => v.Stat).HasColumnType("smallint");
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

        // The name itself follows the rule every writer applies (#944): 3 to 12 ASCII letters, CharacterName.Pattern.
        // Whisper, invite and ignore by name refuse anything else (#936), so a row outside it could never be named.
        // Postgres matches the pattern under COLLATE "C", so the letter ranges are ASCII whatever the database's
        // locale, and its $ matches only at the very end (no trailing newline). SQLite has no regular expressions of
        // its own, so it checks the length and that no character falls outside A-Z and a-z (GLOB is case-sensitive).
        builder.ToTable(t => t.HasCheckConstraint(NameRuleConstraint,
            postgres ? NameRuleCheckSqlPostgres : NameRuleCheckSqlSqlite));

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

    /// <summary>An item's hidden instability (#1180) is never negative, on the item and on its trade record.</summary>
    private const string InstabilityCheckSql = "\"Instability\" >= 0";

    /// <summary>
    /// An item is Tainted exactly when its instability reached the brick (#1180), on the item and on its trade record. 100
    /// is <see cref="ItemInstability.BrickAt" />, which a constant string cannot interpolate.
    /// </summary>
    private const string TaintedCheckSql = "\"Tainted\" = (\"Instability\" >= 100)";

    /// <summary>The check constraint that holds <c>Characters.Name</c> to the name rule, <see cref="CharacterName" /> (#944).</summary>
    public const string NameRuleConstraint = "CK_Characters_NameRule";

    /// <summary>The name rule's check on Postgres: <see cref="CharacterName.Pattern" /> under the "C" collation.</summary>
    public const string NameRuleCheckSqlPostgres = "\"Name\" COLLATE \"C\" ~ '" + CharacterName.Pattern + "'";

    /// <summary>The name rule's check on SQLite (the tests): the length, and ASCII letters only.</summary>
    public const string NameRuleCheckSqlSqlite = "length(\"Name\") BETWEEN 3 AND 12 AND \"Name\" NOT GLOB '*[^A-Za-z]*'";

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

        // Facts of every copy (#1179), like its count and flags. A rare copy, and only a rare one (Rarity 2), carries the two
        // words its roll gave it, and a common copy (Rarity 0) is always identified. Its hidden instability (#1180) is never
        // negative, and the copy is Tainted exactly when that reached the brick (ItemInstability.BrickAt).
        builder.Property(b => b.ItemLevel).HasColumnType("smallint");
        builder.Property(b => b.RareName).HasMaxLength(32);
        builder.Property(b => b.Instability).HasColumnType("smallint");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ItemInstances_Rarity", "\"Rarity\" BETWEEN 0 AND 3");
            t.HasCheckConstraint("CK_ItemInstances_ItemLevel", "\"ItemLevel\" > 0");
            t.HasCheckConstraint("CK_ItemInstances_RareName", "(\"Rarity\" = 2) = (\"RareName\" IS NOT NULL)");
            t.HasCheckConstraint("CK_ItemInstances_Identified", "\"Rarity\" <> 0 OR \"Identified\"");
            t.HasCheckConstraint("CK_ItemInstances_Instability", InstabilityCheckSql);
            t.HasCheckConstraint("CK_ItemInstances_Tainted", TaintedCheckSql);
        });

        // Its modifiers and base stats are its own rows, gone with it.
        builder.HasMany(b => b.Modifiers)
            .WithOne()
            .HasForeignKey(m => m.ItemInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(b => b.BaseStats)
            .WithOne()
            .HasForeignKey(s => s.ItemInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// A copy's rolled base stats (#1179), one row per stat: Stat 0-6 (ItemBaseStat), a value of at least 0. Kept as rolled,
    /// so an edit of the template's ranges never changes a copy.
    /// </summary>
    private static void Configure(EntityTypeBuilder<ItemInstanceBaseStat> builder)
    {
        builder.ToTable("ItemInstanceBaseStats", t =>
        {
            t.HasCheckConstraint("CK_ItemInstanceBaseStats_Stat", "\"Stat\" BETWEEN 0 AND 6");
            t.HasCheckConstraint("CK_ItemInstanceBaseStats_Value", "\"Value\" >= 0");
        });
        builder.HasKey(s => new { s.ItemInstanceId, s.Stat });
        builder.Property(s => s.ItemInstanceId).HasConversion(v => v.Value, v => new ItemInstanceId(v)).IsRequired();
        builder.Property(s => s.Stat).HasColumnType("smallint");
    }

    /// <summary>
    /// An item's modifiers (#1179). Keyed by an id the world server allocates, so a modifier kept across saves is updated
    /// in place. No unique index on the item and the ordinal: a reorder saved row by row would trip it. ModifierId names
    /// a catalog row in the World database, so it has no foreign key, as an item's TemplateId has none. A sealed affix
    /// (#1180) carries its bar's points in SealProgress, never negative, and NULL on every awake row; an item carries at most
    /// one seal, which a partial unique index on the item holds.
    /// </summary>
    private static void Configure(EntityTypeBuilder<ItemInstanceModifier> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.ItemInstanceId).HasConversion(v => v.Value, v => new ItemInstanceId(v)).IsRequired();
        builder.HasMany(m => m.Values)
            .WithOne()
            .HasForeignKey(v => v.ItemInstanceModifierId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.ItemInstanceId);
        builder.HasIndex(m => m.ItemInstanceId, "IX_ItemInstanceModifiers_Sealed")
            .IsUnique()
            .HasFilter("\"SealProgress\" IS NOT NULL");
        builder.ToTable(t => t.HasCheckConstraint("CK_ItemInstanceModifiers_SealProgress",
            "\"SealProgress\" IS NULL OR \"SealProgress\" >= 0"));
    }

    /// <summary>A modifier's rolled numbers. The stat is stored here, so a catalog edit never changes an existing item.</summary>
    private static void Configure(EntityTypeBuilder<ItemInstanceModifierValue> builder)
    {
        builder.HasKey(v => new { v.ItemInstanceModifierId, v.Index });
        builder.Property(v => v.Stat).HasColumnType("smallint");
        builder.ToTable(t => t.HasCheckConstraint("CK_ItemInstanceModifierValues_Range", "\"Value2\" IS NULL OR \"Value2\" >= \"Value\""));
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

    private static void Configure(EntityTypeBuilder<CharacterTalent> builder)
    {
        // A node is held once: the pair is the key. The node is a World DB row, so there is no foreign key to it (as an
        // item's template has none); a row naming a node the tree no longer holds is dropped at select.
        builder.HasKey(b => new { b.CharacterId, b.NodeId });
        builder.Property(b => b.CharacterId).HasConversion(v => v.Value, v => new CharacterId(v)).IsRequired();
        builder.Property(b => b.NodeId).ValueGeneratedNever();

        // Characters are hard-deleted: their talents go with them.
        builder.HasOne<Domain.Characters.Character>()
            .WithMany()
            .HasForeignKey(b => b.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
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
