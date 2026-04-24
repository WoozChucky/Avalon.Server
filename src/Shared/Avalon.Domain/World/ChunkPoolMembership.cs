using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class ChunkPoolMembership
{
    public ChunkPoolId ChunkPoolId { get; set; } = default!;
    public ChunkTemplateId ChunkTemplateId { get; set; } = default!;
    public float Weight { get; set; } = 1.0f;

    public ChunkPool Pool { get; set; } = default!;
    public ChunkTemplate Template { get; set; } = default!;
}
