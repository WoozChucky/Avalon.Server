using Avalon.Domain.World;
using Avalon.World.Database.Repositories;

namespace Avalon.World;

public class StaticData(
    ICharacterCreateInfoRepository characterCreateInfoRepository,
    IClassLevelStatRepository classLevelStatRepository,
    IItemTemplateRepository itemTemplateRepository)
{
    public async Task LoadAsync()
    {
        CharacterCreateInfos = await characterCreateInfoRepository.FindAllAsync();
        ClassLevelStats = await classLevelStatRepository.FindAllAsync();
        ItemTemplates = (await itemTemplateRepository.FindAllAsync(true)).AsReadOnly();
    }
    
    public IReadOnlyCollection<CharacterCreateInfo> CharacterCreateInfos { get; private set; }
    public IReadOnlyCollection<ClassLevelStat> ClassLevelStats { get; private set; }
    public IReadOnlyCollection<ItemTemplate> ItemTemplates { get; private set; }
}
