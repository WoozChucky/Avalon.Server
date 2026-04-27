using DotRecast.Recast.Toolset;

namespace Avalon.World.Maps.Navigation;

/// <summary>
/// Single source of truth for DotRecast bake parameters.
/// Both the server baker (ChunkLayoutNavmeshBuilder, the only in-memory bake path
/// since the legacy town .obj→.nav pipeline was removed) and any client-side mirror
/// bake MUST use these values. Drift here = drift in the baked DtNavMesh = drift in
/// RaycastWalkable results.
/// </summary>
public static class NavmeshBuildSettings
{
    public const float CellSize = 0.3f;
    public const float CellHeight = 0.2f;

    public const float AgentHeight = 2.0f;
    public const float AgentRadius = 0.6f;
    public const float AgentMaxClimb = 0.9f;
    public const float AgentMaxSlope = 45f;
    public const float AgentMaxAcceleration = 8.0f;
    public const float AgentMaxSpeed = 3.5f;

    public const int MinRegionSize = 8;
    public const int MergedRegionSize = 20;

    public const bool FilterLowHangingObstacles = true;
    public const bool FilterLedgeSpans = true;
    public const bool FilterWalkableLowHeightSpans = true;

    public const float EdgeMaxLen = 12f;
    public const float EdgeMaxError = 1.3f;
    public const int VertsPerPoly = 6;

    public const float DetailSampleDist = 6f;
    public const float DetailSampleMaxError = 1f;
    public const int TileSize = 32;

    public static RcNavMeshBuildSettings Create()
    {
        var bs = new RcNavMeshBuildSettings();
        bs.cellSize = CellSize;
        bs.cellHeight = CellHeight;
        bs.agentHeight = AgentHeight;
        bs.agentRadius = AgentRadius;
        bs.agentMaxClimb = AgentMaxClimb;
        bs.agentMaxSlope = AgentMaxSlope;
        bs.agentMaxAcceleration = AgentMaxAcceleration;
        bs.agentMaxSpeed = AgentMaxSpeed;
        bs.minRegionSize = MinRegionSize;
        bs.mergedRegionSize = MergedRegionSize;
        bs.filterLowHangingObstacles = FilterLowHangingObstacles;
        bs.filterLedgeSpans = FilterLedgeSpans;
        bs.filterWalkableLowHeightSpans = FilterWalkableLowHeightSpans;
        bs.edgeMaxLen = EdgeMaxLen;
        bs.edgeMaxError = EdgeMaxError;
        bs.vertsPerPoly = VertsPerPoly;
        bs.detailSampleDist = DetailSampleDist;
        bs.detailSampleMaxError = DetailSampleMaxError;
        bs.buildAll = true;
        bs.tiled = true;
        bs.tileSize = TileSize;
        return bs;
    }
}
