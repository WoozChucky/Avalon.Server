using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests;

/// <summary>
/// The stubbed reference-data repositories StaticData (and World, which builds its own StaticData)
/// is constructed from. Each one answers from what the test passed, read again on every call, so a
/// test can change the rows and reload.
/// </summary>
internal sealed record TestStaticDataRepositories(
    ICharacterCreateInfoRepository CreateInfos,
    IClassLevelStatRepository ClassStats,
    IItemTemplateRepository Items,
    IAbilityTemplateRepository Abilities,
    ICharacterLevelExperienceRepository Levels,
    ICreatureTemplateRepository Creatures,
    ICreatureBaseStatRepository BaseStats,
    ICreatureRarityModifierRepository Rarities,
    ILocalizedTextRepository Texts,
    IDialogueRepository Dialogue,
    ILootTableRepository Loot,
    IVendorStockRepository? Vendors = null,
    ICombatDataRepository? Combat = null,
    IQuestRepository? Quests = null,
    IScriptManager? Scripts = null,
    IAuraTemplateRepository? Auras = null)
{
    public StaticData ToStaticData(Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null) =>
        new(CreateInfos, ClassStats, Items, Abilities, Levels, Creatures, BaseStats, Rarities, Texts, Dialogue, Loot,
            loggerFactory ?? NullLoggerFactory.Instance, Vendors, Combat, Quests, Scripts, Auras);
}

/// <summary>
/// A loaded StaticData over stubbed repositories, holding only what a test passes: class stats,
/// item templates, level thresholds, dialogue, texts, loot tables and vendor stock. Every other area is empty,
/// except one creature base-stat row, which the creature area needs to build its deriver.
/// </summary>
internal static class TestStaticData
{
    public static Task<StaticData> LoadAsync(
        IReadOnlyCollection<ClassLevelStat>? classStats = null,
        IReadOnlyCollection<ItemTemplate>? items = null,
        IReadOnlyCollection<CharacterLevelExperience>? levels = null,
        IReadOnlyCollection<DialogueNode>? nodes = null,
        IReadOnlyCollection<DialogueOption>? options = null,
        IReadOnlyCollection<LocalizedText>? texts = null,
        IReadOnlyCollection<VendorStock>? vendors = null) =>
        LoadAsync(Repositories(
            classStats: () => classStats ?? [],
            items: () => items ?? [],
            levels: () => levels ?? [],
            nodes: () => nodes ?? [],
            options: () => options ?? [],
            texts: () => texts ?? [],
            vendors: vendors is { } rows ? VendorRepositories.Of(() => rows) : null));

    public static async Task<StaticData> LoadAsync(TestStaticDataRepositories repositories)
    {
        var data = repositories.ToStaticData();
        await data.LoadAsync();
        return data;
    }

    /// <summary>Every source is read on each call; one left out is empty.</summary>
    public static TestStaticDataRepositories Repositories(
        Func<IReadOnlyCollection<ClassLevelStat>>? classStats = null,
        Func<IReadOnlyCollection<ItemTemplate>>? items = null,
        Func<IReadOnlyCollection<CharacterLevelExperience>>? levels = null,
        Func<IReadOnlyCollection<DialogueNode>>? nodes = null,
        Func<IReadOnlyCollection<DialogueOption>>? options = null,
        Func<IReadOnlyCollection<LocalizedText>>? texts = null,
        ILootTableRepository? loot = null,
        IVendorStockRepository? vendors = null,
        ICombatDataRepository? combat = null,
        Func<IReadOnlyCollection<CreatureTemplate>>? creatures = null,
        IQuestRepository? quests = null,
        IScriptManager? scripts = null,
        Func<IReadOnlyCollection<AbilityTemplate>>? abilities = null,
        Func<IReadOnlyCollection<AuraTemplate>>? auras = null)
    {
        ICharacterCreateInfoRepository createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));

        IClassLevelStatRepository classStatRepository = Substitute.For<IClassLevelStatRepository>();
        classStatRepository.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(classStats?.Invoke() ?? []));

        IItemTemplateRepository itemRepository = Substitute.For<IItemTemplateRepository>();
        itemRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult((items?.Invoke() ?? []).ToList()));

        IAbilityTemplateRepository abilityRepository = Substitute.For<IAbilityTemplateRepository>();
        abilityRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult((abilities?.Invoke() ?? []).ToList()));

        ICharacterLevelExperienceRepository levelRepository = Substitute.For<ICharacterLevelExperienceRepository>();
        levelRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(levels?.Invoke() ?? []));

        ICreatureTemplateRepository creatureRepository = Substitute.For<ICreatureTemplateRepository>();
        creatureRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult((creatures?.Invoke() ?? []).ToList()));

        ICreatureBaseStatRepository baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(
                [new CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));

        ICreatureRarityModifierRepository rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>([]));

        ILocalizedTextRepository textRepository = Substitute.For<ILocalizedTextRepository>();
        textRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(texts?.Invoke() ?? []));
        textRepository.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        textRepository.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        IDialogueRepository dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(nodes?.Invoke() ?? []));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(options?.Invoke() ?? []));

        IAuraTemplateRepository auraRepository = Substitute.For<IAuraTemplateRepository>();
        auraRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<AuraTemplate>>(auras?.Invoke() ?? []));

        return new TestStaticDataRepositories(createInfos, classStatRepository, itemRepository, abilityRepository,
            levelRepository, creatureRepository, baseStats, rarities, textRepository, dialogue, loot ?? LootRepositories.Empty(),
            vendors, combat, quests, scripts, auraRepository);
    }
}
