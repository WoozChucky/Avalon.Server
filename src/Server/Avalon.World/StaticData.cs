using Avalon.Domain.World;
using Avalon.World.Database.Repositories;

namespace Avalon.World;

public class StaticData(
    ICharacterCreateInfoRepository characterCreateInfoRepository,
    IClassLevelStatRepository classLevelStatRepository,
    IItemTemplateRepository itemTemplateRepository,
    ISpellTemplateRepository spellTemplateRepository,
    ICharacterLevelExperienceRepository characterLevelExperienceRepository)
{
    public async Task LoadAsync()
    {
        CharacterCreateInfos = await characterCreateInfoRepository.FindAllAsync();
        ClassLevelStats = await classLevelStatRepository.FindAllAsync();
        ItemTemplates = (await itemTemplateRepository.FindAllAsync(true)).AsReadOnly();
        SpellTemplates = (await spellTemplateRepository.FindAllAsync()).AsReadOnly();
        CharacterLevelExperiences = await characterLevelExperienceRepository.GetAllAsync();
    }
    
    public IReadOnlyCollection<CharacterCreateInfo> CharacterCreateInfos { get; private set; }
    public IReadOnlyCollection<ClassLevelStat> ClassLevelStats { get; private set; }
    public IReadOnlyCollection<ItemTemplate> ItemTemplates { get; private set; }
    public IReadOnlyCollection<SpellTemplate> SpellTemplates { get; private set; }
    public IReadOnlyCollection<CharacterLevelExperience> CharacterLevelExperiences { get; private set; }
}
