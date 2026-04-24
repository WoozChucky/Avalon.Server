using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class SpawnTableEntry
{
    public int Id { get; set; }                           // surrogate PK
    public SpawnTableId SpawnTableId { get; set; } = default!;
    public string Tag { get; set; } = string.Empty;
    public CreatureTemplateId CreatureId { get; set; } = default!;
    public float Weight { get; set; } = 1.0f;
    public byte MinCount { get; set; } = 1;
    public byte MaxCount { get; set; } = 1;
}
