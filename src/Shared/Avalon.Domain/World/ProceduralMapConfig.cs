using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class ProceduralMapConfig
{
    public MapTemplateId MapTemplateId { get; set; } = default!;   // PK + FK
    public ChunkPoolId ChunkPoolId { get; set; } = default!;
    public SpawnTableId SpawnTableId { get; set; } = default!;
    public ushort MainPathMin { get; set; }
    public ushort MainPathMax { get; set; }
    public float BranchChance { get; set; }
    public byte BranchMaxDepth { get; set; }
    public bool HasBoss { get; set; }
    public ushort BackPortalTargetMapId { get; set; }
    public ushort? ForwardPortalTargetMapId { get; set; }
}
