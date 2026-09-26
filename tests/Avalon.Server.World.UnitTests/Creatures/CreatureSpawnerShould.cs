using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Avalon.World.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

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
            Level = 1, Health = originalHealth + 500, DamageMin = 4, DamageMax = 7, Experience = 25
        };
        data.Apply(await data.PrepareAsync(ReloadArea.Creatures));

        ICreature after = spawner.Spawn(template.Id);

        Assert.Equal(originalHealth, before.Health);
        Assert.Equal(originalHealth + 500, after.Health);
    }

    /// <summary>
    /// The other half of the same trap: a reloaded template's own modifier has to reach the next
    /// spawn too, not just the base-stat table.
    /// </summary>
    [Fact]
    public async Task Use_A_Reloaded_Templates_Modifier_For_The_Next_Spawn()
    {
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(61),
            Name = "Template Reload Target",
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

        repos.Templates[0] = new CreatureTemplate
        {
            Id = template.Id,
            Name = template.Name,
            MinLevel = template.MinLevel,
            MaxLevel = template.MaxLevel,
            Rarity = template.Rarity,
            HealthModifier = 2f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };
        data.Apply(await data.PrepareAsync(ReloadArea.Creatures));

        ICreature after = spawner.Spawn(template.Id);

        Assert.Equal(originalHealth, before.Health);
        Assert.Equal(originalHealth * 2, after.Health);
    }

    /// <summary>
    /// The single assertion that would have caught the bug this whole change exists to fix: every
    /// creature used to spawn as level 1 with 100 health whatever its template said.
    /// </summary>
    [Fact]
    public void Give_A_Spawned_Creature_Its_Templates_Stats_Rather_Than_Hardcoded_Ones()
    {
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(42),
            Name = "Mother Bramble",
            MinLevel = 5,
            MaxLevel = 5,
            Rarity = CreatureRarity.Boss,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f,
            SpeedWalk = 2f,
            SpeedRun = 4f
        };

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.Equal(5, creature.Level);
        Assert.NotEqual(100u, creature.Health);
        Assert.Equal(848u, creature.Health);     // base 106 * 8.0 boss
        Assert.Equal(18u, creature.DamageMin);   // base 9 * 2.0
        Assert.Equal(28u, creature.DamageMax);   // base 14 * 2.0
        Assert.Equal(creature.Health, creature.CurrentHealth);
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

    [Fact]
    public void Carry_The_Templates_Invulnerable_Flag_Onto_The_Spawned_Creature()
    {
        // The flag is what makes a town NPC unkillable, and CombatService reads it off the creature
        // rather than the template. If the spawner drops it, every NPC is killable and the guard in
        // ApplyDamageCore never fires.
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(44),
            Name = "Innkeeper",
            MinLevel = 2,
            MaxLevel = 2,
            Rarity = CreatureRarity.Normal,
            Invulnerable = true,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.True(creature.Invulnerable);
    }

    [Fact]
    public void Leave_A_Creature_Vulnerable_When_Its_Template_Says_Nothing()
    {
        // The default has to stay false, or adding the column would silently make every monster
        // in the game unkillable.
        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(45),
            Name = "Thornback Boar",
            MinLevel = 2,
            MaxLevel = 2,
            Rarity = CreatureRarity.Normal,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.False(creature.Invulnerable);
    }

    [Fact]
    public void Mark_A_Creature_Whose_Template_Has_Dialogue_As_Interactable()
    {
        // What lets a client offer "talk to" for an NPC and not for a wolf. The flag is read off the
        // dialogue catalog at spawn, with the same rule InteractHandler applies.
        CreatureTemplate template = PlainTemplate(46, "Innkeeper");

        ICreature creature = SpawnerOver(template, RootNodeFor(template.Id)).Spawn(template.Id);

        Assert.True(ObjectStateWriter.From(creature, GameEntityFields.CreatureUpdate).CanInteract);
    }

    [Fact]
    public void Leave_A_Monster_Without_The_Interact_Flag()
    {
        // Null, not false: a monster's state omits the member altogether.
        CreatureTemplate template = PlainTemplate(47, "Grey Fen Wolf");

        ICreature creature = SpawnerOver(template).Spawn(template.Id);

        Assert.Null(ObjectStateWriter.From(creature, GameEntityFields.CreatureUpdate).CanInteract);
    }

    [Fact]
    public void Leave_A_Creature_Without_The_Flag_When_Only_Another_Template_Has_Dialogue()
    {
        CreatureTemplate template = PlainTemplate(48, "Thornback Boar");

        ICreature creature = SpawnerOver(template, RootNodeFor(new CreatureTemplateId(999)))
            .Spawn(template.Id);

        Assert.Null(ObjectStateWriter.From(creature, GameEntityFields.CreatureUpdate).CanInteract);
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
    {
        var templateRepository = Substitute.For<ICreatureTemplateRepository>();
        templateRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate> { template }));

        CreatureBaseStat[] baseStats =
        [
            new() { Level = 2, Health = 52,  DamageMin = 4, DamageMax = 7,  Experience = 25 },
            new() { Level = 3, Health = 66,  DamageMin = 5, DamageMax = 9,  Experience = 40 },
            new() { Level = 4, Health = 84,  DamageMin = 7, DamageMax = 11, Experience = 60 },
            new() { Level = 5, Health = 106, DamageMin = 9, DamageMax = 14, Experience = 85 },
        ];
        var baseStatRepository = Substitute.For<ICreatureBaseStatRepository>();
        baseStatRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(baseStats));

        CreatureRarityModifier[] rarities =
        [
            new() { Rarity = CreatureRarity.Normal, HealthMultiplier = 1.0f, DamageMultiplier = 1.0f, ExperienceMultiplier = 1.0f },
            new() { Rarity = CreatureRarity.Boss,   HealthMultiplier = 8.0f, DamageMultiplier = 2.0f, ExperienceMultiplier = 15.0f },
        ];
        var rarityRepository = Substitute.For<ICreatureRarityModifierRepository>();
        rarityRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>(rarities));

        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));
        var classLevelStats = Substitute.For<IClassLevelStatRepository>();
        classLevelStats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassLevelStat>>([]));
        var itemTemplates = Substitute.For<IItemTemplateRepository>();
        itemTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ItemTemplate>()));
        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AbilityTemplate>()));
        var characterLevelExperiences = Substitute.For<ICharacterLevelExperienceRepository>();
        characterLevelExperiences.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));
        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));
        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>(dialogueNodes));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var data = new StaticData(createInfos, classLevelStats, itemTemplates, abilityTemplates,
            characterLevelExperiences, templateRepository, baseStatRepository, rarityRepository,
            localizedText, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);
        data.LoadAsync().GetAwaiter().GetResult();

        var world = Substitute.For<IWorld>();
        world.Data.Returns(data);

        return new CreatureSpawner(NullLoggerFactory.Instance, world);
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
    /// Like <see cref="SpawnerOver"/>, but the template and base-stat repositories read through
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

        var templateRepository = Substitute.For<ICreatureTemplateRepository>();
        templateRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(repos.Templates.ToList()));

        var baseStatRepository = Substitute.For<ICreatureBaseStatRepository>();
        baseStatRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(repos.BaseStats.ToList()));

        CreatureRarityModifier[] rarities =
        [
            new() { Rarity = CreatureRarity.Normal, HealthMultiplier = 1.0f, DamageMultiplier = 1.0f, ExperienceMultiplier = 1.0f },
        ];
        var rarityRepository = Substitute.For<ICreatureRarityModifierRepository>();
        rarityRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>(rarities));

        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));
        var classLevelStats = Substitute.For<IClassLevelStatRepository>();
        classLevelStats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassLevelStat>>([]));
        var itemTemplates = Substitute.For<IItemTemplateRepository>();
        itemTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ItemTemplate>()));
        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AbilityTemplate>()));
        var characterLevelExperiences = Substitute.For<ICharacterLevelExperienceRepository>();
        characterLevelExperiences.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterLevelExperience>>([]));
        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));
        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<DialogueNode>>(repos.DialogueNodes.ToList()));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var data = new StaticData(createInfos, classLevelStats, itemTemplates, abilityTemplates,
            characterLevelExperiences, templateRepository, baseStatRepository, rarityRepository,
            localizedText, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);
        data.LoadAsync().GetAwaiter().GetResult();

        var world = Substitute.For<IWorld>();
        world.Data.Returns(data);

        return (new CreatureSpawner(NullLoggerFactory.Instance, world), data, repos);
    }
}
