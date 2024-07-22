using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
    public DbSet<ItemInstance> ItemInstances { get; set; } = null!;
    public DbSet<MapTemplate> MapTemplates { get; set; } = null!;
    public DbSet<QuestReward> QuestRewards { get; set; } = null!;
    public DbSet<QuestRewardTemplate> QuestRewardTemplates { get; set; } = null!;
    public DbSet<QuestTemplate> QuestTemplates { get; set; } = null!;
    public DbSet<ClassLevelStat> ClassLevelStats { get; set; } = null!;
    public DbSet<CharacterLevelExperience> CharacterLevelExperiences { get; set; } = null!;
    public DbSet<CharacterCreateInfo> CharacterCreateInfos { get; set; } = null!;
    public DbSet<SpellTemplate> SpellTemplates { get; set; } = null!;

    public WorldDbContext(ILoggerFactory loggerFactory, IOptions<DatabaseConfiguration> opts)
    {
        _loggerFactory = loggerFactory;
        _connectionString = opts.Value.World!.ConnectionString;
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
        Configure(modelBuilder.Entity<CreatureTemplate>());
        Configure(modelBuilder.Entity<ItemTemplate>());
        Configure(modelBuilder.Entity<ItemInstance>());
        Configure(modelBuilder.Entity<MapTemplate>());
        Configure(modelBuilder.Entity<QuestReward>());
        Configure(modelBuilder.Entity<QuestRewardTemplate>());
        Configure(modelBuilder.Entity<QuestTemplate>());
        Configure(modelBuilder.Entity<ClassLevelStat>());
        Configure(modelBuilder.Entity<CharacterLevelExperience>());
        Configure(modelBuilder.Entity<CharacterCreateInfo>());
        Configure(modelBuilder.Entity<SpellTemplate>());
    }
    
    private static void Configure(EntityTypeBuilder<CharacterLevelExperience> builder)
    {
        builder.HasKey(b => b.Level);
        
        builder.HasData([
            new CharacterLevelExperience
            {
                Level = 1,
                Experience = 400
            },
            new CharacterLevelExperience
            {
                Level = 2,
                Experience = 900
            },
            new CharacterLevelExperience
            {
                Level = 3,
                Experience = 1400
            },
            new CharacterLevelExperience
            {
                Level = 4,
                Experience = 2100
            },
            new CharacterLevelExperience
            {
                Level = 5,
                Experience = 2800
            },
            new CharacterLevelExperience
            {
                Level = 6,
                Experience = 3600
            },
            new CharacterLevelExperience
            {
                Level = 7,
                Experience = 4500
            },
            new CharacterLevelExperience
            {
                Level = 8,
                Experience = 5400
            },
            new CharacterLevelExperience
            {
                Level = 9,
                Experience = 6500
            },
            new CharacterLevelExperience
            {
                Level = 10,
                Experience = 7600
            },
            new CharacterLevelExperience
            {
                Level = 11,
                Experience = 8700
            },
            new CharacterLevelExperience
            {
                Level = 12,
                Experience = 9800
            },
            new CharacterLevelExperience
            {
                Level = 13,
                Experience = 11000
            },
            new CharacterLevelExperience
            {
                Level = 14,
                Experience = 12300
            },
            new CharacterLevelExperience
            {
                Level = 15,
                Experience = 13600
            },
        ]);
    }
    
    private static void Configure(EntityTypeBuilder<CharacterCreateInfo> builder)
    {
        builder.HasKey(b => b.Class);
        
        var itemIdConverter = new ValueConverter<List<ItemTemplateId>, string>(
            v => string.Join(",", v.Select(i => i.Value)),
            v => v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(val => new ItemTemplateId(ulong.Parse(val))).ToList());

        var itemIdComparer = new ValueComparer<List<ItemTemplateId>>(
            (c1, c2) => c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());
        
        builder.Property(b => b.StartingItems)
            .HasConversion(itemIdConverter)
            .Metadata.SetValueComparer(itemIdComparer);
        
        var spellIdConverter = new ValueConverter<List<SpellId>, string>(
            v => string.Join(",", v.Select(i => i.Value)),
            v => v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(val => new SpellId(uint.Parse(val))).ToList());
        
        var spellIdComparer = new ValueComparer<List<SpellId>>(
            (c1, c2) => c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => System.HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());
        
        builder.Property(b => b.StartingSpells)
            .HasConversion(spellIdConverter)
            .Metadata.SetValueComparer(spellIdComparer);
        
        builder.HasData([
            new CharacterCreateInfo
            {
                Class = CharacterClass.Warrior,
                Map = 1,
                X = 25,
                Y = 51,
                Z = 25,
                Rotation = 0,
                StartingItems = [1, 2, 3],
                StartingSpells = [1]
            },
            new CharacterCreateInfo
            {
                Class = CharacterClass.Wizard,
                Map = 1,
                X = 25,
                Y = 51,
                Z = 25,
                Rotation = 0,
                StartingItems = [1, 2],
                StartingSpells = [2]
            },
            new CharacterCreateInfo
            {
                Class = CharacterClass.Hunter,
                Map = 1,
                X = 25,
                Y = 51,
                Z = 25,
                Rotation = 0,
                StartingItems = [1, 2],
                StartingSpells = []
            },
            new CharacterCreateInfo
            {
                Class = CharacterClass.Healer,
                Map = 1,
                X = 25,
                Y = 51,
                Z = 25,
                Rotation = 0,
                StartingItems = [1, 2],
                StartingSpells = []
            }
        ]);
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
    
    private static void Configure(EntityTypeBuilder<ClassLevelStat> builder)
    {
        builder.HasKey(b => new { b.Class, b.Level });
        
        builder.HasData([
            // Warrior
            new ClassLevelStat
            {
                Class = CharacterClass.Warrior,
                Level = 1,
                BaseHp = 20,
                BaseMana = 0,
                Stamina = 22,
                Strength = 23,
                Agility = 20,
                Intellect = 20
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Warrior,
                Level = 2,
                BaseHp = 40,
                BaseMana = 0,
                Stamina = 24,
                Strength = 25,
                Agility = 21,
                Intellect = 20
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Warrior,
                Level = 3,
                BaseHp = 60,
                BaseMana = 0,
                Stamina = 26,
                Strength = 27,
                Agility = 23,
                Intellect = 20
            },
            new ClassLevelStat()
            {
                Class = CharacterClass.Warrior,
                Level = 4,
                BaseHp = 80,
                BaseMana = 0,
                Stamina = 28,
                Strength = 29,
                Agility = 24,
                Intellect = 20
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Warrior,
                Level = 5,
                BaseHp = 100,
                BaseMana = 0,
                Stamina = 30,
                Strength = 31,
                Agility = 26,
                Intellect = 20
            },
            // Wizard
            new ClassLevelStat
            {
                Class = CharacterClass.Wizard,
                Level = 1,
                BaseHp = 16,
                BaseMana = 20,
                Stamina = 21,
                Strength = 20,
                Agility = 20,
                Intellect = 23
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Wizard,
                Level = 2,
                BaseHp = 32,
                BaseMana = 40,
                Stamina = 22,
                Strength = 20,
                Agility = 21,
                Intellect = 25
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Wizard,
                Level = 3,
                BaseHp = 48,
                BaseMana = 60,
                Stamina = 23,
                Strength = 20,
                Agility = 22,
                Intellect = 27
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Wizard,
                Level = 4,
                BaseHp = 64,
                BaseMana = 80,
                Stamina = 24,
                Strength = 20,
                Agility = 23,
                Intellect = 29
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Wizard,
                Level = 5,
                BaseHp = 80,
                BaseMana = 100,
                Stamina = 25,
                Strength = 20,
                Agility = 24,
                Intellect = 31
            },
            // Hunter
            new ClassLevelStat
            {
                Class = CharacterClass.Hunter,
                Level = 1,
                BaseHp = 18,
                BaseMana = 10,
                Stamina = 20,
                Strength = 21,
                Agility = 23,
                Intellect = 20
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Hunter,
                Level = 2,
                BaseHp = 36,
                BaseMana = 20,
                Stamina = 21,
                Strength = 22,
                Agility = 25,
                Intellect = 20
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Hunter,
                Level = 3,
                BaseHp = 54,
                BaseMana = 30,
                Stamina = 22,
                Strength = 23,
                Agility = 27,
                Intellect = 20
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Hunter,
                Level = 4,
                BaseHp = 72,
                BaseMana = 40,
                Stamina = 23,
                Strength = 24,
                Agility = 29,
                Intellect = 20
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Hunter,
                Level = 5,
                BaseHp = 90,
                BaseMana = 50,
                Stamina = 24,
                Strength = 25,
                Agility = 31,
                Intellect = 20
            },
            // Healer
            new ClassLevelStat
            {
                Class = CharacterClass.Healer,
                Level = 1,
                BaseHp = 18,
                BaseMana = 20,
                Stamina = 20,
                Strength = 20,
                Agility = 21,
                Intellect = 23
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Healer,
                Level = 2,
                BaseHp = 36,
                BaseMana = 40,
                Stamina = 21,
                Strength = 20,
                Agility = 22,
                Intellect = 25
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Healer,
                Level = 3,
                BaseHp = 54,
                BaseMana = 60,
                Stamina = 22,
                Strength = 20,
                Agility = 23,
                Intellect = 27
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Healer,
                Level = 4,
                BaseHp = 72,
                BaseMana = 80,
                Stamina = 23,
                Strength = 20,
                Agility = 24,
                Intellect = 29
            },
            new ClassLevelStat
            {
                Class = CharacterClass.Healer,
                Level = 5,
                BaseHp = 90,
                BaseMana = 100,
                Stamina = 24,
                Strength = 20,
                Agility = 25,
                Intellect = 31
            }
        ]);
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
                SpeedWalk = 2.0f,
                SpeedRun = 5.0f,
                SpeedSwim = 1.6f,
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
                SpeedWalk = 2.0f,
                SpeedRun = 5.0f,
                SpeedSwim = 1.6f,
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
                ScriptName = "UrielPathfinderScript",
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
                SpeedWalk = 2.0f,
                SpeedRun = 5.0f,
                SpeedSwim = 1.6f,
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
        
        var characterClassConverter = new ValueConverter<List<CharacterClass>, string>(
            v => string.Join(',', v.Select(e => e.ToString())),
            v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(e => (CharacterClass)Enum.Parse(typeof(CharacterClass), e)).ToList());
        
        var characterClassComparer = new ValueComparer<List<CharacterClass>>(
            (c1, c2) => c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());
        
        builder
            .Property(e => e.AllowedClasses)
            .HasConversion(characterClassConverter)
            .Metadata.SetValueComparer(characterClassComparer);

        builder.HasData([
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
                Slot = null,
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
                Slot = null,
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
                Slot = null,
            },
            new ItemTemplate()
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
                StatValue1 = 13, // 1.3 seconds
            }
        
        ]);
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

    private static void Configure(EntityTypeBuilder<SpellTemplate> builder)
    {
        builder.Property(b => b.Id)
            .HasConversion(
                v => v.Value,
                v => new SpellId(v)
            ).IsRequired();

        builder.HasData([
            new SpellTemplate
            {
                Id = 1,
                Name = "Strike",
                CastTime = 0,
                Cooldown = 2500,
                Cost = 25,
                Range = SpellRange.Melee,
                Effects = SpellEffect.Damage,
                EffectValue = 10,
                AllowedClasses = [CharacterClass.Warrior]
            },
            new SpellTemplate
            {
                Id = 2,
                Name = "Lightning Bolt",
                CastTime = 1000,
                Cooldown = 1000,
                Cost = 10,
                Range = SpellRange.Medium,
                Effects = SpellEffect.Damage,
                EffectValue = 10,
                AllowedClasses = [CharacterClass.Wizard]
            }
        ]);
    }
}
