using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class SpawnTable : IDbEntity<SpawnTableId>
{
    public SpawnTableId Id { get; set; } = default!;
    public string Name { get; set; } = string.Empty;
    public List<SpawnTableEntry> Entries { get; set; } = [];
}
