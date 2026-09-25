using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Creatures;
using Avalon.World.Dialogue;
using Avalon.World.Localization;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Localization;
using Microsoft.Extensions.Logging;

namespace Avalon.World;

public class StaticData(
    ICharacterCreateInfoRepository characterCreateInfoRepository,
    IClassLevelStatRepository classLevelStatRepository,
    IItemTemplateRepository itemTemplateRepository,
    IAbilityTemplateRepository abilityTemplateRepository,
    ICharacterLevelExperienceRepository characterLevelExperienceRepository,
    ICreatureTemplateRepository creatureTemplateRepository,
    ICreatureBaseStatRepository creatureBaseStatRepository,
    ICreatureRarityModifierRepository creatureRarityModifierRepository,
    ILocalizedTextRepository localizedTextRepository,
    IDialogueRepository dialogueRepository,
    ILoggerFactory loggerFactory)
{
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        CharacterCreateInfos = await characterCreateInfoRepository.FindAllAsync(cancellationToken);
        ClassLevelStats = await classLevelStatRepository.FindAllAsync(cancellationToken);
        ItemTemplates = (await itemTemplateRepository.FindAllAsync(false, cancellationToken)).AsReadOnly();
        AbilityTemplates = (await abilityTemplateRepository.FindAllAsync(false, cancellationToken)).AsReadOnly();
        CharacterLevelExperiences = await characterLevelExperienceRepository.GetAllAsync(cancellationToken);
        CreatureBaseStats = await creatureBaseStatRepository.GetAllAsync(cancellationToken);
        CreatureRarityModifiers = await creatureRarityModifierRepository.GetAllAsync(cancellationToken);

        CreatureTemplates = (await creatureTemplateRepository.FindAllAsync(false, cancellationToken)).AsReadOnly();
        CreatureStats = new CreatureStatDeriver(CreatureBaseStats, CreatureRarityModifiers, loggerFactory);

        LocalizedTexts = new LocalizedTextCatalog(
            await localizedTextRepository.GetAllAsync(cancellationToken),
            await localizedTextRepository.GetAllLocalesAsync(cancellationToken),
            await localizedTextRepository.GetAllClassNamesAsync(cancellationToken),
            loggerFactory);

        Dialogue = new DialogueCatalog(
            await dialogueRepository.GetAllNodesAsync(cancellationToken),
            await dialogueRepository.GetAllOptionsAsync(cancellationToken),
            loggerFactory);
    }

    public IReadOnlyCollection<CharacterCreateInfo> CharacterCreateInfos { get; private set; }
    public IReadOnlyCollection<ClassLevelStat> ClassLevelStats { get; private set; }
    public IReadOnlyCollection<ItemTemplate> ItemTemplates { get; private set; }
    public IReadOnlyCollection<AbilityTemplate> AbilityTemplates { get; private set; }
    public IReadOnlyCollection<CharacterLevelExperience> CharacterLevelExperiences { get; private set; }
    public IReadOnlyCollection<CreatureBaseStat> CreatureBaseStats { get; private set; }
    public IReadOnlyCollection<CreatureRarityModifier> CreatureRarityModifiers { get; private set; }
    public IReadOnlyCollection<CreatureTemplate> CreatureTemplates { get; private set; } = [];

    /// <summary>
    /// Rebuilt whenever creature data loads. It used to be a Lazy singleton that captured the base
    /// stats once on first use and ignored every later change.
    /// </summary>
    public CreatureStatDeriver CreatureStats { get; private set; } = null!;
    public ILocalizedTextCatalog LocalizedTexts { get; private set; } = null!;
    public IDialogueCatalog Dialogue { get; private set; } = null!;
}
