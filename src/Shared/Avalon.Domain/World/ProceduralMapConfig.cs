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

    /// <summary>
    /// The first main-path step (the entry is step 0) a set piece other than the boss's may be placed at; 0 places them
    /// from step 1, as before the limit existed. The boss's set piece still ends the main path whatever this says.
    /// </summary>
    public ushort MinSetPieceStep { get; set; }

    /// <summary>Creature levels by depth (forest content pass); empty keeps every template's own level range.</summary>
    public List<ProceduralDepthBand> DepthBands { get; set; } = [];
}
