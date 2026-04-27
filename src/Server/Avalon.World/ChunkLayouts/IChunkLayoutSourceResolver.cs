using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

public interface IChunkLayoutSourceResolver
{
    IChunkLayoutSource Resolve(MapTemplate template, out ChunkLayoutSourceKind kind);
}
