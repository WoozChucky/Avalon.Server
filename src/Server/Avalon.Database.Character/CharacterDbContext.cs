using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
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

public class CharacterDbContext : DbContext
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

    public DbSet<Domain.Characters.Character> Characters { get; set; } = null!;
    public DbSet<CharacterStats> CharacterStats { get; set; } = null!;
    public DbSet<CharacterInventory> CharacterInventory { get; set; } = null!;
    public DbSet<CharacterAbility> CharacterAbilities { get; set; } = null!;
    public DbSet<ItemInstance> ItemInstances { get; set; } = null!;

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
        Configure(modelBuilder.Entity<Domain.Characters.Character>());
        Configure(modelBuilder.Entity<CharacterStats>());
        Configure(modelBuilder.Entity<CharacterInventory>());
        Configure(modelBuilder.Entity<CharacterAbility>());
        Configure(modelBuilder.Entity<ItemInstance>());
    }

    private static void Configure(EntityTypeBuilder<Domain.Characters.Character> builder)
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
    }

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
        builder.HasKey(b => new {b.CharacterId, b.Container, b.Slot});

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
        builder.HasKey(b => new {b.CharacterId, b.AbilityId});

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
}
