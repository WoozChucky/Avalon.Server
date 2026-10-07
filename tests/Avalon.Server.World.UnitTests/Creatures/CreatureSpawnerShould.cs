using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Reload;
using Avalon.World.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Creatures;

public class CreatureSpawnerShould
{
    /// <summary>
    /// The trap this task exists to remove: CreatureStatDeriver used to be captured once by
    /// StaticData.LoadAsync and never rebuilt, so a reload of the base-stat table would leave every
    /// already-derived template silently stuck on the old numbers.
    /// </summary>
    [Fact]
    public async Task Use_Reloaded_Base_Stats_For_The_Next_Spawn_Only()
    {
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(60),
            Name = "Stat Reload Target",
            MinLevel = 1,
            MaxLevel = 1,
            Rarity = CreatureRarity.Normal,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };

        (CreatureSpawner spawner, StaticData data, MutableRepos repos) = ReloadableSpawnerOver(template);

        ICreature before = spawner.Spawn(template.Id);
        uint originalHealth = before.Health;

        // A NEW row, not a mutation of the existing one: CreatureStatDeriver's dictionary holds the
        // very CreatureBaseStat object references it was built from, so mutating the shared row in
        // place would make even a stale, never-rebuilt deriver report the new health — vacuously
        // passing this test regardless of whether the reload path actually rebuilds anything. A
        // fresh object is also what production does: every prepare reads fresh rows from the
        // database, never the same in-memory instance twice.
        repos.BaseStats[0] = new CreatureBaseStat
        {
            Level = 1,
            Health = originalHealth + 500,
            DamageMin = 4,
            DamageMax = 7,
            Experience = 25
        };
        data.Apply(await data.PrepareAsync(ReloadArea.Creatures));

        ICreature after = spawner.Spawn(template.Id);

