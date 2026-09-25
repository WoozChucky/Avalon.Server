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
    public DbSet<ItemInstance> ItemInstances { get; set; } = null!;
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
    public DbSet<LocalizedText> LocalizedTexts { get; set; } = null!;
    public DbSet<LocalizedTextLocale> LocalizedTextLocales { get; set; } = null!;
    public DbSet<DialogueNode> DialogueNodes { get; set; } = null!;
    public DbSet<DialogueOption> DialogueOptions { get; set; } = null!;
    public DbSet<CharacterClassName> CharacterClassNames { get; set; } = null!;

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
        Configure(modelBuilder.Entity<ItemInstance>());
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
        Configure(modelBuilder.Entity<LocalizedText>());
        Configure(modelBuilder.Entity<LocalizedTextLocale>());
        Configure(modelBuilder.Entity<DialogueNode>());
        Configure(modelBuilder.Entity<DialogueOption>());
        Configure(modelBuilder.Entity<CharacterClassName>());

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
    /// These numbers are provisional: they are calibrated against a player who has 100 health and never
    /// grows, which is issue #434. Creature and character numbers get revisited together in a balance
    /// pass once gear and character scaling exist to compensate.
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
            LootId = 0,
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
            LootId = 0,
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
            LootId = 0,
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
            LootId = 0,
            MinGold = 0,
            MaxGold = 0,
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
            LootId = 0,
            MinGold = 0,
            MaxGold = 0,
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
            LootId = 0,
            MinGold = 0,
            MaxGold = 0,
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
            LootId = 0,
            MinGold = 0,
            MaxGold = 0,
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
            LootId = 0,
            MinGold = 0,
            MaxGold = 0,
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
            LootId = 0,
            MinGold = 0,
            MaxGold = 0,
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
            LootId = 0,
            MinGold = 0,
            MaxGold = 0,
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
            .ValueGeneratedOnAdd();

        builder.Property(b => b.CharacterId)
            .HasConversion(
                v => v.Value,
                v => new CharacterId(v)
            );

        builder.Property(b => b.TemplateId)
            .HasConversion(
                v => v.Value,
                v => new ItemTemplateId(v)
            );

        builder.HasOne(b => b.Template)
            .WithMany()
            .HasForeignKey(b => b.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
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
            });
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
