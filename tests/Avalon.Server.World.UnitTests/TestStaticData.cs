using System.Reflection;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World;
using Avalon.World.Scripts;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests;

/// <summary>
/// The stubbed reference-data repositories StaticData (and World, which builds its own StaticData)
/// is constructed from. Each one answers from what the test passed, read again on every call, so a
/// test can change the rows and reload. Without <see cref="ItemModifiers" /> the modifier catalog is empty.
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
    IAuraTemplateRepository? Auras = null,
    IDoodadTemplateRepository? Doodads = null,
    IItemModifierRepository? ItemModifiers = null)
{
    public StaticData ToStaticData(Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null) =>
        new(CreateInfos, ClassStats, Items, ItemModifiers ?? ItemModifierRepositories.Empty(), Abilities, Levels, Creatures, BaseStats, Rarities, Texts, Dialogue, Loot,
            loggerFactory ?? NullLoggerFactory.Instance, Vendors, Combat, Quests, Scripts, Auras, Doodads);
}

/// <summary>
/// A loaded StaticData over stubbed repositories, holding only what a test passes: class stats,
/// item templates, level thresholds, dialogue, texts, loot tables and vendor stock. Every other area is empty,
/// except one creature base-stat row, which the creature area needs to build its deriver.
/// </summary>
internal static class TestStaticData
{
    /// <summary>
    /// A fresh Glyph template (#1180 part C), the one a sound item patch holds: a test whose world loads items and asks
    /// that nothing was logged at Error passes it among its templates.
    /// </summary>
    public static ItemTemplate Glyph() => new()
    {
        Id = new ItemTemplateId(7900),
        Name = "Glyph",
        Class = ItemClass.Crafting,
        SubClass = ItemSubClass.Glyph,
        MaxStackSize = 1,
        Rarity = ItemRarity.Common,
    };

    /// <summary>
    /// The seeded template of each ability the seeded talent tree links (#1181), the abilities a sound tree needs, since
    /// each capstone is checked against its ability's shape: a test whose StaticData falls back on the seeded tree and
    /// asks that nothing was logged at Error passes them among its abilities, and <see cref="TalentTreeAuras" /> among
    /// its auras, as it passes <see cref="Glyph" /> among its items. Copies, fresh on each call: the shared scenario data
    /// they are read from is never handed out, so a test may change them.
    /// </summary>
    public static IReadOnlyCollection<AbilityTemplate> TalentTreeAbilities() =>
        [.. TalentSeed.Abilities().Select(link => Seeded.Abilities.TryGet(link.AbilityId, out AbilityTemplate? template)
            ? Copy(template)
            : throw new InvalidOperationException($"the seed holds no ability {link.AbilityId.Value}"))];

    /// <summary>
    /// The seeded auras <see cref="TalentTreeAbilities" /> apply, which their capstones are checked against: copies, their
    /// modifiers included.
    /// </summary>
    public static IReadOnlyCollection<AuraTemplate> TalentTreeAuras() =>
    [
        .. TalentTreeAbilities().Select(a => a.AuraId).OfType<AuraId>().DistinctBy(id => id.Value)
            .Select(id => Seeded.Auras.TryGet(id, out AuraTemplate? aura)
                ? Copy(aura)
                : throw new InvalidOperationException($"the seed holds no aura {id.Value}")),
    ];

    private static StaticData Seeded => ScenarioReferenceData.Shared.Data;

    /// <summary>A copy of every public settable property; an aura's modifiers are copied one by one.</summary>
    private static T Copy<T>(T source) where T : new()
    {
        T copy = new();
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property is { CanRead: true, CanWrite: true })
                property.SetValue(copy, property.GetValue(source));
        }

        if (copy is AuraTemplate aura)
            aura.Modifiers = [.. aura.Modifiers.Select(Copy)];
        return copy;
    }

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
        Func<IReadOnlyCollection<AuraTemplate>>? auras = null,
        Func<IReadOnlyCollection<DoodadTemplate>>? doodads = null)
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

        IDoodadTemplateRepository? doodadRepository = null;
        if (doodads is not null)
        {
            doodadRepository = Substitute.For<IDoodadTemplateRepository>();
            doodadRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(doodads()));
        }

        return new TestStaticDataRepositories(createInfos, classStatRepository, itemRepository, abilityRepository,
            levelRepository, creatureRepository, baseStats, rarities, textRepository, dialogue, loot ?? LootRepositories.Empty(),
            vendors, combat, quests, scripts, auraRepository, doodadRepository);
    }
}
