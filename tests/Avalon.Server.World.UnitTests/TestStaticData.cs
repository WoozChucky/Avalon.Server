using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests;

/// <summary>
/// A loaded StaticData over stubbed repositories, holding only what a test passes: class stats,
/// item templates, level thresholds, dialogue and texts. Every other area is empty, except one
/// creature base-stat row, which the creature area needs to build its deriver.
/// </summary>
internal static class TestStaticData
{
    public static async Task<StaticData> LoadAsync(
        IReadOnlyCollection<ClassLevelStat>? classStats = null,
        IReadOnlyCollection<ItemTemplate>? items = null,
        IReadOnlyCollection<CharacterLevelExperience>? levels = null,
        IReadOnlyCollection<DialogueNode>? nodes = null,
        IReadOnlyCollection<DialogueOption>? options = null,
        IReadOnlyCollection<LocalizedText>? texts = null)
    {
        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));

        var classStatRepository = Substitute.For<IClassLevelStatRepository>();
        classStatRepository.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(classStats ?? []));

        var itemRepository = Substitute.For<IItemTemplateRepository>();
        itemRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((items ?? []).ToList()));

        var abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AbilityTemplate>()));

        var levelRepository = Substitute.For<ICharacterLevelExperienceRepository>();
        levelRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(levels ?? []));

        var creatures = Substitute.For<ICreatureTemplateRepository>();
        creatures.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate>()));

        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(
                [new CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));

        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>([]));

        var textRepository = Substitute.For<ILocalizedTextRepository>();
        textRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(texts ?? []));
        textRepository.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        textRepository.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(nodes ?? []));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(options ?? []));

        var data = new StaticData(createInfos, classStatRepository, itemRepository, abilities, levelRepository,
            creatures, baseStats, rarities, textRepository, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);
        await data.LoadAsync();
        return data;
    }
}
