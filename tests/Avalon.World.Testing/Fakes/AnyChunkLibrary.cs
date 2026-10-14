using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.World.Testing.Fakes;

/// <summary>
/// A chunk library that knows every chunk, by a name made from its id ("chunk_7"). A <c>MapInstance</c> names its
/// layout's chunks as it is constructed (the entries of the chunk layout packet an arrival is sent) and cannot be built
/// without a chunk library, so an instance built over a layout no catalog holds is given this one. It serves nothing
/// else: no pool, no set piece.
/// </summary>
public sealed class AnyChunkLibrary : IChunkLibrary
{
    public ChunkTemplate GetById(ChunkTemplateId id) => new() { Id = id, Name = $"chunk_{id.Value}" };

    public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

    public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) => throw new NotSupportedException();

    public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
        throw new NotSupportedException();

    public IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId) => throw new NotSupportedException();
}