        Assert.Equal(originalHealth, before.Health);
        Assert.Equal(originalHealth + 500, after.Health);
    }

    [Fact]
    public void Roll_A_Level_Inside_The_Templates_Range()
    {
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(43),
            Name = "Grey Fen Wolf",
            MinLevel = 2,
            MaxLevel = 5,
            Rarity = CreatureRarity.Normal,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };

        CreatureSpawner spawner = SpawnerOver(template);

        for (int i = 0; i < 200; i++)
        {
            ICreature creature = spawner.Spawn(template.Id);
            Assert.InRange(creature.Level, (ushort)2, (ushort)5);
        }
    }

    /// <summary>
    /// The flag is what makes a town NPC unkillable, and CombatService reads it off the creature rather than the
    /// template: dropped here, every NPC is killable. Left unset, it stays false, or adding the column would
    /// silently make every monster in the game unkillable.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Carry_the_templates_invulnerable_flag_onto_the_spawned_creature(bool invulnerable)
    {
        CreatureTemplate template = PlainTemplate(44, "Innkeeper");
        if (invulnerable)
            template.Invulnerable = true;

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.Equal(invulnerable, creature.Invulnerable);
    }

    /// <summary>
    /// What a skill's shape must overlap (#164): the template's body radius, or the default one with a warning when
    /// the template's is not a finite value above 0. The check constraint refuses such a row in Postgres, but a
    /// template can still reach the spawner another way (a test database, a hand-built template). A NaN body is
    /// never hit, and an infinite one is hit by everything.
    /// </summary>
    [Theory]
    [InlineData(1.75f, true)]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(0f, false)]
    [InlineData(-1f, false)]
    public void Use_the_templates_body_radius_or_warn_and_fall_back_to_the_default(float radius, bool usable)
    {
        CreatureTemplate template = PlainTemplate(49, "Misshapen Wolf");
        template.BodyRadius = radius;
        var warnings = new WarningCountingLoggerFactory();

        ICreature creature = SpawnerOver(template, warnings).Spawn(template.Id);

        Assert.Equal(usable ? radius : UnitBody.DefaultCreatureRadius, creature.BodyRadius);
        Assert.Equal(usable ? 0 : 1, warnings.Warnings);
    }

    /// <summary>
    /// <c>/reload dialogue</c> is forward-only like every other reload area: the flag is fixed at
    /// spawn, so a creature already standing keeps the value it spawned with and only the next spawn
    /// of that template picks up the new dialogue.
    /// </summary>
    [Fact]
    public async Task Fix_The_Interact_Flag_At_Spawn_So_A_Dialogue_Reload_Reaches_Only_Later_Spawns()
    {
        CreatureTemplate template = PlainTemplate(62, "Late Talker");
        template.MinLevel = 1;
        template.MaxLevel = 1;

        (CreatureSpawner spawner, StaticData data, MutableRepos repos) = ReloadableSpawnerOver(template);

        ICreature before = spawner.Spawn(template.Id);

        repos.DialogueNodes.Add(RootNodeFor(template.Id));
        data.Apply(await data.PrepareAsync(ReloadArea.Dialogue));

        ICreature after = spawner.Spawn(template.Id);

        Assert.Null(ObjectStateWriter.From(before, GameEntityFields.None).CanInteract);
        Assert.True(ObjectStateWriter.From(after, GameEntityFields.None).CanInteract);
    }

    /// <summary>
    /// #709: <c>/reload creatures</c> is forward-only for the advertised rarity too: a creature already
    /// standing keeps the rarity it spawned with, and only the next spawn of that template is sent the new one.
    /// </summary>
    [Fact]
    public async Task Fix_The_Rarity_At_Spawn_So_A_Creature_Reload_Reaches_Only_Later_Spawns()
    {
        CreatureTemplate template = PlainTemplate(64, "Promoted Wolf");
        template.MinLevel = 1;
        template.MaxLevel = 1;

        (CreatureSpawner spawner, StaticData data, MutableRepos repos) = ReloadableSpawnerOver(template);

        ICreature before = spawner.Spawn(template.Id);

        repos.Templates[0] = new CreatureTemplate
        {
            Id = template.Id,
            Name = template.Name,
            MinLevel = 1,
            MaxLevel = 1,
            Rarity = CreatureRarity.Boss,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };
        data.Apply(await data.PrepareAsync(ReloadArea.Creatures));

        ICreature after = spawner.Spawn(template.Id);

        Assert.Null(ObjectStateWriter.From(before, GameEntityFields.CreatureUpdate).Rarity);
        Assert.Equal(Avalon.Network.Packets.State.CreatureRarity.Boss,
            ObjectStateWriter.From(after, GameEntityFields.CreatureUpdate).Rarity);
    }

    /// <summary>Forest content pass: a procedural map's depth band picks the level; the template keeps its identity and kit.</summary>
    [Fact]
    public void Spawn_at_the_level_it_is_given_rather_than_the_templates_range()
    {
        CreatureTemplate template = PlainTemplate(70, "Band Target");   // its own range is 2-2
        CreatureSpawner spawner = SpawnerOver(template);

        ICreature creature = spawner.Spawn(new Avalon.World.Public.Maps.CreatureInfo { PrototypeIndex = 70 }, level: 4);

        Assert.Equal((ushort)4, creature.Level);
        Assert.Equal(84u, creature.Health);   // the level 4 base row: 84 health, Normal, modifier 1
        Assert.Equal(template.Id, creature.Metadata.Id);
    }

    [Fact]
    public void Spawn_at_level_one_when_given_level_zero()
    {
        CreatureSpawner spawner = SpawnerOver(PlainTemplate(71, "Floor Target"));

        ICreature creature = spawner.Spawn(new Avalon.World.Public.Maps.CreatureInfo { PrototypeIndex = 71 }, level: 0);

        Assert.Equal((ushort)1, creature.Level);
    }

    private static CreatureTemplate PlainTemplate(ulong id, string name) => new()
    {
        Id = new CreatureTemplateId(id),
        Name = name,
        MinLevel = 2,
        MaxLevel = 2,
        Rarity = CreatureRarity.Normal,
        HealthModifier = 1f,
        DamageModifier = 1f,
        ExperienceModifier = 1f
    };

    private static DialogueNode RootNodeFor(CreatureTemplateId templateId) => new()
    {
        Id = new DialogueNodeId(1),
        CreatureTemplateId = templateId,
        IsRoot = true,
        TextId = new LocalizedTextId(1)
    };

    /// <summary>
    /// A real <c>StaticData</c> over substituted repositories, loaded once, so the spawner reads its
    /// template and stats from <c>world.Data</c> the same way it does in production. The base stats
    /// and rarity rows are the real seeded values for the levels these tests touch.
    /// </summary>
    private static CreatureSpawner SpawnerOver(CreatureTemplate template, params DialogueNode[] dialogueNodes)
        => SpawnerOver(template, NullLoggerFactory.Instance, dialogueNodes);

    private static CreatureSpawner SpawnerOver(CreatureTemplate template, ILoggerFactory spawnerLogging,
        params DialogueNode[] dialogueNodes)
    {
        ICreatureTemplateRepository templateRepository = Substitute.For<ICreatureTemplateRepository>();
        templateRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate> { template }));

        CreatureBaseStat[] baseStats =
        [
            new() { Level = 2, Health = 52,  DamageMin = 4, DamageMax = 7,  Experience = 25 },
            new() { Level = 3, Health = 66,  DamageMin = 5, DamageMax = 9,  Experience = 40 },
            new() { Level = 4, Health = 84,  DamageMin = 7, DamageMax = 11, Experience = 60 },
            new() { Level = 5, Health = 106, DamageMin = 9, DamageMax = 14, Experience = 85 },
        ];
        ICreatureBaseStatRepository baseStatRepository = Substitute.For<ICreatureBaseStatRepository>();
        baseStatRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(baseStats));

        CreatureRarityModifier[] rarities =
        [
            new() { Rarity = CreatureRarity.Normal, HealthMultiplier = 1.0f, DamageMultiplier = 1.0f, ExperienceMultiplier = 1.0f },
            new() { Rarity = CreatureRarity.Boss,   HealthMultiplier = 8.0f, DamageMultiplier = 2.0f, ExperienceMultiplier = 15.0f },
        ];
        ICreatureRarityModifierRepository rarityRepository = Substitute.For<ICreatureRarityModifierRepository>();
        rarityRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>(rarities));

        ICharacterCreateInfoRepository createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));
        IClassLevelStatRepository classLevelStats = Substitute.For<IClassLevelStatRepository>();
        classLevelStats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassLevelStat>>([]));
        IItemTemplateRepository itemTemplates = Substitute.For<IItemTemplateRepository>();
        itemTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ItemTemplate>()));
        IAbilityTemplateRepository abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AbilityTemplate>()));
        ICharacterLevelExperienceRepository characterLevelExperiences = Substitute.For<ICharacterLevelExperienceRepository>();
        characterLevelExperiences.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));
        ILocalizedTextRepository localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));
        IDialogueRepository dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>(dialogueNodes));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var data = new StaticData(createInfos, classLevelStats, itemTemplates, abilityTemplates,
            characterLevelExperiences, templateRepository, baseStatRepository, rarityRepository,
            localizedText, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);
        data.LoadAsync().GetAwaiter().GetResult();

        IWorld world = Substitute.For<IWorld>();
        world.Data.Returns(data);

        return new CreatureSpawner(spawnerLogging, world);
    }

    /// <summary>Counts warnings from every logger it creates.</summary>
    private sealed class WarningCountingLoggerFactory : ILoggerFactory, ILogger
    {
        public int Warnings { get; private set; }

        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings++;
        }
    }

    /// <summary>
    /// Repository stand-ins the reload trap tests mutate between a spawn and a reload. The
    /// substituted repositories read through these lists via lambdas, so reassigning or mutating a
    /// field here changes what the next <c>PrepareAsync</c> reads.
    /// </summary>
    private sealed class MutableRepos
    {
        public List<CreatureTemplate> Templates = [];
        public List<CreatureBaseStat> BaseStats = [];
        public List<DialogueNode> DialogueNodes = [];
    }

    /// <summary>
    /// Like <see cref="SpawnerOver(CreatureTemplate, DialogueNode[])"/>, but the template and base-stat repositories read through
    /// <see cref="MutableRepos"/> so a trap-regression test can change what the next
    /// <c>StaticData.PrepareAsync</c> reads and reload it into the same <c>StaticData</c> the
    /// spawner already holds.
    /// </summary>
    private static (CreatureSpawner Spawner, StaticData Data, MutableRepos Repos) ReloadableSpawnerOver(
        CreatureTemplate template)
    {
        var repos = new MutableRepos
        {
            Templates = [template],
            BaseStats = [new() { Level = 1, Health = 50, DamageMin = 4, DamageMax = 7, Experience = 25 }]
        };

        ICreatureTemplateRepository templateRepository = Substitute.For<ICreatureTemplateRepository>();
        templateRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(repos.Templates.ToList()));

        ICreatureBaseStatRepository baseStatRepository = Substitute.For<ICreatureBaseStatRepository>();
        baseStatRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(repos.BaseStats.ToList()));

        CreatureRarityModifier[] rarities =
        [
            new() { Rarity = CreatureRarity.Normal, HealthMultiplier = 1.0f, DamageMultiplier = 1.0f, ExperienceMultiplier = 1.0f },
            new() { Rarity = CreatureRarity.Boss,   HealthMultiplier = 8.0f, DamageMultiplier = 2.0f, ExperienceMultiplier = 15.0f },
        ];
        ICreatureRarityModifierRepository rarityRepository = Substitute.For<ICreatureRarityModifierRepository>();
        rarityRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>(rarities));

        ICharacterCreateInfoRepository createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));
        IClassLevelStatRepository classLevelStats = Substitute.For<IClassLevelStatRepository>();
        classLevelStats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassLevelStat>>([]));
        IItemTemplateRepository itemTemplates = Substitute.For<IItemTemplateRepository>();
        itemTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ItemTemplate>()));
        IAbilityTemplateRepository abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AbilityTemplate>()));
        ICharacterLevelExperienceRepository characterLevelExperiences = Substitute.For<ICharacterLevelExperienceRepository>();
        characterLevelExperiences.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));
        ILocalizedTextRepository localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));
        IDialogueRepository dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<DialogueNode>>(repos.DialogueNodes.ToList()));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var data = new StaticData(createInfos, classLevelStats, itemTemplates, abilityTemplates,
            characterLevelExperiences, templateRepository, baseStatRepository, rarityRepository,
            localizedText, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);
        data.LoadAsync().GetAwaiter().GetResult();

        IWorld world = Substitute.For<IWorld>();
        world.Data.Returns(data);

        return (new CreatureSpawner(NullLoggerFactory.Instance, world), data, repos);
    }
}
