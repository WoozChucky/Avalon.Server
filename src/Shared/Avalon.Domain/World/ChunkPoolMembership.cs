using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class ChunkPoolMembership
{
    public ChunkPoolId ChunkPoolId { get; set; } = default!;
    public ChunkTemplateId ChunkTemplateId { get; set; } = default!;
    public float Weight { get; set; } = 1.0f;

    /// <summary>The most times one procedural layout may place this chunk; null places it without limit.</summary>
    public int? MaxPerLayout { get; set; }

    public ChunkPool Pool { get; set; } = default!;
    public ChunkTemplate Template { get; set; } = default!;
}
