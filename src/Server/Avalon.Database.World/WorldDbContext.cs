using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Database.World;

public sealed class CharacterDbContextFactory : IDesignTimeDbContextFactory<WorldDbContext>
{
    public WorldDbContext CreateDbContext(string[] args)
    {
        // 1) Load a deterministic, design-time configuration
        //    Priority: appsettings.Design.json (repo-local), then environment variables, then appsettings.json if present.
        string basePath = Directory.GetCurrentDirectory(); // root where you run dotnet ef
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.Design.json", true)
            .AddJsonFile("appsettings.json", true) // optional convenience
            .AddEnvironmentVariables()
            .Build();

        // 2) Resolve strongly-typed configuration the same way runtime does
        DatabaseConfiguration dbConfig = new();
        configuration.GetSection("Database").Bind(dbConfig);

        string worldConn = dbConfig.World?.ConnectionString
                           ?? configuration["Database:World:ConnectionString"]
                           ?? throw new InvalidOperationException(
                               "World connection string not found for design time. " +
                               "Provide Database:World:ConnectionString in appsettings.Design.json or Database__World__ConnectionString env var.");

        // 3) Minimal logger factory (keeps parity with your OnConfiguring)
        ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddConsole();
        });

        // 4) Construct the context using public constructor
        //    context reads opts.Value.World.ConnectionString internally.
        IOptions<DatabaseConfiguration> opts = Options.Create(new DatabaseConfiguration
        {
            World = new DatabaseConnection {ConnectionString = worldConn}
        });

        WorldDbContext ctx = new(loggerFactory, opts);

        // 5) Mirror OnConfiguring behavior
        //    Left here just to highlight parity
        //    ctx.Database.SetCommandTimeout(TimeSpan.FromSeconds(60)); // example tweak if you want

        return ctx;
    }
}

