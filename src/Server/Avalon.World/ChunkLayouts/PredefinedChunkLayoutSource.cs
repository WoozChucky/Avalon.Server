using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

public class PredefinedChunkLayoutSource : IChunkLayoutSource
{
    public Task<ChunkLayout> BuildAsync(MapTemplate template, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Task 4");
}
