using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Avalon.World.Database;

public class WorldDbContext : DbContext
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _connectionString;
    
    public DbSet<CreatureTemplate> CreatureTemplates { get; set; } = null!;
    public DbSet<ItemTemplate> ItemTemplates { get; set; } = null!;
    public DbSet<MapTemplate> MapTemplates { get; set; } = null!;
    public DbSet<QuestReward> QuestRewards { get; set; } = null!;
    public DbSet<QuestRewardTemplate> QuestRewardTemplates { get; set; } = null!;
    public DbSet<QuestTemplate> QuestTemplates { get; set; } = null!;

    public WorldDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.World!.ConnectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseLoggerFactory(_loggerFactory);
        
        optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString), mysql =>
        {
            // because MySQL does not support schemas: https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/1100
            mysql.SchemaBehavior(MySqlSchemaBehavior.Translate, (schema, table) => $"{schema}.{table}");
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<CreatureTemplate>());
        Configure(modelBuilder.Entity<ItemTemplate>());
        Configure(modelBuilder.Entity<MapTemplate>());
        Configure(modelBuilder.Entity<QuestReward>());
        Configure(modelBuilder.Entity<QuestRewardTemplate>());
        Configure(modelBuilder.Entity<QuestTemplate>());
    }

    private static void Configure(EntityTypeBuilder<QuestReward> builder)
    {
        builder.HasKey(b => new { b.QuestId, b.RewardId });
        
        builder.HasOne(b => b.Quest)
            .WithMany(q => q.Rewards)
            .HasForeignKey(b => b.QuestId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(b => b.Reward)
            .WithMany()
            .HasForeignKey(b => b.RewardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
    
    private static void Configure(EntityTypeBuilder<QuestRewardTemplate> builder)
    {
    }
    
    private static void Configure(EntityTypeBuilder<QuestTemplate> builder)
    {
        builder.HasKey(b => b.Id);
        
        builder.HasMany(q => q.Rewards)
            .WithOne(r => r.Quest)
            .HasForeignKey(r => r.QuestId)
            .OnDelete(DeleteBehavior.Cascade); // Adjust delete behavior as needed
    }

    private static void Configure(EntityTypeBuilder<CreatureTemplate> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new CreatureTemplateId(v)
            )
            .IsRequired();
        
        builder.HasData([
            new CreatureTemplate
            {
                Id = 1,
                Name = "Uriel",
                SubName = string.Empty,
                IconName = string.Empty,
                MinLevel = 1,
                MaxLevel = 1,
                SpeedWalk = 30,
                SpeedRun = 35,
                SpeedSwim = 18,
                Rank = 0,
                Family = CreatureFamily.None,
                Type = CreatureType.Humanoid,
                Exp = 0,
                LootId = 0,
                MinGold = 0,
                MaxGold = 0,
                AIName = string.Empty,
                MovementType = 0,
                DetectionRange = 20,
                MovementId = 0,
                ScriptName = "UrielTownPatrolScript",
                HealthModifier = 1,
                ManaModifier = 1,
                ArmorModifier = 1,
                ExperienceModifier = 1,
                RegenHealth = 1,
                DmgSchool = 0,
                DamageModifier = 1,
                BaseAttackTime = 1,
                RangeAttackTime = 0
            },
            new CreatureTemplate
            {
                Id = 2,
                Name = "Borin Stoutbeard",
                SubName = string.Empty,
                IconName = string.Empty,
                MinLevel = 1,
                MaxLevel = 1,
                SpeedWalk = 30,
                SpeedRun = 35,
                SpeedSwim = 18,
                Rank = 0,
                Family = CreatureFamily.None,
                Type = CreatureType.Humanoid,
                Exp = 0,
                LootId = 0,
                MinGold = 0,
                MaxGold = 0,
                AIName = string.Empty,
                MovementType = 0,
                DetectionRange = 20,
                MovementId = 0,
                ScriptName = string.Empty,
                HealthModifier = 1,
                ManaModifier = 1,
                ArmorModifier = 1,
                ExperienceModifier = 1,
                RegenHealth = 1,
                DmgSchool = 0,
                DamageModifier = 1,
                BaseAttackTime = 1,
                RangeAttackTime = 0
            },
            new CreatureTemplate
            {
                Id = 3,
                Name = "Innkeeper",
                SubName = string.Empty,
                IconName = string.Empty,
                MinLevel = 1,
                MaxLevel = 1,
                SpeedWalk = 30,
                SpeedRun = 35,
                SpeedSwim = 18,
                Rank = 0,
                Family = CreatureFamily.None,
                Type = CreatureType.Humanoid,
                Exp = 0,
                LootId = 0,
                MinGold = 0,
                MaxGold = 0,
                AIName = string.Empty,
                MovementType = 0,
                DetectionRange = 20,
                MovementId = 0,
                ScriptName = string.Empty,
                HealthModifier = 1,
                ManaModifier = 1,
                ArmorModifier = 1,
                ExperienceModifier = 1,
                RegenHealth = 1,
                DmgSchool = 0,
                DamageModifier = 1,
                BaseAttackTime = 1,
                RangeAttackTime = 0
            }
        ]);
    }

    private static void Configure(EntityTypeBuilder<ItemTemplate> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new ItemTemplateId(v)
            )
            .IsRequired();
    }
    
    private static void Configure(EntityTypeBuilder<MapTemplate> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new MapTemplateId(v)
            )
            .IsRequired();
        
        builder.HasData([
            new MapTemplate
            {
                Id = 1,
                Name = "world.bin",
                Description = "Glimmerdell",
                Directory = "Maps/",
                InstanceType = MapInstanceType.OpenWorld,
                PvP = false,
                MinLevel = 1,
                MaxLevel = 60,
                AreaTableId = 0,
                LoadingScreenId = 0,
                CorpseX = 0,
                CorpseY = 0,
                MaxPlayers = 32
            },
        ]);
    }
}
