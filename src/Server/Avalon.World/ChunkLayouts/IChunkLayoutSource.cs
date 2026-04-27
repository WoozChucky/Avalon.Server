using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

public interface IChunkLayoutSource
{
    Task<ChunkLayout> BuildAsync(MapTemplate template, CancellationToken ct);
}

public enum ChunkLayoutSourceKind { Predefined, Procedural }
