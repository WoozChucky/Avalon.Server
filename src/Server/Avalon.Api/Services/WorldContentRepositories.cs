using Avalon.Api.Hosting.Worlds;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;

namespace Avalon.Api.Services;

/// <summary>
/// The repositories over one named world (<see cref="IWorldRepositories"/>), and the inputs its procedural maps are
/// laid out from: observability's reads of a presence's own world (#523).
/// </summary>
public interface IWorldContentRepositories : IWorldRepositories
{
    IProceduralLayoutInputsResolver LayoutInputs(WorldId world);
}

public sealed class WorldContentRepositories : WorldRepositories, IWorldContentRepositories
{
    private readonly IWorldDbContextFactory _contexts;

    public WorldContentRepositories(IWorldDbContextFactory contexts) : base(contexts)
    {
        _contexts = contexts;
    }

    public IProceduralLayoutInputsResolver LayoutInputs(WorldId world) =>
        new ProceduralLayoutInputsResolver(
            new ChunkPoolRepository(_contexts.ForWorld(world)),
            new ChunkTemplateRepository(_contexts.ForWorld(world)));
}
