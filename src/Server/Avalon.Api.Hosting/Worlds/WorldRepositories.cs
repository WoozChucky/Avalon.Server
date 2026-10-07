using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;

namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// The ordinary repositories over one named world, for the reads that span worlds (#523): the
/// caller's characters on every world, and observability's lookups in a presence's own world.
/// Each call builds a fresh repository; they hold no state beyond their context factory.
/// </summary>
public interface IWorldRepositories
{
    ICharacterRepository Characters(WorldId world);
    IGameplayFenceRepository GameplayFences(WorldId world);
    ICharacterConsolidationRepository CharacterConsolidations(WorldId world);
    IMapTemplateRepository MapTemplates(WorldId world);
    IProceduralMapConfigRepository ProceduralMapConfigs(WorldId world);
}

public class WorldRepositories(IWorldDbContextFactory contexts) : IWorldRepositories
{
    public ICharacterConsolidationRepository CharacterConsolidations(WorldId world) => new CharacterConsolidationRepository(contexts.ForCharacters(world));

    public IGameplayFenceRepository GameplayFences(WorldId world) => new GameplayFenceRepository(contexts.ForCharacters(world));

    public ICharacterRepository Characters(WorldId world) => new CharacterRepository(contexts.ForCharacters(world));

    public IMapTemplateRepository MapTemplates(WorldId world) => new MapTemplateRepository(contexts.ForWorld(world));

    public IProceduralMapConfigRepository ProceduralMapConfigs(WorldId world) =>
        new ProceduralMapConfigRepository(contexts.ForWorld(world));
}
