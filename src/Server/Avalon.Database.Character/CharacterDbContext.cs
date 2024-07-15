using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Avalon.Database.Character;

public class CharacterDbContext : DbContext
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _connectionString;
    
    public DbSet<Domain.Characters.Character> Characters { get; set; } = null!;
    public DbSet<CharacterStats> CharacterStats { get; set; } = null!;
    public DbSet<CharacterInventory> CharacterInventory { get; set; } = null!;
    public DbSet<CharacterSpell> CharacterSpells { get; set; } = null!;

    public CharacterDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.Characters!.ConnectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseLoggerFactory(_loggerFactory)
            .EnableSensitiveDataLogging();
        
        optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString), mysql =>
        {
            // because MySQL does not support schemas: https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/1100
            mysql.SchemaBehavior(MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}.{table}");
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<Domain.Characters.Character>());
        Configure(modelBuilder.Entity<CharacterStats>());
        Configure(modelBuilder.Entity<CharacterInventory>());
        Configure(modelBuilder.Entity<CharacterSpell>());
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
        
        builder
            .ToTable(t => t.HasCheckConstraint(
                "CK_CharacterStats_MinValues", 
                "MaxHealth >= 0 AND Stamina >= 0 AND Strength >= 0 AND Agility >= 0 AND Intellect >= 0"
                ));
        
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
        
        builder.HasIndex(b => b.CharacterId);
    }
    
    private static void Configure(EntityTypeBuilder<CharacterSpell> builder)
    {
        builder.HasKey(b => new { b.CharacterId, b.SpellId });
        
        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            ).IsRequired();
        
        builder.Property(b => b.SpellId)
            .HasConversion(
                v => v.Value,
                v => new SpellId(v)
            );
        
        builder.HasOne(e => e.Character)
            .WithMany()
            .HasForeignKey(e => e.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasIndex(b => b.CharacterId);
    }
}
