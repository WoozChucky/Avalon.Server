using DotRecast.Core;
using DotRecast.Detour.Io;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;
using DotRecast.Recast.Toolset.Geom;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps.Navigation;

public interface INavigationMeshBaker
{
    Task ExecuteAsync();
}

public class NavigationMeshBaker : INavigationMeshBaker
{
    private readonly ILogger<NavigationMeshBaker> _logger;
    
    private static readonly float CellSize = 0.3f;
    private static readonly float CellHeight = 0.2f;

    // Agent
    private static readonly float AgentHeight = 2.0f;
    private static readonly float AgentRadius = 0.6f;
    private static readonly float AgentMaxClimb = 0.9f;
    private static readonly float AgentMaxSlope = 45f;
    private static readonly float AgentMaxAcceleration = 8.0f;
    private static readonly float AgentMaxSpeed = 3.5f;

    // Region
    private static readonly int MinRegionSize = 8;
    private static readonly int MergedRegionSize = 20;

    // Filtering
    private static readonly bool FilterLowHangingObstacles = true;
    private static readonly bool FilterLedgeSpans = true;
    private static readonly bool FilterWalkableLowHeightSpans = true;

    // Polygonization
    private static readonly float EdgeMaxLen = 12f;
    private static readonly float EdgeMaxError = 1.3f;

    private static readonly int VertsPerPoly = 6;

    // Detail Mesh
    private static readonly float DetailSampleDist = 6f;
    private static readonly float DetailSampleMaxError = 1f;
    private static readonly int TileSize = 32;

    public NavigationMeshBaker(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<NavigationMeshBaker>();
    }

    public async Task ExecuteAsync()
    {
        var missingMeshes = GetMissingMeshes();
        if (missingMeshes.Count == 0) return;

        _logger.LogInformation("Found {MeshName} missing navigation meshes", missingMeshes.Count);

        var tasks = missingMeshes.Select(missingMesh => Task.Run(async () =>
            {
                _logger.LogInformation("Building navigation mesh from: {MeshName}", missingMesh);

                var path = Path.Combine(Directory.GetCurrentDirectory(), "Maps", missingMesh);
                var geometry = DemoInputGeomProvider.LoadFile(path);
                var builder = new TileNavMeshBuilder();
                var result = builder.Build(geometry, ToBuildSettings());

                var navMeshFileName = Path.Combine(Directory.GetCurrentDirectory(), "Maps", missingMesh.Replace(".obj", ".nav"));

                await using var fs = new FileStream(navMeshFileName, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
                await using var bw = new BinaryWriter(fs);
                var writer = new DtMeshSetWriter();
                writer.Write(bw, result.NavMesh, RcByteOrder.LITTLE_ENDIAN, true);
                await fs.FlushAsync();
            }))
            .ToList();

        await Task.WhenAll(tasks);
    }

    private IList<string> GetMissingMeshes()
    {
        var mapsFolder = Path.Combine(Directory.GetCurrentDirectory(), "Maps");
        
        var objFiles = Directory.GetFiles(mapsFolder, "*.obj");

        return objFiles.Where(objFile => !File.Exists(objFile.Replace(".obj", ".nav"))).ToList();
    }

    private static RcNavMeshBuildSettings ToBuildSettings()
    {
        var bs = new RcNavMeshBuildSettings();

        // Rasterization
        bs.cellSize = CellSize;
        bs.cellHeight = CellHeight;

        // Agent
        bs.agentHeight = AgentHeight;
        bs.agentHeight = AgentHeight;
        bs.agentRadius = AgentRadius;
        bs.agentMaxClimb = AgentMaxClimb;
        bs.agentMaxSlope = AgentMaxSlope;
        bs.agentMaxAcceleration = AgentMaxAcceleration;
        bs.agentMaxSpeed = AgentMaxSpeed;

        // Region
        bs.minRegionSize = MinRegionSize;
        bs.mergedRegionSize = MergedRegionSize;

        // Filtering
        bs.filterLowHangingObstacles = FilterLowHangingObstacles;
        bs.filterLedgeSpans = FilterLedgeSpans;
        bs.filterWalkableLowHeightSpans = FilterWalkableLowHeightSpans;

        // Polygonization
        bs.edgeMaxLen = EdgeMaxLen;
        bs.edgeMaxError = EdgeMaxError;
        bs.vertsPerPoly = VertsPerPoly;

        // Detail Mesh
        bs.detailSampleDist = DetailSampleDist;
        bs.detailSampleMaxError = DetailSampleMaxError;
        
        bs.buildAll = true;

        // Tiles
        bs.tiled = true;
        bs.tileSize = TileSize;

        return bs;
    }
}
