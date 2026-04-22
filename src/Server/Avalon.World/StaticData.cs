using Avalon.Database.World.Repositories;
using Avalon.Domain.World;

namespace Avalon.World;

public class StaticData(
    ICharacterCreateInfoRepository characterCreateInfoRepository,
    IClassLevelStatRepository classLevelStatRepository,
    IItemTemplateRepository itemTemplateRepository,
    ISpellTemplateRepository spellTemplateRepository,
    ICharacterLevelExperienceRepository characterLevelExperienceRepository)
{
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        CharacterCreateInfos = await characterCreateInfoRepository.FindAllAsync(cancellationToken);
        ClassLevelStats = await classLevelStatRepository.FindAllAsync(cancellationToken);
        ItemTemplates = (await itemTemplateRepository.FindAllAsync(true, cancellationToken)).AsReadOnly();
        SpellTemplates = (await spellTemplateRepository.FindAllAsync(false, cancellationToken)).AsReadOnly();
        CharacterLevelExperiences = await characterLevelExperienceRepository.GetAllAsync(cancellationToken);
    }

    public IReadOnlyCollection<CharacterCreateInfo> CharacterCreateInfos { get; private set; }
    public IReadOnlyCollection<ClassLevelStat> ClassLevelStats { get; private set; }
    public IReadOnlyCollection<ItemTemplate> ItemTemplates { get; private set; }
    public IReadOnlyCollection<SpellTemplate> SpellTemplates { get; private set; }
    public IReadOnlyCollection<CharacterLevelExperience> CharacterLevelExperiences { get; private set; }
}
