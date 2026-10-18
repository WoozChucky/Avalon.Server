using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// A chunk of a pool, its weight (above 0) and the most times one layout may place it (null: no limit).
/// </summary>
public record ChunkPoolMember(ChunkTemplate Template, float Weight, int? MaxPerLayout = null);
