using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// A loaded StaticData whose items and loot tables come from the test, read on every prepare so a
/// test can change them and reload. Every other area is empty, except one creature base-stat row
/// and one level row, which the kill path reads.
/// </summary>
internal static class LootStaticData
{
    public static async Task<StaticData> LoadAsync(
        Func<IReadOnlyCollection<ItemTemplate>> items,
        Func<IReadOnlyCollection<LootTable>> tables,
        IReadOnlyCollection<CharacterLevelExperience>? levels = null)
    {
        IReadOnlyCollection<CharacterLevelExperience> levelRows =
            levels ?? new[] { new CharacterLevelExperience { Level = 1, Experience = 1_000_000 } };

        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterCreateInfo>>([]));

        var classStats = Substitute.For<IClassLevelStatRepository>();
        classStats.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<ClassLevelStat>>([]));

        var itemRepository = Substitute.For<IItemTemplateRepository>();
        itemRepository.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(items().ToList()));

        var abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<AbilityTemplate>()));

        var levelRepository = Substitute.For<ICharacterLevelExperienceRepository>();
        levelRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(levelRows));

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

        var texts = Substitute.For<ILocalizedTextRepository>();
        texts.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        texts.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        texts.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        var data = new StaticData(createInfos, classStats, itemRepository, abilities, levelRepository,
            creatures, baseStats, rarities, texts, dialogue, LootRepositories.Of(tables), NullLoggerFactory.Instance);
        await data.LoadAsync();
        return data;
    }
}
