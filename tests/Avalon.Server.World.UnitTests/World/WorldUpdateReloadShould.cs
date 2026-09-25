using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Creatures;
using Avalon.World.Maps;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// Every test in StaticDataReloadShould calls Data.ApplyPending() itself, so deleting
/// "Data.ApplyPending();" from World.Update would leave all of them green. This is the only test
/// pinning the wiring: it drives a real World.Update tick and asserts a queued reload goes live.
/// </summary>
public class WorldUpdateReloadShould
{
    [Fact]
    public async Task Apply_A_Reload_Queued_Before_The_Tick_When_Update_Runs()
    {
        Avalon.World.World world = await BuildWorldAsync();

        var newTemplate = new CreatureTemplate
        {
            Id = new CreatureTemplateId(999),
            Name = "reloaded-creature",
            MinLevel = 1,
            MaxLevel = 1,
            Rarity = CreatureRarity.Normal,
            HealthModifier = 1f,
            DamageModifier = 1f,
            ExperienceModifier = 1f
        };
        List<CreatureBaseStat> baseStats = [new() { Level = 1, Health = 10, DamageMin = 1, DamageMax = 2, Experience = 5 }];
        List<CreatureRarityModifier> rarities =
            [new() { Rarity = CreatureRarity.Normal, HealthMultiplier = 1f, DamageMultiplier = 1f, ExperienceMultiplier = 1f }];

        var patch = new CreaturesPatch(
            [newTemplate], baseStats, rarities,
            new CreatureStatDeriver(baseStats, rarities, NullLoggerFactory.Instance));

        Task applied = world.Data.ApplyOnNextTickAsync(patch);
        Assert.False(applied.IsCompleted);

        world.Update(TimeSpan.FromMilliseconds(16));

        await applied.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(world.Data.CreatureTemplates, t => t.Id == newTemplate.Id);
    }

    private static async Task<Avalon.World.World> BuildWorldAsync()
    {
        var worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test", Host = "127.0.0.1", Port = 0, MinVersion = "0.0.1", Version = "1.0.0"
            });

        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterCreateInfo>());
        var stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ClassLevelStat>());
        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterLevelExperience>());
        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemTemplate>());
        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<AbilityTemplate>());
        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var creatureTemplates = Substitute.For<ICreatureTemplateRepository>();
        creatureTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate>()));
        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(
                [new CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));
        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>([]));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(Substitute.For<IChunkLayoutInstanceFactory>());

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        scopeFactory.CreateScope().Returns(scope);

        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration
            {
                WorldId = new Avalon.Domain.Auth.WorldId(1),
                ScriptHotReloadIntervalSeconds = 3600
            }),
            serviceProvider,
            worldRepository,
            Substitute.For<IAvalonMapManager>(),
            scopeFactory,
            createInfos,
            stats,
            items,
            abilityTemplates,
            levels,
            creatureTemplates,
            baseStats,
            rarities,
            localizedText,
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            dialogue, LootRepositories.Empty());

        await world.LoadAsync(CancellationToken.None);
        return world;
    }
}
