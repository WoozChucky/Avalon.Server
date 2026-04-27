using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.World.ChunkLayouts;

public class ChunkLayoutSourceResolver : IChunkLayoutSourceResolver
{
    private readonly IChunkLayoutSource _predefined;
    private readonly IChunkLayoutSource _procedural;

    public ChunkLayoutSourceResolver(
        PredefinedChunkLayoutSource predefined,
        ProceduralChunkLayoutSource procedural)
    {
        _predefined = predefined;
        _procedural = procedural;
    }

    // Test ctor — accepts the abstraction directly so unit tests can substitute.
    // Public so the unit-test assembly (not strong-named) can call it without
    // an InternalsVisibleTo handshake.
    public ChunkLayoutSourceResolver(IChunkLayoutSource predefined, IChunkLayoutSource procedural)
    {
        _predefined = predefined;
        _procedural = procedural;
    }

    public IChunkLayoutSource Resolve(MapTemplate template, out ChunkLayoutSourceKind kind)
    {
        if (template.MapType == MapType.Town)
        {
            kind = ChunkLayoutSourceKind.Predefined;
            return _predefined;
        }

        kind = ChunkLayoutSourceKind.Procedural;
        return _procedural;
    }
}
