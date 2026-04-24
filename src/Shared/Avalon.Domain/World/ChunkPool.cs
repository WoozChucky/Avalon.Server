using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class ChunkPool : IDbEntity<ChunkPoolId>
{
    public ChunkPoolId Id { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
    public List<ChunkPoolMembership> Memberships { get; set; } = [];
}