public class WorldDbContext : DbContext
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly string? _connectionString;

    public WorldDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.World!.ConnectionString;
    }

    /// <summary>Configured by the caller. Lets a test point the same model at another provider.</summary>
    public WorldDbContext(DbContextOptions<WorldDbContext> options) : base(options)
    {
    }

    public DbSet<CreatureTemplate> CreatureTemplates { get; set; } = null!;
    public DbSet<ItemTemplate> ItemTemplates { get; set; } = null!;
    public DbSet<MapTemplate> MapTemplates { get; set; } = null!;
    public DbSet<QuestReward> QuestRewards { get; set; } = null!;
    public DbSet<QuestRewardTemplate> QuestRewardTemplates { get; set; } = null!;
    public DbSet<QuestTemplate> QuestTemplates { get; set; } = null!;
    public DbSet<ClassLevelStat> ClassLevelStats { get; set; } = null!;
    public DbSet<CharacterLevelExperience> CharacterLevelExperiences { get; set; } = null!;
    public DbSet<CreatureBaseStat> CreatureBaseStats { get; set; } = null!;
    public DbSet<CreatureRarityModifier> CreatureRarityModifiers { get; set; } = null!;
    public DbSet<CharacterCreateInfo> CharacterCreateInfos { get; set; } = null!;
    public DbSet<AbilityTemplate> AbilityTemplates { get; set; } = null!;
    public DbSet<ChunkTemplate> ChunkTemplates { get; set; } = null!;
    public DbSet<ChunkPool> ChunkPools { get; set; } = null!;
    public DbSet<SpawnTable> SpawnTables { get; set; } = null!;
    public DbSet<ProceduralMapConfig> ProceduralMapConfigs { get; set; } = null!;
    public DbSet<MapChunkPlacement> MapChunkPlacements { get; set; } = null!;
    public DbSet<MapCreatureSpawn> MapCreatureSpawns { get; set; } = null!;
    public DbSet<CreaturePath> CreaturePaths { get; set; } = null!;
    public DbSet<CreaturePathPoint> CreaturePathPoints { get; set; } = null!;
    public DbSet<LocalizedText> LocalizedTexts { get; set; } = null!;
    public DbSet<LocalizedTextLocale> LocalizedTextLocales { get; set; } = null!;
    public DbSet<DialogueNode> DialogueNodes { get; set; } = null!;
    public DbSet<DialogueOption> DialogueOptions { get; set; } = null!;
    public DbSet<CharacterClassName> CharacterClassNames { get; set; } = null!;
    public DbSet<LootTable> LootTables { get; set; } = null!;
    public DbSet<LootTableEntry> LootTableEntries { get; set; } = null!;
    public DbSet<VendorStock> VendorStocks { get; set; } = null!;
    public DbSet<VendorStockCost> VendorStockCosts { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        optionsBuilder
            .UseLoggerFactory(_loggerFactory)
            .EnableSensitiveDataLogging();

        optionsBuilder.UseNpgsql(_connectionString!);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Configure(modelBuilder.Entity<CreatureTemplate>());
        Configure(modelBuilder.Entity<ItemTemplate>());
        Configure(modelBuilder.Entity<MapTemplate>());
        Configure(modelBuilder.Entity<QuestReward>());
        Configure(modelBuilder.Entity<QuestRewardTemplate>());
        Configure(modelBuilder.Entity<QuestTemplate>());
        Configure(modelBuilder.Entity<ClassLevelStat>());
        Configure(modelBuilder.Entity<CharacterLevelExperience>());
        Configure(modelBuilder.Entity<CreatureBaseStat>());
        Configure(modelBuilder.Entity<CreatureRarityModifier>());
        Configure(modelBuilder.Entity<CharacterCreateInfo>());
        Configure(modelBuilder.Entity<AbilityTemplate>());
        Configure(modelBuilder.Entity<ChunkTemplate>());
        Configure(modelBuilder.Entity<ChunkPool>());
        Configure(modelBuilder.Entity<SpawnTable>());
        Configure(modelBuilder.Entity<ProceduralMapConfig>());
        Configure(modelBuilder.Entity<MapChunkPlacement>());
        Configure(modelBuilder.Entity<MapCreatureSpawn>());
        Configure(modelBuilder.Entity<CreaturePath>());
        Configure(modelBuilder.Entity<CreaturePathPoint>());
        Configure(modelBuilder.Entity<LocalizedText>());
        Configure(modelBuilder.Entity<LocalizedTextLocale>());
        Configure(modelBuilder.Entity<DialogueNode>());
        Configure(modelBuilder.Entity<DialogueOption>());
        Configure(modelBuilder.Entity<CharacterClassName>());
        Configure(modelBuilder.Entity<LootTable>());
        Configure(modelBuilder.Entity<LootTableEntry>());
        Configure(modelBuilder.Entity<VendorStock>());
        Configure(modelBuilder.Entity<VendorStockCost>());

        modelBuilder.Entity<ChunkPoolMembership>(e =>
        {
            e.HasKey(m => new { m.ChunkPoolId, m.ChunkTemplateId });
            e.Property(m => m.ChunkPoolId)
                .HasConversion(v => v.Value, v => new ChunkPoolId(v));
            e.Property(m => m.ChunkTemplateId)
                .HasConversion(v => v.Value, v => new ChunkTemplateId(v));
            e.HasOne(m => m.Template)
                .WithMany()
                .HasForeignKey(m => m.ChunkTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// Per-level creature base stats. The experience column is calibrated against
    /// <see cref="CharacterLevelExperience" />'s thresholds to hold a roughly constant pace of about 30
    /// Normal kills per level across 1-10, rather than being chosen arbitrarily.
    /// </summary>
    /// <remarks>
    /// Seeded to level 10 while the only banded map reaches 5, so the next zone needs no migration.
    /// These numbers are provisional: they were calibrated against a player who had 100 health and
    /// never grew. Players now derive health and power from <see cref="ClassLevelStat" /> by level plus
    /// worn gear (#434, #463), but combat does not yet read the derived damage or armour, so creature
    /// and character numbers get revisited together in the combat-balance pass (#506).
    /// </remarks>
    private static void Configure(EntityTypeBuilder<CreatureBaseStat> builder)
    {
        builder.HasKey(b => b.Level);

        builder.HasData(
            new CreatureBaseStat { Level = 1,  Health = 40,  DamageMin = 3,  DamageMax = 5,  Experience = 15 },
            new CreatureBaseStat { Level = 2,  Health = 52,  DamageMin = 4,  DamageMax = 7,  Experience = 25 },
            new CreatureBaseStat { Level = 3,  Health = 66,  DamageMin = 5,  DamageMax = 9,  Experience = 40 },
            new CreatureBaseStat { Level = 4,  Health = 84,  DamageMin = 7,  DamageMax = 11, Experience = 60 },
            new CreatureBaseStat { Level = 5,  Health = 106, DamageMin = 9,  DamageMax = 14, Experience = 85 },
            new CreatureBaseStat { Level = 6,  Health = 133, DamageMin = 11, DamageMax = 17, Experience = 115 },
            new CreatureBaseStat { Level = 7,  Health = 166, DamageMin = 14, DamageMax = 21, Experience = 150 },
            new CreatureBaseStat { Level = 8,  Health = 206, DamageMin = 17, DamageMax = 26, Experience = 195 },
            new CreatureBaseStat { Level = 9,  Health = 254, DamageMin = 21, DamageMax = 32, Experience = 250 },
            new CreatureBaseStat { Level = 10, Health = 312, DamageMin = 26, DamageMax = 39, Experience = 320 });
    }

    /// <summary>
    /// What each rarity tier multiplies a creature's base stats by. A table rather than a switch so a
    /// tier is retuned as data alongside the base stats themselves.
    /// </summary>
    private static void Configure(EntityTypeBuilder<CreatureRarityModifier> builder)
    {
        builder.HasKey(b => b.Rarity);

        builder.HasData(
            new CreatureRarityModifier { Rarity = CreatureRarity.Normal, HealthMultiplier = 1.0f, DamageMultiplier = 1.0f, ExperienceMultiplier = 1.0f },
            new CreatureRarityModifier { Rarity = CreatureRarity.Elite,  HealthMultiplier = 2.5f, DamageMultiplier = 1.4f, ExperienceMultiplier = 3.0f },
            new CreatureRarityModifier { Rarity = CreatureRarity.Rare,   HealthMultiplier = 4.0f, DamageMultiplier = 1.7f, ExperienceMultiplier = 6.0f },
            new CreatureRarityModifier { Rarity = CreatureRarity.Boss,   HealthMultiplier = 8.0f, DamageMultiplier = 2.2f, ExperienceMultiplier = 15.0f });
    }

    private static void Configure(EntityTypeBuilder<CharacterLevelExperience> builder)
    {
        builder.HasKey(b => b.Level);

        builder.HasData(
            new CharacterLevelExperience {Level = 1, Experience = 400},
            new CharacterLevelExperience {Level = 2, Experience = 900},
            new CharacterLevelExperience {Level = 3, Experience = 1400},
            new CharacterLevelExperience {Level = 4, Experience = 2100},
            new CharacterLevelExperience {Level = 5, Experience = 2800},
            new CharacterLevelExperience {Level = 6, Experience = 3600},
            new CharacterLevelExperience {Level = 7, Experience = 4500},
            new CharacterLevelExperience {Level = 8, Experience = 5400},
            new CharacterLevelExperience {Level = 9, Experience = 6500},
            new CharacterLevelExperience {Level = 10, Experience = 7600},
            new CharacterLevelExperience {Level = 11, Experience = 8700},
            new CharacterLevelExperience {Level = 12, Experience = 9800},
            new CharacterLevelExperience {Level = 13, Experience = 11000},
            new CharacterLevelExperience {Level = 14, Experience = 12300},
            new CharacterLevelExperience {Level = 15, Experience = 13600}
        );
    }

    private static void Configure(EntityTypeBuilder<CharacterCreateInfo> builder)
    {
        builder.HasKey(b => b.Class);

        ValueConverter<List<ItemTemplateId>, string> itemIdConverter = new(
            v => string.Join(",", v.Select(i => i.Value)),
            v => v.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                .Select(val => new ItemTemplateId(ulong.Parse(val))).ToList());

        ValueComparer<List<ItemTemplateId>> itemIdComparer = new(
            (c1, c2) => c1!.SequenceEqual(c2!),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        builder.Property(b => b.StartingItems)
            .HasConversion(itemIdConverter)
            .Metadata.SetValueComparer(itemIdComparer);

        ValueConverter<List<AbilityId>, string> abilityIdConverter = new(
            v => string.Join(",", v.Select(i => i.Value)),
            v => v.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                .Select(val => new AbilityId(uint.Parse(val))).ToList());

        ValueComparer<List<AbilityId>> abilityIdComparer = new(
            (c1, c2) => c1!.SequenceEqual(c2!),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        builder.Property(b => b.StartingSpells)
            .HasConversion(abilityIdConverter)
            .Metadata.SetValueComparer(abilityIdComparer);

        builder.HasData(new CharacterCreateInfo
        {
            Class = CharacterClass.Warrior,
            Map = 1,
            X = 25,
            Y = 51,
            Z = 25,
            Rotation = 0,
            StartingItems = [1, 2, 3],
            StartingSpells = [1, 2, 100]
        }, new CharacterCreateInfo
        {
            Class = CharacterClass.Wizard,
            Map = 1,
            X = 25,
            Y = 51,
            Z = 25,
            Rotation = 0,
            StartingItems = [1, 2],
            StartingSpells = [2, 101]
        }, new CharacterCreateInfo
        {
            Class = CharacterClass.Hunter,
            Map = 1,
            X = 25,
            Y = 51,
            Z = 25,
            Rotation = 0,
            StartingItems = [1, 2],
            StartingSpells = [102]
        }, new CharacterCreateInfo
        {
            Class = CharacterClass.Healer,
            Map = 1,
            X = 25,
            Y = 51,
            Z = 25,
            Rotation = 0,
            StartingItems = [1, 2],
            StartingSpells = [103]
        });
    }

    private static void Configure(EntityTypeBuilder<QuestReward> builder)
    {
        builder.HasKey(b => new {b.QuestId, b.RewardId});

        builder.HasOne(b => b.Quest)
            .WithMany(q => q.Rewards)
            .HasForeignKey(b => b.QuestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Reward)
            .WithMany()
            .HasForeignKey(b => b.RewardId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<ClassLevelStat> builder)
    {
        builder.HasKey(b => new {b.Class, b.Level});

        builder.HasData(new ClassLevelStat
        {
            Class = CharacterClass.Warrior,
            Level = 1,
            BaseHp = 20,
            BaseMana = 0,
            Stamina = 22,
            Strength = 23,
            Agility = 20,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Warrior,
            Level = 2,
            BaseHp = 40,
            BaseMana = 0,
            Stamina = 24,
            Strength = 25,
            Agility = 21,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Warrior,
            Level = 3,
            BaseHp = 60,
            BaseMana = 0,
            Stamina = 26,
            Strength = 27,
            Agility = 23,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Warrior,
            Level = 4,
            BaseHp = 80,
            BaseMana = 0,
            Stamina = 28,
            Strength = 29,
            Agility = 24,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Warrior,
            Level = 5,
            BaseHp = 100,
            BaseMana = 0,
            Stamina = 30,
            Strength = 31,
            Agility = 26,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Wizard,
            Level = 1,
            BaseHp = 16,
            BaseMana = 20,
            Stamina = 21,
            Strength = 20,
            Agility = 20,
            Intellect = 23
        }, new ClassLevelStat
        {
            Class = CharacterClass.Wizard,
            Level = 2,
            BaseHp = 32,
            BaseMana = 40,
            Stamina = 22,
            Strength = 20,
            Agility = 21,
            Intellect = 25
        }, new ClassLevelStat
        {
            Class = CharacterClass.Wizard,
            Level = 3,
            BaseHp = 48,
            BaseMana = 60,
            Stamina = 23,
            Strength = 20,
            Agility = 22,
            Intellect = 27
        }, new ClassLevelStat
        {
            Class = CharacterClass.Wizard,
            Level = 4,
            BaseHp = 64,
            BaseMana = 80,
            Stamina = 24,
            Strength = 20,
            Agility = 23,
            Intellect = 29
        }, new ClassLevelStat
        {
            Class = CharacterClass.Wizard,
            Level = 5,
            BaseHp = 80,
            BaseMana = 100,
            Stamina = 25,
            Strength = 20,
            Agility = 24,
            Intellect = 31
        }, new ClassLevelStat
        {
            Class = CharacterClass.Hunter,
            Level = 1,
            BaseHp = 18,
            BaseMana = 10,
            Stamina = 20,
            Strength = 21,
            Agility = 23,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Hunter,
            Level = 2,
            BaseHp = 36,
            BaseMana = 20,
            Stamina = 21,
            Strength = 22,
            Agility = 25,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Hunter,
            Level = 3,
            BaseHp = 54,
            BaseMana = 30,
            Stamina = 22,
            Strength = 23,
            Agility = 27,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Hunter,
            Level = 4,
            BaseHp = 72,
            BaseMana = 40,
            Stamina = 23,
            Strength = 24,
            Agility = 29,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Hunter,
            Level = 5,
            BaseHp = 90,
            BaseMana = 50,
            Stamina = 24,
            Strength = 25,
            Agility = 31,
            Intellect = 20
        }, new ClassLevelStat
        {
            Class = CharacterClass.Healer,
            Level = 1,
            BaseHp = 18,
            BaseMana = 20,
            Stamina = 20,
            Strength = 20,
            Agility = 21,
            Intellect = 23
        }, new ClassLevelStat
        {
            Class = CharacterClass.Healer,
            Level = 2,
            BaseHp = 36,
            BaseMana = 40,
            Stamina = 21,
            Strength = 20,
            Agility = 22,
            Intellect = 25
        }, new ClassLevelStat
        {
            Class = CharacterClass.Healer,
            Level = 3,
            BaseHp = 54,
            BaseMana = 60,
            Stamina = 22,
            Strength = 20,
            Agility = 23,
            Intellect = 27
        }, new ClassLevelStat
        {
            Class = CharacterClass.Healer,
            Level = 4,
            BaseHp = 72,
            BaseMana = 80,
            Stamina = 23,
            Strength = 20,
            Agility = 24,
            Intellect = 29
        }, new ClassLevelStat
        {
            Class = CharacterClass.Healer,
            Level = 5,
            BaseHp = 90,
            BaseMana = 100,
            Stamina = 24,
            Strength = 20,
            Agility = 25,
            Intellect = 31
        });

        // Levels 6-16 (#463): the experience table ends at 15, so 16 is reachable, and a level with
        // no row leaves a character's stats where they were. Each column continues the step its
        // class takes over levels 1-5, where L is the level:
        //   Warrior: BaseHp 20L, BaseMana 0,   Stamina 20+2L, Strength 21+2L, Agility 20+floor(3(L-1)/2), Intellect 20
        //   Wizard:  BaseHp 16L, BaseMana 20L, Stamina 20+L,  Strength 20,    Agility 19+L,  Intellect 21+2L
        //   Hunter:  BaseHp 18L, BaseMana 10L, Stamina 19+L,  Strength 20+L,  Agility 21+2L, Intellect 20
        //   Healer:  BaseHp 18L, BaseMana 20L, Stamina 19+L,  Strength 20,    Agility 20+L,  Intellect 21+2L
        // Warrior Agility is the one column that does not rise by a whole step: 20, 21, 23, 24, 26
        // alternates +1 and +2, so it keeps alternating. Every formula reproduces levels 1-5 exactly.
        // Provisional, like the creature numbers: revisited in the combat-balance pass (#506).
        builder.HasData(
            new ClassLevelStat { Class = CharacterClass.Warrior, Level =  6, BaseHp = 120, BaseMana =   0, Stamina = 32, Strength = 33, Agility = 27, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level =  7, BaseHp = 140, BaseMana =   0, Stamina = 34, Strength = 35, Agility = 29, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level =  8, BaseHp = 160, BaseMana =   0, Stamina = 36, Strength = 37, Agility = 30, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level =  9, BaseHp = 180, BaseMana =   0, Stamina = 38, Strength = 39, Agility = 32, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level = 10, BaseHp = 200, BaseMana =   0, Stamina = 40, Strength = 41, Agility = 33, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level = 11, BaseHp = 220, BaseMana =   0, Stamina = 42, Strength = 43, Agility = 35, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level = 12, BaseHp = 240, BaseMana =   0, Stamina = 44, Strength = 45, Agility = 36, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level = 13, BaseHp = 260, BaseMana =   0, Stamina = 46, Strength = 47, Agility = 38, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level = 14, BaseHp = 280, BaseMana =   0, Stamina = 48, Strength = 49, Agility = 39, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level = 15, BaseHp = 300, BaseMana =   0, Stamina = 50, Strength = 51, Agility = 41, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Warrior, Level = 16, BaseHp = 320, BaseMana =   0, Stamina = 52, Strength = 53, Agility = 42, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level =  6, BaseHp =  96, BaseMana = 120, Stamina = 26, Strength = 20, Agility = 25, Intellect = 33 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level =  7, BaseHp = 112, BaseMana = 140, Stamina = 27, Strength = 20, Agility = 26, Intellect = 35 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level =  8, BaseHp = 128, BaseMana = 160, Stamina = 28, Strength = 20, Agility = 27, Intellect = 37 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level =  9, BaseHp = 144, BaseMana = 180, Stamina = 29, Strength = 20, Agility = 28, Intellect = 39 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level = 10, BaseHp = 160, BaseMana = 200, Stamina = 30, Strength = 20, Agility = 29, Intellect = 41 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level = 11, BaseHp = 176, BaseMana = 220, Stamina = 31, Strength = 20, Agility = 30, Intellect = 43 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level = 12, BaseHp = 192, BaseMana = 240, Stamina = 32, Strength = 20, Agility = 31, Intellect = 45 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level = 13, BaseHp = 208, BaseMana = 260, Stamina = 33, Strength = 20, Agility = 32, Intellect = 47 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level = 14, BaseHp = 224, BaseMana = 280, Stamina = 34, Strength = 20, Agility = 33, Intellect = 49 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level = 15, BaseHp = 240, BaseMana = 300, Stamina = 35, Strength = 20, Agility = 34, Intellect = 51 },
            new ClassLevelStat { Class = CharacterClass.Wizard,  Level = 16, BaseHp = 256, BaseMana = 320, Stamina = 36, Strength = 20, Agility = 35, Intellect = 53 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level =  6, BaseHp = 108, BaseMana =  60, Stamina = 25, Strength = 26, Agility = 33, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level =  7, BaseHp = 126, BaseMana =  70, Stamina = 26, Strength = 27, Agility = 35, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level =  8, BaseHp = 144, BaseMana =  80, Stamina = 27, Strength = 28, Agility = 37, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level =  9, BaseHp = 162, BaseMana =  90, Stamina = 28, Strength = 29, Agility = 39, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level = 10, BaseHp = 180, BaseMana = 100, Stamina = 29, Strength = 30, Agility = 41, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level = 11, BaseHp = 198, BaseMana = 110, Stamina = 30, Strength = 31, Agility = 43, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level = 12, BaseHp = 216, BaseMana = 120, Stamina = 31, Strength = 32, Agility = 45, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level = 13, BaseHp = 234, BaseMana = 130, Stamina = 32, Strength = 33, Agility = 47, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level = 14, BaseHp = 252, BaseMana = 140, Stamina = 33, Strength = 34, Agility = 49, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level = 15, BaseHp = 270, BaseMana = 150, Stamina = 34, Strength = 35, Agility = 51, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Hunter,  Level = 16, BaseHp = 288, BaseMana = 160, Stamina = 35, Strength = 36, Agility = 53, Intellect = 20 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level =  6, BaseHp = 108, BaseMana = 120, Stamina = 25, Strength = 20, Agility = 26, Intellect = 33 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level =  7, BaseHp = 126, BaseMana = 140, Stamina = 26, Strength = 20, Agility = 27, Intellect = 35 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level =  8, BaseHp = 144, BaseMana = 160, Stamina = 27, Strength = 20, Agility = 28, Intellect = 37 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level =  9, BaseHp = 162, BaseMana = 180, Stamina = 28, Strength = 20, Agility = 29, Intellect = 39 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level = 10, BaseHp = 180, BaseMana = 200, Stamina = 29, Strength = 20, Agility = 30, Intellect = 41 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level = 11, BaseHp = 198, BaseMana = 220, Stamina = 30, Strength = 20, Agility = 31, Intellect = 43 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level = 12, BaseHp = 216, BaseMana = 240, Stamina = 31, Strength = 20, Agility = 32, Intellect = 45 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level = 13, BaseHp = 234, BaseMana = 260, Stamina = 32, Strength = 20, Agility = 33, Intellect = 47 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level = 14, BaseHp = 252, BaseMana = 280, Stamina = 33, Strength = 20, Agility = 34, Intellect = 49 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level = 15, BaseHp = 270, BaseMana = 300, Stamina = 34, Strength = 20, Agility = 35, Intellect = 51 },
            new ClassLevelStat { Class = CharacterClass.Healer,  Level = 16, BaseHp = 288, BaseMana = 320, Stamina = 35, Strength = 20, Agility = 36, Intellect = 53 });
    }

    // ReSharper disable once UnusedParameter.Local
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

        // Issue #460. Null means the creature drops no items. Deleting a table leaves its creatures
        // dropping nothing rather than deleting them.
        builder.Property(b => b.LootTableId)
            .HasConversion(v => v!.Value, v => new LootTableId(v))
            .IsRequired(false);
        builder.HasOne<LootTable>()
            .WithMany()
            .HasForeignKey(b => b.LootTableId)
            .OnDelete(DeleteBehavior.SetNull);

        // Templates 1-3 are the town NPCs, placed on map 1 by the MapCreatureSpawns rows above.
        // They are Invulnerable — a town NPC is never killable — and run TownNpcScript, which
        // stands still and never aggros. Experience is 0 because a creature that cannot die cannot
        // pay out; the 20 they used to carry was left over from their stint as placeholder monsters
        // in the forest spawn table. They are interactive: talking to an NPC opens a dialogue,
        // added for issue #431.
        builder.HasData(new CreatureTemplate
        {
            Id = 1,
            Name = "Uriel",
            SubName = string.Empty,
            IconName = string.Empty,
            MinLevel = 1,
            MaxLevel = 1,
            SpeedWalk = 2.0f,
            SpeedRun = 5.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.None,
            Type = CreatureType.Humanoid,
            Experience = 0,
            LootTableId = null,
            MinGold = 0,
            MaxGold = 0,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 20,
            MovementId = 0,
            ScriptName = "TownNpcScript",
            Invulnerable = true,
            HealthModifier = 1,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 2,
            Name = "Borin Stoutbeard",
            SubName = string.Empty,
            IconName = string.Empty,
            MinLevel = 1,
            MaxLevel = 1,
            SpeedWalk = 2.0f,
            SpeedRun = 5.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.None,
            Type = CreatureType.Humanoid,
            Experience = 0,
            LootTableId = null,
            MinGold = 0,
            MaxGold = 0,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 20,
            MovementId = 0,
            ScriptName = "TownNpcScript",
            Invulnerable = true,
            HealthModifier = 1,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 3,
            Name = "Innkeeper",
            SubName = string.Empty,
            IconName = string.Empty,
            MinLevel = 1,
            MaxLevel = 1,
            SpeedWalk = 2.0f,
            SpeedRun = 5.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.None,
            Type = CreatureType.Humanoid,
            Experience = 0,
            LootTableId = null,
            MinGold = 0,
            MaxGold = 0,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 20,
            MovementId = 0,
            ScriptName = "TownNpcScript",
            Invulnerable = true,
            HealthModifier = 1,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 4,
            Name = "Thornback Boar",
            SubName = "gore-scarred",
            IconName = string.Empty,
            MinLevel = 1,
            MaxLevel = 3,
            SpeedWalk = 2.0f,
            SpeedRun = 4.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.Boar,
            Type = CreatureType.Beast,

            // Null so the experience is derived from the creature's level rather than authored here.
            Experience = null,
            LootTableId = 2,
            MinGold = 3,
            MaxGold = 8,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 12,
            MovementId = 0,
            ScriptName = "AggroDefendScript",
            HealthModifier = 1.1f,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1.0f,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 5,
            Name = "Grey Fen Wolf",
            SubName = "lean and patient",
            IconName = string.Empty,
            MinLevel = 2,
            MaxLevel = 4,
            SpeedWalk = 2.0f,
            SpeedRun = 4.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.Wolf,
            Type = CreatureType.Beast,

            // Null so the experience is derived from the creature's level rather than authored here.
            Experience = null,
            LootTableId = 3,
            MinGold = 4,
            MaxGold = 10,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 18,
            MovementId = 0,
            ScriptName = "AggroDefendScript",
            HealthModifier = 1.0f,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1.1f,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 6,
            Name = "Blightfly Swarmling",
            SubName = "a drone of the bloom",
            IconName = string.Empty,
            MinLevel = 1,
            MaxLevel = 2,
            SpeedWalk = 2.0f,
            SpeedRun = 4.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.None,
            Type = CreatureType.Critter,

            // Null so the experience is derived from the creature's level rather than authored here.
            Experience = null,
            LootTableId = 4,
            MinGold = 1,
            MaxGold = 4,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 8,
            MovementId = 0,
            ScriptName = "AggroDefendScript",
            HealthModifier = 0.6f,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 0.7f,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 7,
            Name = "Husk of the Wold",
            SubName = "what the wold leaves behind",
            IconName = string.Empty,
            MinLevel = 3,
            MaxLevel = 4,
            SpeedWalk = 2.0f,
            SpeedRun = 4.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.None,
            Type = CreatureType.Undead,

            // Null so the experience is derived from the creature's level rather than authored here.
            Experience = null,
            LootTableId = 5,
            MinGold = 6,
            MaxGold = 14,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 14,
            MovementId = 0,
            ScriptName = "AggroDefendScript",
            HealthModifier = 1.3f,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1.0f,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 8,
            Name = "Bramblemaw Alpha",
            SubName = "the pack's black heart",
            IconName = string.Empty,
            MinLevel = 3,
            MaxLevel = 5,
            SpeedWalk = 2.0f,
            SpeedRun = 4.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Elite,
            Family = CreatureFamily.Wolf,
            Type = CreatureType.Beast,

            // Null so the experience is derived from the creature's level rather than authored here.
            Experience = null,
            LootTableId = 6,
            MinGold = 20,
            MaxGold = 45,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 22,
            MovementId = 0,
            ScriptName = "AggroDefendScript",
            HealthModifier = 1.0f,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1.1f,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 9,
            Name = "Old Tuskroot",
            SubName = "older than the rot",
            IconName = string.Empty,
            MinLevel = 4,
            MaxLevel = 5,
            SpeedWalk = 2.0f,
            SpeedRun = 4.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Rare,
            Family = CreatureFamily.Boar,
            Type = CreatureType.Beast,

            // Null so the experience is derived from the creature's level rather than authored here.
            Experience = null,
            LootTableId = 7,
            MinGold = 40,
            MaxGold = 90,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 20,
            MovementId = 0,
            ScriptName = "AggroDefendScript",
            HealthModifier = 1.2f,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1.1f,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        }, new CreatureTemplate
        {
            Id = 10,
            Name = "Mother Bramble",
            SubName = "rooted at the heart of the wold",
            IconName = string.Empty,
            MinLevel = 5,
            MaxLevel = 5,
            SpeedWalk = 2.0f,
            SpeedRun = 4.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Boss,
            Family = CreatureFamily.None,
            Type = CreatureType.Elemental,

            // Null so the experience is derived from the creature's level rather than authored here.
            Experience = null,
            LootTableId = 8,
            MinGold = 150,
            MaxGold = 300,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 26,
            MovementId = 0,
            ScriptName = "AggroDefendScript",
            HealthModifier = 1.0f,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1.0f,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        });

        // Template 11: Marta Ledgerwell, the town's banker (#463). A town NPC like 1-3: invulnerable,
        // TownNpcScript, no loot, experience 0. Her dialogue offers OpenBank, which is what makes her
        // a banker (NpcInteraction.IsBanker).
        builder.HasData(new CreatureTemplate
        {
            Id = 11,
            Name = "Marta Ledgerwell",
            SubName = "Banker",
            IconName = string.Empty,
            MinLevel = 1,
            MaxLevel = 1,
            SpeedWalk = 2.0f,
            SpeedRun = 5.0f,
            SpeedSwim = 1.6f,
            Rarity = CreatureRarity.Normal,
            Family = CreatureFamily.None,
            Type = CreatureType.Humanoid,
            Experience = 0,
            LootTableId = null,
            MinGold = 0,
            MaxGold = 0,
            AIName = string.Empty,
            MovementType = 0,
            DetectionRange = 20,
            MovementId = 0,
            ScriptName = "TownNpcScript",
            Invulnerable = true,
            HealthModifier = 1,
            ManaModifier = 1,
            ArmorModifier = 1,
            ExperienceModifier = 1,
            RegenHealth = 1,
            DmgSchool = 0,
            DamageModifier = 1,
            BaseAttackTime = 1,
            RangeAttackTime = 0
        });

        // Templates 12-14 (#432): the town's vendors. Town NPCs like Marta: invulnerable,
        // TownNpcScript, no loot, experience 0. Each one's dialogue offers OpenShop, which is what
        // makes it a vendor (NpcInteraction.IsVendor); its stock is in VendorStocks.
        builder.HasData(
            TownNpc(12, "Garrick Emberforge", "Weapons Dealer"),
            TownNpc(13, "Hilde Brassbuckle", "Armourer"),
            TownNpc(14, "Tobin Marrowfield", "Trade Goods"));
    }

    /// <summary>A town NPC exactly like Marta (template 11): level 1, unkillable, standing still, dropping nothing.</summary>
    private static CreatureTemplate TownNpc(ulong id, string name, string subName) => new()
    {
        Id = id,
        Name = name,
        SubName = subName,
        IconName = string.Empty,
        MinLevel = 1,
        MaxLevel = 1,
        SpeedWalk = 2.0f,
        SpeedRun = 5.0f,
        SpeedSwim = 1.6f,
        Rarity = CreatureRarity.Normal,
        Family = CreatureFamily.None,
        Type = CreatureType.Humanoid,
        Experience = 0,
        LootTableId = null,
        MinGold = 0,
        MaxGold = 0,
        AIName = string.Empty,
        MovementType = 0,
        DetectionRange = 20,
        MovementId = 0,
        ScriptName = "TownNpcScript",
        Invulnerable = true,
        HealthModifier = 1,
        ManaModifier = 1,
        ArmorModifier = 1,
        ExperienceModifier = 1,
        RegenHealth = 1,
        DmgSchool = 0,
        DamageModifier = 1,
        BaseAttackTime = 1,
        RangeAttackTime = 0
    };

    private static void Configure(EntityTypeBuilder<ItemTemplate> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new ItemTemplateId(v)
            )
            .IsRequired();

        ValueConverter<List<CharacterClass>, string> characterClassConverter = new(
            v => string.Join(',', v.Select(e => e.ToString())),
            v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => (CharacterClass)Enum.Parse(typeof(CharacterClass), e)).ToList());

        ValueComparer<List<CharacterClass>> characterClassComparer = new(
            (c1, c2) => c1!.SequenceEqual(c2!),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        builder
            .Property(e => e.AllowedClasses)
            .HasConversion(characterClassConverter)
            .Metadata.SetValueComparer(characterClassComparer);

        builder.HasData(
            new ItemTemplate
            {
                Id = 1,
                Name = "Health Potion",
                Class = ItemClass.Consumable,
                SubClass = ItemSubClass.Potion,
                Flags = ItemTemplateFlags.NoSell,
                MaxStackSize = 40,
                DisplayId = 1,
                Rarity = ItemRarity.Common,
                BuyPrice = 10,
                SellPrice = 5,
                Slot = null
            },
            new ItemTemplate
            {
                Id = 2,
                Name = "Mana Potion",
                Class = ItemClass.Consumable,
                SubClass = ItemSubClass.Potion,
                Flags = ItemTemplateFlags.NoSell,
                MaxStackSize = 40,
                DisplayId = 2,
                Rarity = ItemRarity.Common,
                BuyPrice = 13,
                SellPrice = 6,
                Slot = null
            },
            new ItemTemplate
            {
                Id = 3,
                Name = "Town Portal Scroll",
                Class = ItemClass.Consumable,
                SubClass = ItemSubClass.Scroll,
                Flags = ItemTemplateFlags.NoSell,
                MaxStackSize = 40,
                DisplayId = 3,
                Rarity = ItemRarity.Common,
                BuyPrice = 100,
                SellPrice = 50,
                Slot = null
            }, new ItemTemplate
            {
                Id = 4,
                Name = "Rusted Sword",
                Class = ItemClass.Weapon,
                SubClass = ItemSubClass.OneHanded,
                Flags = ItemTemplateFlags.NoSell,
                MaxStackSize = 1,
                DisplayId = 4,
                Rarity = ItemRarity.Common,
                BuyPrice = 100,
                SellPrice = 50,
                Slot = ItemSlotType.MainHand,
                AllowedClasses = [CharacterClass.Warrior],
                ItemPower = 2,
                RequiredLevel = 1,
                DamageMin1 = 1,
                DamageMax1 = 3,
                DamageType1 = DamageType.Physical,
                StatType1 = StatType.AttackSpeed,
                StatValue1 = 13 // 1.3 seconds
            },
            // The forest pools (issue #460). Items 5-31 can be sold (owner decision), so they carry no
            // NoSell. Items 5-8: one weapon per class, the group in loot table 9.
            // Item 4 is left as it is, since it may become a starting item; item 7 is the forest's own sword.
            new ItemTemplate
            {
                Id = 5,
                Name = "Thornwood Staff",
                Class = ItemClass.Weapon,
                SubClass = ItemSubClass.TwoHanded,
                Flags = ItemTemplateFlags.None,
                MaxStackSize = 1,
                DisplayId = 5,
                Rarity = ItemRarity.Uncommon,
                BuyPrice = 200,
                SellPrice = 50,
                Slot = ItemSlotType.MainHand,
                AllowedClasses = [CharacterClass.Wizard],
                ItemPower = 3,
                RequiredLevel = 1,
                DamageMin1 = 2,
                DamageMax1 = 5,
                DamageType1 = DamageType.Physical,
                StatType1 = StatType.AttackSpeed,
                StatValue1 = 18, // 1.8 seconds
                StatType2 = StatType.Intellect,
                StatValue2 = 1
            }, new ItemTemplate
            {
                Id = 6,
                Name = "Briarstring Bow",
                Class = ItemClass.Weapon,
                SubClass = ItemSubClass.Ranged,
                Flags = ItemTemplateFlags.None,
                MaxStackSize = 1,
                DisplayId = 6,
                Rarity = ItemRarity.Uncommon,
                BuyPrice = 200,
                SellPrice = 50,
                Slot = ItemSlotType.MainHand,
                AllowedClasses = [CharacterClass.Hunter],
                ItemPower = 3,
                RequiredLevel = 1,
                DamageMin1 = 2,
                DamageMax1 = 4,
                DamageType1 = DamageType.Physical,
                StatType1 = StatType.AttackSpeed,
                StatValue1 = 15, // 1.5 seconds
                StatType2 = StatType.Agility,
                StatValue2 = 1
            }, new ItemTemplate
            {
                Id = 7,
                Name = "Bramblesteel Sword",
                Class = ItemClass.Weapon,
                SubClass = ItemSubClass.OneHanded,
                Flags = ItemTemplateFlags.None,
                MaxStackSize = 1,
                DisplayId = 7,
                Rarity = ItemRarity.Uncommon,
                BuyPrice = 200,
                SellPrice = 50,
                Slot = ItemSlotType.MainHand,
                AllowedClasses = [CharacterClass.Warrior],
                ItemPower = 3,
                RequiredLevel = 1,
                DamageMin1 = 2,
                DamageMax1 = 4,
                DamageType1 = DamageType.Physical,
                StatType1 = StatType.AttackSpeed,
                StatValue1 = 13, // 1.3 seconds
                StatType2 = StatType.Strength,
                StatValue2 = 1
            }, new ItemTemplate
            {
                Id = 8,
                Name = "Rootknot Mace",
                Class = ItemClass.Weapon,
                SubClass = ItemSubClass.OneHanded,
                Flags = ItemTemplateFlags.None,
                MaxStackSize = 1,
                DisplayId = 8,
                Rarity = ItemRarity.Uncommon,
                BuyPrice = 200,
                SellPrice = 50,
                Slot = ItemSlotType.MainHand,
                AllowedClasses = [CharacterClass.Healer],
                ItemPower = 3,
                RequiredLevel = 1,
                DamageMin1 = 2,
                DamageMax1 = 4,
                DamageType1 = DamageType.Physical,
                StatType1 = StatType.AttackSpeed,
                StatValue1 = 15, // 1.5 seconds
                StatType2 = StatType.Intellect,
                StatValue2 = 1
            },
            // Items 9-11: collectible scrolls, the group in loot table 10. Using one does nothing yet.
            new ItemTemplate
            {
                Id = 9,
                Name = "Scroll of Falling Leaves",
                Class = ItemClass.Consumable,
                SubClass = ItemSubClass.Scroll,
                Flags = ItemTemplateFlags.None,
                MaxStackSize = 20,
                DisplayId = 9,
                Rarity = ItemRarity.Common,
                BuyPrice = 20,
                SellPrice = 5,
                Slot = null
            }, new ItemTemplate
            {
                Id = 10,
                Name = "Scroll of the Mossy Hollow",
                Class = ItemClass.Consumable,
                SubClass = ItemSubClass.Scroll,
                Flags = ItemTemplateFlags.None,
                MaxStackSize = 20,
                DisplayId = 10,
                Rarity = ItemRarity.Common,
                BuyPrice = 20,
                SellPrice = 5,
                Slot = null
            }, new ItemTemplate
            {
                Id = 11,
                Name = "Scroll of Whispering Pines",
                Class = ItemClass.Consumable,
                SubClass = ItemSubClass.Scroll,
                Flags = ItemTemplateFlags.None,
                MaxStackSize = 20,
                DisplayId = 11,
                Rarity = ItemRarity.Common,
                BuyPrice = 20,
                SellPrice = 5,
                Slot = null
            });

        // Items 12-31: the forest armour, one set per class in five slots, each piece its own 2 %
        // entry in loot table 11. Every piece carries Armor, so no class is unarmoured when
        // mitigation lands: in each slot cloth (Wizard, Healer) is below leather (Hunter), which is
        // below plate (Warrior).
        builder.HasData(
            ForestArmourPiece(12, "Barkplate Helm", CharacterClass.Warrior, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Strength, 1), (StatType.Armor, 4), (StatType.Stamina, 1)),
            ForestArmourPiece(13, "Barkplate Chestguard", CharacterClass.Warrior, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Strength, 2), (StatType.Armor, 8), (StatType.Stamina, 2)),
            ForestArmourPiece(14, "Barkplate Legguards", CharacterClass.Warrior, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Strength, 2), (StatType.Armor, 6), (StatType.Stamina, 1)),
            ForestArmourPiece(15, "Barkplate Gauntlets", CharacterClass.Warrior, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Strength, 1), (StatType.Armor, 3), (StatType.Stamina, 1)),
            ForestArmourPiece(16, "Barkplate Boots", CharacterClass.Warrior, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Strength, 1), (StatType.Armor, 3), (StatType.Stamina, 1)),
            ForestArmourPiece(17, "Mossweave Hood", CharacterClass.Wizard, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Intellect, 2), (StatType.Armor, 1)),
            ForestArmourPiece(18, "Mossweave Robe", CharacterClass.Wizard, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Intellect, 3), (StatType.Armor, 3)),
            ForestArmourPiece(19, "Mossweave Leggings", CharacterClass.Wizard, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Intellect, 3), (StatType.Armor, 2)),
            ForestArmourPiece(20, "Mossweave Gloves", CharacterClass.Wizard, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Intellect, 1), (StatType.Armor, 1)),
            ForestArmourPiece(21, "Mossweave Slippers", CharacterClass.Wizard, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Intellect, 1), (StatType.Armor, 1)),
            ForestArmourPiece(22, "Fernstalker Cap", CharacterClass.Hunter, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Agility, 2), (StatType.Armor, 2)),
            ForestArmourPiece(23, "Fernstalker Jerkin", CharacterClass.Hunter, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Agility, 3), (StatType.Armor, 5)),
            ForestArmourPiece(24, "Fernstalker Breeches", CharacterClass.Hunter, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Agility, 3), (StatType.Armor, 4)),
            ForestArmourPiece(25, "Fernstalker Grips", CharacterClass.Hunter, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Agility, 1), (StatType.Armor, 2)),
            ForestArmourPiece(26, "Fernstalker Boots", CharacterClass.Hunter, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Agility, 1), (StatType.Armor, 2)),
            ForestArmourPiece(27, "Dewleaf Circlet", CharacterClass.Healer, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
            ForestArmourPiece(28, "Dewleaf Vestments", CharacterClass.Healer, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Intellect, 2), (StatType.Stamina, 2), (StatType.Armor, 3)),
            ForestArmourPiece(29, "Dewleaf Leggings", CharacterClass.Healer, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Intellect, 2), (StatType.Stamina, 1), (StatType.Armor, 2)),
            ForestArmourPiece(30, "Dewleaf Handwraps", CharacterClass.Healer, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
            ForestArmourPiece(31, "Dewleaf Sandals", CharacterClass.Healer, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)));

        // The Common starter tier (#432), what the town vendors sell: one weapon per class and a
        // five-slot set per class. Each stat is the matching forest piece's (items 5-8, 12-31) times
        // 0.6, rounded half away from zero, and at least 1 where the forest piece has it. Weapon
        // damage is scaled the same way. AttackSpeed is a swing time, so it is copied, never scaled.
        // Sold, never dropped: no loot table names these.
        builder.HasData(
            StarterWeapon(32, "Ironwood Sword", CharacterClass.Warrior, ItemSubClass.OneHanded, damageMax: 2, attackSpeed: 13, (StatType.Strength, 1)),
            StarterWeapon(33, "Ash Staff", CharacterClass.Wizard, ItemSubClass.TwoHanded, damageMax: 3, attackSpeed: 18, (StatType.Intellect, 1)),
            StarterWeapon(34, "Hunter's Shortbow", CharacterClass.Hunter, ItemSubClass.Ranged, damageMax: 2, attackSpeed: 15, (StatType.Agility, 1)),
            StarterWeapon(35, "Oak Mace", CharacterClass.Healer, ItemSubClass.OneHanded, damageMax: 2, attackSpeed: 15, (StatType.Intellect, 1)));

        builder.HasData(
            StarterArmourPiece(36, "Ironbound Helm", CharacterClass.Warrior, ItemSubClass.Helmet, ItemSlotType.Head, 60, (StatType.Strength, 1), (StatType.Armor, 2), (StatType.Stamina, 1)),
            StarterArmourPiece(37, "Ironbound Chestguard", CharacterClass.Warrior, ItemSubClass.Chest, ItemSlotType.Chest, 100, (StatType.Strength, 1), (StatType.Armor, 5), (StatType.Stamina, 1)),
            StarterArmourPiece(38, "Ironbound Legguards", CharacterClass.Warrior, ItemSubClass.Legs, ItemSlotType.Legs, 80, (StatType.Strength, 1), (StatType.Armor, 4), (StatType.Stamina, 1)),
            StarterArmourPiece(39, "Ironbound Gauntlets", CharacterClass.Warrior, ItemSubClass.Gloves, ItemSlotType.Hands, 40, (StatType.Strength, 1), (StatType.Armor, 2), (StatType.Stamina, 1)),
            StarterArmourPiece(40, "Ironbound Boots", CharacterClass.Warrior, ItemSubClass.Boots, ItemSlotType.Feet, 40, (StatType.Strength, 1), (StatType.Armor, 2), (StatType.Stamina, 1)),
            StarterArmourPiece(41, "Linen Hood", CharacterClass.Wizard, ItemSubClass.Helmet, ItemSlotType.Head, 60, (StatType.Intellect, 1), (StatType.Armor, 1)),
            StarterArmourPiece(42, "Linen Robe", CharacterClass.Wizard, ItemSubClass.Chest, ItemSlotType.Chest, 100, (StatType.Intellect, 2), (StatType.Armor, 2)),
            StarterArmourPiece(43, "Linen Leggings", CharacterClass.Wizard, ItemSubClass.Legs, ItemSlotType.Legs, 80, (StatType.Intellect, 2), (StatType.Armor, 1)),
            StarterArmourPiece(44, "Linen Gloves", CharacterClass.Wizard, ItemSubClass.Gloves, ItemSlotType.Hands, 40, (StatType.Intellect, 1), (StatType.Armor, 1)),
            StarterArmourPiece(45, "Linen Slippers", CharacterClass.Wizard, ItemSubClass.Boots, ItemSlotType.Feet, 40, (StatType.Intellect, 1), (StatType.Armor, 1)),
            StarterArmourPiece(46, "Hide Cap", CharacterClass.Hunter, ItemSubClass.Helmet, ItemSlotType.Head, 60, (StatType.Agility, 1), (StatType.Armor, 1)),
            StarterArmourPiece(47, "Hide Jerkin", CharacterClass.Hunter, ItemSubClass.Chest, ItemSlotType.Chest, 100, (StatType.Agility, 2), (StatType.Armor, 3)),
            StarterArmourPiece(48, "Hide Breeches", CharacterClass.Hunter, ItemSubClass.Legs, ItemSlotType.Legs, 80, (StatType.Agility, 2), (StatType.Armor, 2)),
            StarterArmourPiece(49, "Hide Grips", CharacterClass.Hunter, ItemSubClass.Gloves, ItemSlotType.Hands, 40, (StatType.Agility, 1), (StatType.Armor, 1)),
            StarterArmourPiece(50, "Hide Boots", CharacterClass.Hunter, ItemSubClass.Boots, ItemSlotType.Feet, 40, (StatType.Agility, 1), (StatType.Armor, 1)),
            StarterArmourPiece(51, "Wool Circlet", CharacterClass.Healer, ItemSubClass.Helmet, ItemSlotType.Head, 60, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
            StarterArmourPiece(52, "Wool Vestments", CharacterClass.Healer, ItemSubClass.Chest, ItemSlotType.Chest, 100, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 2)),
            StarterArmourPiece(53, "Wool Leggings", CharacterClass.Healer, ItemSubClass.Legs, ItemSlotType.Legs, 80, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
            StarterArmourPiece(54, "Wool Handwraps", CharacterClass.Healer, ItemSubClass.Gloves, ItemSlotType.Hands, 40, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
            StarterArmourPiece(55, "Wool Sandals", CharacterClass.Healer, ItemSubClass.Boots, ItemSlotType.Feet, 40, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)));

        // Item 56 (#432): Tobin's Greater Health Potion, sold for gold plus two Health Potions. Its
        // fields are the Health Potion's. ItemTemplate carries no potion effect and nothing uses a
        // potion yet, so there is no effect of its own to seed.
        builder.HasData(new ItemTemplate
        {
            Id = 56,
            Name = "Greater Health Potion",
            Class = ItemClass.Consumable,
            SubClass = ItemSubClass.Potion,
            Flags = ItemTemplateFlags.NoSell,
            MaxStackSize = 40,
            DisplayId = 56,
            Rarity = ItemRarity.Common,
            BuyPrice = 25,
            SellPrice = 12,
            Slot = null
        });
    }

    /// <summary>
    /// One piece of forest armour: Uncommon, level 1, for one class, with up to three stats. Used by
    /// the Configure(EntityTypeBuilder&lt;ItemTemplate&gt;) seed only.
    /// </summary>
    private static ItemTemplate ForestArmourPiece(
        ulong id, string name, CharacterClass characterClass, ItemSubClass subClass, ItemSlotType slot,
        params (StatType Type, uint Value)[] stats) => new()
    {
        Id = id,
        Name = name,
        Class = ItemClass.Armor,
        SubClass = subClass,
        Flags = ItemTemplateFlags.None,
        MaxStackSize = 1,
        DisplayId = (uint)id,
        Rarity = ItemRarity.Uncommon,
        BuyPrice = 200,
        SellPrice = 50,
        Slot = slot,
        AllowedClasses = [characterClass],
        ItemPower = 3,
        RequiredLevel = 1,
        StatType1 = stats.Length > 0 ? stats[0].Type : null,
        StatValue1 = stats.Length > 0 ? stats[0].Value : null,
        StatType2 = stats.Length > 1 ? stats[1].Type : null,
        StatValue2 = stats.Length > 1 ? stats[1].Value : null,
        StatType3 = stats.Length > 2 ? stats[2].Type : null,
        StatValue3 = stats.Length > 2 ? stats[2].Value : null,
    };

    /// <summary>
    /// A Common starter weapon (#432): level 1, for one class, main hand. Minimum damage is always
    /// 1, and the stats are AttackSpeed then one attribute, the forest weapons' shape. Sells for a
    /// quarter of its 120.
    /// </summary>
    private static ItemTemplate StarterWeapon(
        ulong id, string name, CharacterClass characterClass, ItemSubClass subClass, uint damageMax, uint attackSpeed,
        (StatType Type, uint Value) stat) => new()
    {
        Id = id,
        Name = name,
        Class = ItemClass.Weapon,
        SubClass = subClass,
        Flags = ItemTemplateFlags.None,
        MaxStackSize = 1,
        DisplayId = (uint)id,
        Rarity = ItemRarity.Common,
        BuyPrice = 120,
        SellPrice = 30,
        Slot = ItemSlotType.MainHand,
        AllowedClasses = [characterClass],
        ItemPower = 2,
        RequiredLevel = 1,
        DamageMin1 = 1,
        DamageMax1 = damageMax,
        DamageType1 = DamageType.Physical,
        StatType1 = StatType.AttackSpeed,
        StatValue1 = attackSpeed,
        StatType2 = stat.Type,
        StatValue2 = stat.Value,
    };

    /// <summary>
    /// A piece of Common starter armour (#432): level 1, for one class, with up to three stats.
    /// Sells for a quarter of <paramref name="buyPrice" />.
    /// </summary>
    private static ItemTemplate StarterArmourPiece(
        ulong id, string name, CharacterClass characterClass, ItemSubClass subClass, ItemSlotType slot, uint buyPrice,
        params (StatType Type, uint Value)[] stats) => new()
    {
        Id = id,
        Name = name,
        Class = ItemClass.Armor,
        SubClass = subClass,
        Flags = ItemTemplateFlags.None,
        MaxStackSize = 1,
        DisplayId = (uint)id,
        Rarity = ItemRarity.Common,
        BuyPrice = buyPrice,
        SellPrice = buyPrice / 4,
        Slot = slot,
        AllowedClasses = [characterClass],
        ItemPower = 2,
        RequiredLevel = 1,
        StatType1 = stats.Length > 0 ? stats[0].Type : null,
        StatValue1 = stats.Length > 0 ? stats[0].Value : null,
        StatType2 = stats.Length > 1 ? stats[1].Type : null,
        StatValue2 = stats.Length > 1 ? stats[1].Value : null,
        StatType3 = stats.Length > 2 ? stats[2].Type : null,
        StatValue3 = stats.Length > 2 ? stats[2].Value : null,
    };

    private static void Configure(EntityTypeBuilder<MapTemplate> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new MapTemplateId(v)
            )
            .IsRequired();

        // NOTE: ForestDungeon (Id=2) requires a ProceduralMapConfig row + a populated ChunkPool
        // + a SpawnTable to be functional. These will be seeded manually via SQL once real chunks
        // are imported. Until then, attempting to enter ForestDungeon will fail gracefully.
        builder.HasData(new MapTemplate
            {
                Id = 1,
                Name = "world.bin",
                Description = "Glimmerdell",
                MapType = MapType.Town,
                PvP = false,
                MinLevel = 1,
                MaxLevel = 60,
                AreaTableId = 0,
                LoadingScreenId = 0,
                CorpseX = 0,
                CorpseY = 0,
                MaxPlayers = 30,
                DefaultSpawnX = 25f,
                DefaultSpawnY = 51f,
                DefaultSpawnZ = 25f,
                LogoutMapId = null
            },
            new MapTemplate
            {
                Id = 2,
                Name = "ForestDungeon",
                Description = "Forest Dungeon",
                MapType = MapType.Normal,
                PvP = false,
                MinLevel = 1,
                // Re-banded from 10 to 5. The band scales rewards only — a level 6 creature here is legal.
                MaxLevel = 5,
                AreaTableId = 0,
                LoadingScreenId = 0,
                MaxPlayers = 1,
                DefaultSpawnX = 0,
                DefaultSpawnY = 0,
                DefaultSpawnZ = 0,
                CorpseX = null,
                CorpseY = null,
                CorpseZ = null,
                LogoutMapId = 1
            });
    }

    private static void Configure(EntityTypeBuilder<ChunkTemplate> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new ChunkTemplateId(v))
            .IsRequired();

        builder.OwnsMany(b => b.SpawnSlots, s =>
        {
            s.WithOwner().HasForeignKey("ChunkTemplateId");
            s.Property<int>("Id");
            s.HasKey("Id");
        });
        builder.OwnsMany(b => b.PortalSlots, s =>
        {
            s.WithOwner().HasForeignKey("ChunkTemplateId");
            s.Property<int>("Id");
            s.HasKey("Id");
        });

        ValueConverter<string[], string> tagsConverter = new(
            v => string.Join(',', v),
            v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
        ValueComparer<string[]> tagsComparer = new(
            (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
            c => c.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            c => c.ToArray());
        builder.Property(b => b.Tags)
            .HasConversion(tagsConverter)
            .Metadata.SetValueComparer(tagsComparer);
    }

    private static void Configure(EntityTypeBuilder<ChunkPool> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new ChunkPoolId(v))
            .IsRequired();

        builder.HasMany(b => b.Memberships)
            .WithOne(m => m.Pool)
            .HasForeignKey(m => m.ChunkPoolId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void Configure(EntityTypeBuilder<SpawnTable> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new SpawnTableId(v))
            .IsRequired();

        builder.OwnsMany(b => b.Entries, e =>
        {
            e.WithOwner().HasForeignKey(x => x.SpawnTableId);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.SpawnTableId)
                .HasConversion(v => v.Value, v => new SpawnTableId(v));
            e.Property(x => x.CreatureId)
                .HasConversion(v => v.Value, v => new CreatureTemplateId(v));
        });
    }

    private static void Configure(EntityTypeBuilder<ProceduralMapConfig> builder)
    {
        builder.HasKey(b => b.MapTemplateId);
        builder.Property(b => b.MapTemplateId)
            .HasConversion(v => v.Value, v => new MapTemplateId(v))
            .IsRequired();
        builder.Property(b => b.ChunkPoolId)
            .HasConversion(v => v.Value, v => new ChunkPoolId(v));
        builder.Property(b => b.SpawnTableId)
            .HasConversion(v => v.Value, v => new SpawnTableId(v));
    }

    private static void Configure(EntityTypeBuilder<MapChunkPlacement> builder)
    {
        builder.ToTable("MapChunkPlacements");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new MapChunkPlacementId(v))
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(b => b.MapTemplateId)
            .HasConversion(v => v.Value, v => new MapTemplateId(v))
            .IsRequired();
        builder.Property(b => b.ChunkTemplateId)
            .HasConversion(v => v.Value, v => new ChunkTemplateId(v))
            .IsRequired();

        builder.HasIndex(b => b.MapTemplateId);
        builder.HasIndex(b => new { b.MapTemplateId, b.GridX, b.GridZ }).IsUnique();

        builder.Property(p => p.BackPortalTargetMapId).IsRequired(false);
        builder.Property(p => p.ForwardPortalTargetMapId).IsRequired(false);
    }

    private static void Configure(EntityTypeBuilder<MapCreatureSpawn> builder)
    {
        builder.ToTable("MapCreatureSpawns");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new MapCreatureSpawnId(v))
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(b => b.MapTemplateId)
            .HasConversion(v => v.Value, v => new MapTemplateId(v))
            .IsRequired();
        builder.Property(b => b.CreatureTemplateId)
            .HasConversion(v => v.Value, v => new CreatureTemplateId(v))
            .IsRequired();

        // Placement reads every row for one map at instance-build time.
        builder.HasIndex(b => b.MapTemplateId);

        // Optional route (#421). Deleting a path leaves its spawns standing rather than deleting them.
        builder.Property(b => b.PathId)
            .HasConversion(v => v!.Value, v => new CreaturePathId(v))
            .IsRequired(false);
        builder.HasOne(b => b.Path)
            .WithMany()
            .HasForeignKey(b => b.PathId)
            .OnDelete(DeleteBehavior.SetNull);

        // Town (map 1). Offsets are metres from the map's entry spawn point and the facings are
        // yaw in degrees, chosen so each NPC looks back toward an arriving player: forward is
        // (sin yaw, 0, cos yaw), so atan2(-offsetX, -offsetZ) points at the entry.
        //
        // These positions are deliberately provisional. The town's geometry lives in the chunk
        // .obj assets rather than in this repository, so they were picked to put the three NPCs
        // in a visible arc a few metres in front of the player instead of against any particular
        // doorway. Retuning them is a data change, not a code change.
        builder.HasData(
            new MapCreatureSpawn
            {
                Id = 1, MapTemplateId = 1, CreatureTemplateId = 1,     // Uriel
                OffsetX = -3f, OffsetY = 0f, OffsetZ = 4f, Facing = 143f
            },
            new MapCreatureSpawn
            {
                Id = 2, MapTemplateId = 1, CreatureTemplateId = 2,     // Borin Stoutbeard
                OffsetX = 3f, OffsetY = 0f, OffsetZ = 4f, Facing = 217f
            },
            new MapCreatureSpawn
            {
                Id = 3, MapTemplateId = 1, CreatureTemplateId = 3,     // Innkeeper
                OffsetX = 0f, OffsetY = 0f, OffsetZ = 7f, Facing = 180f
            });

        // Marta (#463), beyond Uriel on the same side, facing the entry: atan2(6, -6) = 135 degrees.
        builder.HasData(new MapCreatureSpawn
        {
            Id = 4, MapTemplateId = 1, CreatureTemplateId = 11,     // Marta Ledgerwell
            OffsetX = -6f, OffsetY = 0f, OffsetZ = 6f, Facing = 135f
        });

        // The vendors (#432), grouped around Marta on the same side, each facing the entry:
        // atan2(9, -4) = 114, atan2(9, -8) = 132, atan2(6, -10) = 149 degrees.
        builder.HasData(
            new MapCreatureSpawn
            {
                Id = 5, MapTemplateId = 1, CreatureTemplateId = 12,     // Garrick Emberforge
                OffsetX = -9f, OffsetY = 0f, OffsetZ = 4f, Facing = 114f
            },
            new MapCreatureSpawn
            {
                Id = 6, MapTemplateId = 1, CreatureTemplateId = 13,     // Hilde Brassbuckle
                OffsetX = -9f, OffsetY = 0f, OffsetZ = 8f, Facing = 132f
            },
            new MapCreatureSpawn
            {
                Id = 7, MapTemplateId = 1, CreatureTemplateId = 14,     // Tobin Marrowfield
                OffsetX = -6f, OffsetY = 0f, OffsetZ = 10f, Facing = 149f
            });
    }

    private static void Configure(EntityTypeBuilder<CreaturePath> builder)
    {
        builder.ToTable("CreaturePaths");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new CreaturePathId(v))
            .IsRequired()
            .ValueGeneratedOnAdd();
        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);

        builder.HasMany(b => b.Points)
            .WithOne()
            .HasForeignKey(p => p.PathId)
            .OnDelete(DeleteBehavior.Cascade);

        // No seed rows: nothing patrols yet. Author a path, point a MapCreatureSpawn's PathId at it,
        // and give that creature's template a patrol script.
    }

    private static void Configure(EntityTypeBuilder<CreaturePathPoint> builder)
    {
        builder.ToTable("CreaturePathPoints");
        // A path's points are identified by their walk order, so the order is unique per path.
        builder.HasKey(b => new { b.PathId, b.Sequence });
        builder.Property(b => b.PathId)
            .HasConversion(v => v.Value, v => new CreaturePathId(v))
            .IsRequired();
        builder.Property(b => b.WaitMs).HasDefaultValue(0);
    }

    private static void Configure(EntityTypeBuilder<LocalizedText> builder)
    {
        builder.ToTable("LocalizedTexts");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new LocalizedTextId(v))
            .IsRequired()
            .ValueGeneratedOnAdd();

        // Base (enUS) wording. Translations are in LocalizedTextLocales; a locale with no row
        // falls back to these, which is what makes partial translation shippable.
        builder.HasData(
            // Node lines.
            new LocalizedText { Id = 1, Text = "The wold grows darker each season, {name}. There are seasons now where it does not lighten at all." },
            new LocalizedText { Id = 2, Text = "Something took root at its heart. The beasts feel it before we do — they change, and then they do not change back." },
            new LocalizedText { Id = 3, Text = "Some return. Not all of what returns is who left." },
            new LocalizedText { Id = 4, Text = "Steel holds. Wood rots. Remember which one you are carrying when you walk under those trees." },
            new LocalizedText { Id = 5, Text = "Once, and I came back for the anvil rather than the view. A {class} might fare better than a smith did." },
            new LocalizedText { Id = 6, Text = "Room's upstairs, {name}, stew's on, and I ask no questions about the state of your boots." },
            // Option lines. "Farewell." is written once and referenced six times.
            new LocalizedText { Id = 7, Text = "What changed?" },
            new LocalizedText { Id = 8, Text = "And the ones who go in?" },
            new LocalizedText { Id = 9, Text = "Have you been in?" },
            new LocalizedText { Id = 10, Text = "Farewell." },
            // Class names.
            new LocalizedText { Id = 11, Text = "Warrior" },
            new LocalizedText { Id = 12, Text = "Wizard" },
            new LocalizedText { Id = 13, Text = "Hunter" },
            new LocalizedText { Id = 14, Text = "Healer" });

        // Marta Ledgerwell, the banker (#463). Her closing option reuses "Farewell." (10).
        builder.HasData(
            new LocalizedText { Id = 15, Text = "Coin and keepsakes both, {name}. The vault keeps what the road would take." },
            new LocalizedText { Id = 16, Text = "Open my bank." });

        // The vendors (#432): a greeting and a trade line each, then two option lines they share.
        // Their closing option reuses "Farewell." (10).
        builder.HasData(
            new LocalizedText { Id = 17, Text = "Steel, stave or string, traveller. What'll it be?" },
            new LocalizedText { Id = 18, Text = "Every blade here I hammered myself. Won't match what the forest spits out, but it'll keep you breathing till you find better." },
            new LocalizedText { Id = 19, Text = "Mind the rack. Looking to cover something?" },
            new LocalizedText { Id = 20, Text = "Plate, leather, cloth. I fit every trade. Buy it plain, earn it fancy." },
            new LocalizedText { Id = 21, Text = "Potions, scrolls, supplies. And I'll take what you've no use for." },
            new LocalizedText { Id = 22, Text = "I buy anything that isn't nailed to you. Fair prices, mostly." },
            new LocalizedText { Id = 23, Text = "What do you deal in?" },
            new LocalizedText { Id = 24, Text = "Show me your wares." });
    }

    private static void Configure(EntityTypeBuilder<LocalizedTextLocale> builder)
    {
        builder.ToTable("LocalizedTextLocales");
        builder.HasKey(b => new { b.TextId, b.Locale });
        builder.Property(b => b.TextId)
            .HasConversion(v => v.Value, v => new LocalizedTextId(v))
            .IsRequired();
        builder.Property(b => b.Locale)
            .HasConversion(new EnumToStringConverter<AccountLocale>())
            .IsRequired();

        // ptPT. The gender selects and the class-name inflections are the parts that matter:
        // Portuguese agrees adjectives with gender where English does not, which is why the
        // {g:male|female} construct exists at all. Needs native-speaker review before merge.
        builder.HasData(
            new LocalizedTextLocale { TextId = 1, Locale = AccountLocale.ptPT, Text = "A mata escurece a cada estação, {name}. Já há estações em que nunca chega a clarear." },
            new LocalizedTextLocale { TextId = 2, Locale = AccountLocale.ptPT, Text = "Algo se enraizou no coração dela. Os bichos sentem-no antes de nós — mudam, e depois não voltam a ser o que eram." },
            new LocalizedTextLocale { TextId = 3, Locale = AccountLocale.ptPT, Text = "Alguns regressam. Mas nem tudo o que regressa é quem partiu." },
            new LocalizedTextLocale { TextId = 4, Locale = AccountLocale.ptPT, Text = "O aço aguenta. A madeira apodrece. Lembra-te de qual dos dois levas contigo quando caminhares debaixo daquelas árvores." },
            // {g:Um|Uma} agrees the article with the class name, which inflects on the same gender.
            new LocalizedTextLocale { TextId = 5, Locale = AccountLocale.ptPT, Text = "Uma vez, e voltei pela bigorna e não pela paisagem. {g:Um|Uma} {class} talvez se saia melhor do que um ferreiro se saiu." },
            new LocalizedTextLocale { TextId = 6, Locale = AccountLocale.ptPT, Text = "Sê bem-{g:vindo|vinda}, {name}. O quarto é lá em cima, o guisado está ao lume, e não faço perguntas sobre o estado das tuas botas." },
            new LocalizedTextLocale { TextId = 7, Locale = AccountLocale.ptPT, Text = "O que mudou?" },
            new LocalizedTextLocale { TextId = 8, Locale = AccountLocale.ptPT, Text = "E os que entram?" },
            new LocalizedTextLocale { TextId = 9, Locale = AccountLocale.ptPT, Text = "Já lá entraste?" },
            new LocalizedTextLocale { TextId = 10, Locale = AccountLocale.ptPT, Text = "Adeus." },
            // Caçador{g:|a} has an EMPTY male branch — the masculine takes no suffix.
            new LocalizedTextLocale { TextId = 11, Locale = AccountLocale.ptPT, Text = "Guerreir{g:o|a}" },
            new LocalizedTextLocale { TextId = 12, Locale = AccountLocale.ptPT, Text = "Mag{g:o|a}" },
            new LocalizedTextLocale { TextId = 13, Locale = AccountLocale.ptPT, Text = "Caçador{g:|a}" },
            new LocalizedTextLocale { TextId = 14, Locale = AccountLocale.ptPT, Text = "Curandeir{g:o|a}" });

        // Marta (#463). Needs native-speaker review before merge, like the rows above.
        builder.HasData(
            new LocalizedTextLocale { TextId = 15, Locale = AccountLocale.ptPT, Text = "Moedas e recordações, {name}. O cofre guarda o que a estrada levaria." },
            new LocalizedTextLocale { TextId = 16, Locale = AccountLocale.ptPT, Text = "Abre o meu cofre." });

        // The vendors (#432). Needs native-speaker review before deploying, like the rows above.
        builder.HasData(
            new LocalizedTextLocale { TextId = 17, Locale = AccountLocale.ptPT, Text = "Aço, cajado ou corda, viajante. O que vai ser?" },
            new LocalizedTextLocale { TextId = 18, Locale = AccountLocale.ptPT, Text = "Cada lâmina aqui fui eu que a forjei. Não se compara ao que a floresta cospe, mas há de te manter com vida até encontrares melhor." },
            new LocalizedTextLocale { TextId = 19, Locale = AccountLocale.ptPT, Text = "Cuidado com o expositor. Queres cobrir alguma coisa?" },
            new LocalizedTextLocale { TextId = 20, Locale = AccountLocale.ptPT, Text = "Placas, couro, tecido. Visto todos os ofícios. Compra-o simples, ganha-o vistoso." },
            new LocalizedTextLocale { TextId = 21, Locale = AccountLocale.ptPT, Text = "Poções, pergaminhos, mantimentos. E fico com o que não te faz falta." },
            new LocalizedTextLocale { TextId = 22, Locale = AccountLocale.ptPT, Text = "Compro tudo o que não estiver pregado a ti. Preços justos, quase sempre." },
            new LocalizedTextLocale { TextId = 23, Locale = AccountLocale.ptPT, Text = "Com que é que negoceias?" },
            new LocalizedTextLocale { TextId = 24, Locale = AccountLocale.ptPT, Text = "Mostra-me a tua mercadoria." });
    }

    private static void Configure(EntityTypeBuilder<DialogueNode> builder)
    {
        builder.ToTable("DialogueNodes");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new DialogueNodeId(v))
            .IsRequired()
            .ValueGeneratedOnAdd();
        builder.Property(b => b.CreatureTemplateId)
            .HasConversion(v => v.Value, v => new CreatureTemplateId(v))
            .IsRequired();
        builder.Property(b => b.TextId)
            .HasConversion(v => v.Value, v => new LocalizedTextId(v))
            .IsRequired();

        builder.HasIndex(b => b.CreatureTemplateId);

        builder.HasData(
            // Uriel (template 1).
            new DialogueNode { Id = 1, CreatureTemplateId = 1, IsRoot = true,  TextId = 1 },
            new DialogueNode { Id = 2, CreatureTemplateId = 1, IsRoot = false, TextId = 2 },
            new DialogueNode { Id = 3, CreatureTemplateId = 1, IsRoot = false, TextId = 3 },
            // Borin Stoutbeard (template 2).
            new DialogueNode { Id = 4, CreatureTemplateId = 2, IsRoot = true,  TextId = 4 },
            new DialogueNode { Id = 5, CreatureTemplateId = 2, IsRoot = false, TextId = 5 },
            // Innkeeper (template 3).
            new DialogueNode { Id = 6, CreatureTemplateId = 3, IsRoot = true,  TextId = 6 });

        // Marta Ledgerwell (template 11, #463).
        builder.HasData(new DialogueNode { Id = 7, CreatureTemplateId = 11, IsRoot = true, TextId = 15 });

        // The vendors (#432): a root (the greeting) and a trade node (the trade line) each.
        builder.HasData(
            new DialogueNode { Id = 8,  CreatureTemplateId = 12, IsRoot = true,  TextId = 17 },   // Garrick
            new DialogueNode { Id = 9,  CreatureTemplateId = 12, IsRoot = false, TextId = 18 },
            new DialogueNode { Id = 10, CreatureTemplateId = 13, IsRoot = true,  TextId = 19 },   // Hilde
            new DialogueNode { Id = 11, CreatureTemplateId = 13, IsRoot = false, TextId = 20 },
            new DialogueNode { Id = 12, CreatureTemplateId = 14, IsRoot = true,  TextId = 21 },   // Tobin
            new DialogueNode { Id = 13, CreatureTemplateId = 14, IsRoot = false, TextId = 22 });
    }

    private static void Configure(EntityTypeBuilder<DialogueOption> builder)
    {
        builder.ToTable("DialogueOptions");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new DialogueOptionId(v))
            .IsRequired()
            .ValueGeneratedOnAdd();
        builder.Property(b => b.NodeId)
            .HasConversion(v => v.Value, v => new DialogueNodeId(v))
            .IsRequired();
        builder.Property(b => b.TextId)
            .HasConversion(v => v.Value, v => new LocalizedTextId(v))
            .IsRequired();
        builder.Property(b => b.NextNodeId)
            .HasConversion(v => v!.Value, v => new DialogueNodeId(v))
            .IsRequired(false);
        // #463. Null for an option that only talks. A number, so the enum is append-only.
        builder.Property(b => b.Action).IsRequired(false);

        builder.HasIndex(b => b.NodeId);

        // Every node ends with a "Farewell." (text 10) so a player always has a way out. A node
        // with no options would leave the client showing text it cannot dismiss.
        builder.HasData(
            new DialogueOption { Id = 1,  NodeId = 1, TextId = 7,  NextNodeId = 2,    SortOrder = 0 },
            new DialogueOption { Id = 2,  NodeId = 1, TextId = 10, NextNodeId = null, SortOrder = 1 },
            new DialogueOption { Id = 3,  NodeId = 2, TextId = 8,  NextNodeId = 3,    SortOrder = 0 },
            new DialogueOption { Id = 4,  NodeId = 2, TextId = 10, NextNodeId = null, SortOrder = 1 },
            new DialogueOption { Id = 5,  NodeId = 3, TextId = 10, NextNodeId = null, SortOrder = 0 },
            new DialogueOption { Id = 6,  NodeId = 4, TextId = 9,  NextNodeId = 5,    SortOrder = 0 },
            new DialogueOption { Id = 7,  NodeId = 4, TextId = 10, NextNodeId = null, SortOrder = 1 },
            new DialogueOption { Id = 8,  NodeId = 5, TextId = 10, NextNodeId = null, SortOrder = 0 },
            new DialogueOption { Id = 9,  NodeId = 6, TextId = 10, NextNodeId = null, SortOrder = 0 });

        // Marta (#463). "Open my bank." opens the bank and leads back to her greeting, so the
        // conversation, and the bank with it, stays open; "Farewell." ends both.
        builder.HasData(
            new DialogueOption { Id = 10, NodeId = 7, TextId = 16, NextNodeId = 7, SortOrder = 0, Action = DialogueOptionAction.OpenBank },
            new DialogueOption { Id = 11, NodeId = 7, TextId = 10, NextNodeId = null, SortOrder = 1 });

        // The vendors (#432). "What do you deal in?" shows the trade line. "Show me your wares."
        // opens the shop and stays on its node, so the conversation, and the shop with it, stays
        // open. "Farewell." ends both.
        builder.HasData(
            // Garrick: root 8, trade 9.
            new DialogueOption { Id = 12, NodeId = 8,  TextId = 23, NextNodeId = 9,    SortOrder = 0 },
            new DialogueOption { Id = 13, NodeId = 8,  TextId = 24, NextNodeId = 8,    SortOrder = 1, Action = DialogueOptionAction.OpenShop },
            new DialogueOption { Id = 14, NodeId = 8,  TextId = 10, NextNodeId = null, SortOrder = 2 },
            new DialogueOption { Id = 15, NodeId = 9,  TextId = 24, NextNodeId = 9,    SortOrder = 0, Action = DialogueOptionAction.OpenShop },
            new DialogueOption { Id = 16, NodeId = 9,  TextId = 10, NextNodeId = null, SortOrder = 1 },
            // Hilde: root 10, trade 11.
            new DialogueOption { Id = 17, NodeId = 10, TextId = 23, NextNodeId = 11,   SortOrder = 0 },
            new DialogueOption { Id = 18, NodeId = 10, TextId = 24, NextNodeId = 10,   SortOrder = 1, Action = DialogueOptionAction.OpenShop },
            new DialogueOption { Id = 19, NodeId = 10, TextId = 10, NextNodeId = null, SortOrder = 2 },
            new DialogueOption { Id = 20, NodeId = 11, TextId = 24, NextNodeId = 11,   SortOrder = 0, Action = DialogueOptionAction.OpenShop },
            new DialogueOption { Id = 21, NodeId = 11, TextId = 10, NextNodeId = null, SortOrder = 1 },
            // Tobin: root 12, trade 13.
            new DialogueOption { Id = 22, NodeId = 12, TextId = 23, NextNodeId = 13,   SortOrder = 0 },
            new DialogueOption { Id = 23, NodeId = 12, TextId = 24, NextNodeId = 12,   SortOrder = 1, Action = DialogueOptionAction.OpenShop },
            new DialogueOption { Id = 24, NodeId = 12, TextId = 10, NextNodeId = null, SortOrder = 2 },
            new DialogueOption { Id = 25, NodeId = 13, TextId = 24, NextNodeId = 13,   SortOrder = 0, Action = DialogueOptionAction.OpenShop },
            new DialogueOption { Id = 26, NodeId = 13, TextId = 10, NextNodeId = null, SortOrder = 1 });
    }

    private static void Configure(EntityTypeBuilder<CharacterClassName> builder)
    {
        builder.ToTable("CharacterClassNames");
        builder.HasKey(b => b.Class);
        builder.Property(b => b.Class)
            .HasConversion(new EnumToStringConverter<CharacterClass>())
            .IsRequired();
        builder.Property(b => b.TextId)
            .HasConversion(v => v.Value, v => new LocalizedTextId(v))
            .IsRequired();

        builder.HasData(
            new CharacterClassName { Class = CharacterClass.Warrior, TextId = 11 },
            new CharacterClassName { Class = CharacterClass.Wizard,  TextId = 12 },
            new CharacterClassName { Class = CharacterClass.Hunter,  TextId = 13 },
            new CharacterClassName { Class = CharacterClass.Healer,  TextId = 14 });
    }

    /// <summary>Loot tables (issue #460). Rolled on the tick when a creature dies; see Avalon.World.Loot.</summary>
    private static void Configure(EntityTypeBuilder<LootTable> builder)
    {
        builder.ToTable("LootTables");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(v => v.Value, v => new LootTableId(v))
            .IsRequired()
            .ValueGeneratedNever();
        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);

        builder.HasMany(b => b.Entries)
            .WithOne()
            .HasForeignKey(e => e.LootTableId)
            .OnDelete(DeleteBehavior.Cascade);

        // The forest roster (issue #460). Tables 2-8 are one per hostile creature template (the
        // creature rows name them in Configure(EntityTypeBuilder<CreatureTemplate>)); tables 1 and
        // 9-11 are shared pools the creature tables reference.
        builder.HasData(
            new LootTable { Id = 1, Name = "Forest common" },
            new LootTable { Id = 2, Name = "Thornback Boar" },
            new LootTable { Id = 3, Name = "Grey Fen Wolf" },
            new LootTable { Id = 4, Name = "Blightfly Swarmling" },
            new LootTable { Id = 5, Name = "Husk of the Wold" },
            new LootTable { Id = 6, Name = "Bramblemaw Alpha" },
            new LootTable { Id = 7, Name = "Old Tuskroot" },
            new LootTable { Id = 8, Name = "Mother Bramble" },
            new LootTable { Id = 9, Name = "Forest weapons" },
            new LootTable { Id = 10, Name = "Forest scrolls" },
            new LootTable { Id = 11, Name = "Forest armour" });
    }

    private static void Configure(EntityTypeBuilder<LootTableEntry> builder)
    {
        // Exactly one target. Written to read the same on Postgres and on the SQLite the unit tests
        // build the model on: each side of <> is a boolean.
        builder.ToTable("LootTableEntries", t => t.HasCheckConstraint(
            "CK_LootTableEntries_ExactlyOneTarget",
            "(\"ItemTemplateId\" IS NULL) <> (\"ReferenceTableId\" IS NULL)"));

        // An entry is identified by its roll order, so the order is unique per table.
        builder.HasKey(b => new { b.LootTableId, b.Sequence });
        builder.Property(b => b.LootTableId)
            .HasConversion(v => v.Value, v => new LootTableId(v))
            .IsRequired();

        builder.Property(b => b.ItemTemplateId)
            .HasConversion(v => v!.Value, v => new ItemTemplateId(v))
            .IsRequired(false);
        builder.HasOne<ItemTemplate>()
            .WithMany()
            .HasForeignKey(b => b.ItemTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.ReferenceTableId)
            .HasConversion(v => v!.Value, v => new LootTableId(v))
            .IsRequired(false);
        builder.HasOne<LootTable>()
            .WithMany()
            .HasForeignKey(b => b.ReferenceTableId)
            .OnDelete(DeleteBehavior.Restrict);

        // Items 1 Health Potion, 2 Mana Potion, 3 Town Portal Scroll; 5-8 the forest weapons, 9-11 the
        // forest scrolls, 12-31 the forest armour. Every creature table rolls both potions on their
        // own, then the shared pools: table 1, the weapon group (table 9) at 2 %, the scroll group
        // (table 10) at 10 %, and the armour table (table 11) on every kill, whose pieces each roll
        // at 2 %. A group always drops exactly one of its entries when its table rolls, so the chance
        // a group drops at all is the chance its table is referenced with. Potion and common-table
        // chances rise with the creature's rarity; the pool references are the same for every creature.
        builder.HasData(
            new LootTableEntry { LootTableId = 1, Sequence = 1, ItemTemplateId = 3, Chance = 5f, MinCount = 1, MaxCount = 1 },
            new LootTableEntry { LootTableId = 1, Sequence = 2, ItemTemplateId = 1, Chance = 10f, MinCount = 1, MaxCount = 2 });

        builder.HasData(ForestCreatureTable(2, potion: 20f, potionMax: 1, mana: 10f, manaMax: 1, common: 25f));
        builder.HasData(ForestCreatureTable(3, potion: 20f, potionMax: 1, mana: 10f, manaMax: 1, common: 25f));
        builder.HasData(ForestCreatureTable(4, potion: 20f, potionMax: 1, mana: 10f, manaMax: 1, common: 25f));
        builder.HasData(ForestCreatureTable(5, potion: 20f, potionMax: 1, mana: 10f, manaMax: 1, common: 25f));
        builder.HasData(ForestCreatureTable(6, potion: 40f, potionMax: 2, mana: 25f, manaMax: 1, common: 50f));
        builder.HasData(ForestCreatureTable(7, potion: 60f, potionMax: 3, mana: 40f, manaMax: 2, common: 75f));
        builder.HasData(ForestCreatureTable(8, potion: 100f, potionMax: 4, mana: 100f, manaMax: 3, common: 100f, potionMin: 2));

        // Table 9: one weapon per class, equal weights.
        builder.HasData(
            new LootTableEntry { LootTableId = 9, Sequence = 1, ItemTemplateId = 7, Chance = 25f, GroupId = 1, MinCount = 1, MaxCount = 1 },
            new LootTableEntry { LootTableId = 9, Sequence = 2, ItemTemplateId = 5, Chance = 25f, GroupId = 1, MinCount = 1, MaxCount = 1 },
            new LootTableEntry { LootTableId = 9, Sequence = 3, ItemTemplateId = 6, Chance = 25f, GroupId = 1, MinCount = 1, MaxCount = 1 },
            new LootTableEntry { LootTableId = 9, Sequence = 4, ItemTemplateId = 8, Chance = 25f, GroupId = 1, MinCount = 1, MaxCount = 1 });

        // Table 10: three scrolls, equal weights.
        builder.HasData(
            new LootTableEntry { LootTableId = 10, Sequence = 1, ItemTemplateId = 9, Chance = 33f, GroupId = 1, MinCount = 1, MaxCount = 1 },
            new LootTableEntry { LootTableId = 10, Sequence = 2, ItemTemplateId = 10, Chance = 33f, GroupId = 1, MinCount = 1, MaxCount = 1 },
            new LootTableEntry { LootTableId = 10, Sequence = 3, ItemTemplateId = 11, Chance = 33f, GroupId = 1, MinCount = 1, MaxCount = 1 });

        // Table 11: twenty armour pieces, each rolled on its own.
        builder.HasData(
            ForestArmourEntry(sequence: 1, item: 12),
            ForestArmourEntry(sequence: 2, item: 13),
            ForestArmourEntry(sequence: 3, item: 14),
            ForestArmourEntry(sequence: 4, item: 15),
            ForestArmourEntry(sequence: 5, item: 16),
            ForestArmourEntry(sequence: 6, item: 17),
            ForestArmourEntry(sequence: 7, item: 18),
            ForestArmourEntry(sequence: 8, item: 19),
            ForestArmourEntry(sequence: 9, item: 20),
            ForestArmourEntry(sequence: 10, item: 21),
            ForestArmourEntry(sequence: 11, item: 22),
            ForestArmourEntry(sequence: 12, item: 23),
            ForestArmourEntry(sequence: 13, item: 24),
            ForestArmourEntry(sequence: 14, item: 25),
            ForestArmourEntry(sequence: 15, item: 26),
            ForestArmourEntry(sequence: 16, item: 27),
            ForestArmourEntry(sequence: 17, item: 28),
            ForestArmourEntry(sequence: 18, item: 29),
            ForestArmourEntry(sequence: 19, item: 30),
            ForestArmourEntry(sequence: 20, item: 31));
    }

    /// <summary>The shape every forest creature's table shares; only the chances and counts differ.</summary>
    private static LootTableEntry[] ForestCreatureTable(
        int table, float potion, int potionMax, float mana, int manaMax, float common, int potionMin = 1) =>
    [
        new() { LootTableId = table, Sequence = 1, ItemTemplateId = 1, Chance = potion, MinCount = potionMin, MaxCount = potionMax },
        new() { LootTableId = table, Sequence = 2, ItemTemplateId = 2, Chance = mana, MinCount = 1, MaxCount = manaMax },
        new() { LootTableId = table, Sequence = 3, ReferenceTableId = 1, Chance = common, MinCount = 1, MaxCount = 1 },
        new() { LootTableId = table, Sequence = 4, ReferenceTableId = 9, Chance = 2f, MinCount = 1, MaxCount = 1 },
        new() { LootTableId = table, Sequence = 5, ReferenceTableId = 10, Chance = 10f, MinCount = 1, MaxCount = 1 },
        new() { LootTableId = table, Sequence = 6, ReferenceTableId = 11, Chance = 100f, MinCount = 1, MaxCount = 1 },
    ];

    private static LootTableEntry ForestArmourEntry(int sequence, ulong item) =>
        new() { LootTableId = 11, Sequence = sequence, ItemTemplateId = item, Chance = 2f, MinCount = 1, MaxCount = 1 };

    /// <summary>
    /// Vendor stock (#432). Loaded whole into VendorCatalog by the Vendors reload area. Both pairings
    /// are enforced both ways, and written to read the same on Postgres and on SQLite: each side of
    /// = is a boolean.
    /// </summary>
    private static void Configure(EntityTypeBuilder<VendorStock> builder)
    {
        builder.ToTable("VendorStocks", t =>
        {
            t.HasCheckConstraint("CK_VendorStocks_RestockPairsWithMaxStock",
                "(\"MaxStock\" IS NULL) = (\"RestockSeconds\" IS NULL)");
            t.HasCheckConstraint("CK_VendorStocks_QuestPairs",
                "(\"RequiredQuestId\" IS NULL) = (\"RequiredQuestState\" IS NULL)");
            t.HasCheckConstraint("CK_VendorStocks_MaxStockPositive", "\"MaxStock\" IS NULL OR \"MaxStock\" >= 1");
            t.HasCheckConstraint("CK_VendorStocks_RestockPositive", "\"RestockSeconds\" IS NULL OR \"RestockSeconds\" >= 1");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.CreatureTemplateId)
            .HasConversion(v => v.Value, v => new CreatureTemplateId(v))
            .IsRequired();
        builder.HasOne<CreatureTemplate>()
            .WithMany()
            .HasForeignKey(b => b.CreatureTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.ItemTemplateId)
            .HasConversion(v => v.Value, v => new ItemTemplateId(v))
            .IsRequired();
        builder.HasOne<ItemTemplate>()
            .WithMany()
            .HasForeignKey(b => b.ItemTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.MaxStock).IsRequired(false);
        builder.Property(b => b.RestockSeconds).IsRequired(false);
        builder.Property(b => b.PriceOverride).IsRequired(false);
        builder.Property(b => b.RequiredQuestId).IsRequired(false);
        builder.Property(b => b.RequiredQuestState).IsRequired(false);

        // A row is named by its position in its vendor's list.
        builder.HasIndex(b => new { b.CreatureTemplateId, b.Sequence }).IsUnique();

        builder.HasMany(b => b.Costs)
            .WithOne()
            .HasForeignKey(c => c.VendorStockId)
            .OnDelete(DeleteBehavior.Cascade);

        // Garrick Emberforge (template 12): the four starter weapons, unlimited.
        builder.HasData(
            new VendorStock { Id = 1, CreatureTemplateId = 12, Sequence = 1, ItemTemplateId = 32 },
            new VendorStock { Id = 2, CreatureTemplateId = 12, Sequence = 2, ItemTemplateId = 33 },
            new VendorStock { Id = 3, CreatureTemplateId = 12, Sequence = 3, ItemTemplateId = 34 },
            new VendorStock { Id = 4, CreatureTemplateId = 12, Sequence = 4, ItemTemplateId = 35 });

        // Hilde Brassbuckle (template 13): the four starter sets, items 36-55, rows 5-24. Each class's
        // chest piece is limited to 2 and restocks every 30 minutes; the rest are unlimited.
        builder.HasData(Enumerable.Range(0, 20).Select(piece =>
        {
            bool chest = piece % 5 == 1;   // helm, chest, legs, gloves, boots within each set
            return new VendorStock
            {
                Id = 5 + piece,
                CreatureTemplateId = 13,
                Sequence = (uint)(1 + piece),
                ItemTemplateId = (ulong)(36 + piece),
                MaxStock = chest ? 2u : null,
                RestockSeconds = chest ? 1800u : null,
            };
        }).ToArray());

        // Tobin Marrowfield (template 14): the Health and Mana Potions, the Town Portal Scroll and
        // the three forest scrolls, unlimited; and the Greater Health Potion, limited to 5,
        // restocking every 10 minutes, and costing two Health Potions on top of its gold.
        builder.HasData(
            new VendorStock { Id = 25, CreatureTemplateId = 14, Sequence = 1, ItemTemplateId = 1 },
            new VendorStock { Id = 26, CreatureTemplateId = 14, Sequence = 2, ItemTemplateId = 2 },
            new VendorStock { Id = 27, CreatureTemplateId = 14, Sequence = 3, ItemTemplateId = 3 },
            new VendorStock { Id = 28, CreatureTemplateId = 14, Sequence = 4, ItemTemplateId = 9 },
            new VendorStock { Id = 29, CreatureTemplateId = 14, Sequence = 5, ItemTemplateId = 10 },
            new VendorStock { Id = 30, CreatureTemplateId = 14, Sequence = 6, ItemTemplateId = 11 },
            new VendorStock
            {
                Id = 31, CreatureTemplateId = 14, Sequence = 7, ItemTemplateId = 56, MaxStock = 5, RestockSeconds = 600
            });
    }

    private static void Configure(EntityTypeBuilder<VendorStockCost> builder)
    {
        builder.ToTable("VendorStockCosts", t => t.HasCheckConstraint("CK_VendorStockCosts_CountPositive", "\"Count\" >= 1"));

        // One cost line per item per row.
        builder.HasKey(b => new { b.VendorStockId, b.ItemTemplateId });
        builder.Property(b => b.ItemTemplateId)
            .HasConversion(v => v.Value, v => new ItemTemplateId(v))
            .IsRequired();
        builder.HasOne<ItemTemplate>()
            .WithMany()
            .HasForeignKey(b => b.ItemTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        // The Greater Health Potion (stock row 31) costs two Health Potions (item 1) per unit.
        builder.HasData(new VendorStockCost { VendorStockId = 31, ItemTemplateId = 1, Count = 2 });
    }

    private static void Configure(EntityTypeBuilder<AbilityTemplate> builder)
    {
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new AbilityId(v)
            ).IsRequired();

        builder.HasData(new AbilityTemplate
        {
            Id = 1,
            Name = "Strike",
            CastTime = 0,
            Cooldown = 2500,
            Cost = 25,
            Range = SpellRange.Melee,
            Effects = SpellEffect.Damage,
            EffectValue = 10,
            AllowedClasses = [CharacterClass.Warrior],
            SpellScript = "StrikeAbilityScript"
        }, new AbilityTemplate
        {
            Id = 2,
            Name = "Fireball",
            CastTime = 2000,
            Cooldown = 1000,
            Cost = 10,
            Range = SpellRange.Medium,
            Effects = SpellEffect.Damage,
            EffectValue = 10,
            AllowedClasses = [CharacterClass.Warrior, CharacterClass.Wizard],
            SpellScript = "FireballAbilityScript"
        }, new AbilityTemplate
        {
            // Basic attack — Warrior
            Id = 100,
            Name = "Warrior Slash",
            CastTime = 0,
            Cooldown = 500,
            Cost = 0,
            Range = SpellRange.Melee,
            Effects = SpellEffect.Damage,
            EffectValue = 15,
            AllowedClasses = [CharacterClass.Warrior],
            SpellScript = "StrikeAbilityScript",
            ThreatMultiplier = 1.5f
        }, new AbilityTemplate
        {
            // Basic attack — Wizard
            Id = 101,
            Name = "Wizard Bolt",
            CastTime = 200,
            Cooldown = 700,
            Cost = 0,
            Range = SpellRange.Medium,
            Effects = SpellEffect.Damage,
            EffectValue = 8,
            AllowedClasses = [CharacterClass.Wizard],
            SpellScript = "StrikeAbilityScript",
            ThreatMultiplier = 1.0f
        }, new AbilityTemplate
        {
            // Basic attack — Hunter
            Id = 102,
            Name = "Hunter Shot",
            CastTime = 0,
            Cooldown = 600,
            Cost = 0,
            Range = SpellRange.Long,
            Effects = SpellEffect.Damage,
            EffectValue = 10,
            AllowedClasses = [CharacterClass.Hunter],
            SpellScript = "StrikeAbilityScript",
            ThreatMultiplier = 1.0f
        }, new AbilityTemplate
        {
            // Basic attack — Healer
            Id = 103,
            Name = "Healer Wand",
            CastTime = 300,
            Cooldown = 800,
            Cost = 0,
            Range = SpellRange.Medium,
            Effects = SpellEffect.Damage,
            EffectValue = 5,
            AllowedClasses = [CharacterClass.Healer],
            SpellScript = "StrikeAbilityScript",
            ThreatMultiplier = 0.8f
        });
    }
}
