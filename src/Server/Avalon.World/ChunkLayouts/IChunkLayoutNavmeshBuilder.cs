using System.Diagnostics.CodeAnalysis;
using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using DotRecast.Detour;
using DotRecast.Recast.Toolset.Builder;
using DotRecast.Recast.Toolset.Geom;
using Microsoft.Extensions.Logging;

namespace Avalon.World.ChunkLayouts;

public interface IChunkLayoutNavmeshBuilder
{
    Task<DtNavMesh> BuildAsync(ChunkLayout layout, CancellationToken ct);
}

public class ChunkLayoutNavmeshBuilder : IChunkLayoutNavmeshBuilder
{
    private readonly ILogger<ChunkLayoutNavmeshBuilder> _logger;
    private readonly IChunkLibrary _library;

    public ChunkLayoutNavmeshBuilder(ILoggerFactory lf, IChunkLibrary library)
    {
        _logger = lf.CreateLogger<ChunkLayoutNavmeshBuilder>();
        _library = library;
    }

    public Task<DtNavMesh> BuildAsync(ChunkLayout layout, CancellationToken ct) =>
        Task.Run(() => BakeNavmesh(layout), ct);

    private DtNavMesh BakeNavmesh(ChunkLayout layout)
    {
        var combinedObjPath = ComposeCombinedObjToTempFile(layout);
        try
        {
            var geom = DemoInputGeomProvider.LoadFile(combinedObjPath);
            var builder = new TileNavMeshBuilder();
            var result = builder.Build(geom, NavmeshBuildSettings.Create());
            if (result?.NavMesh is null)
                throw new NavmeshBuildFailedException($"DotRecast returned null mesh for layout (seed {layout.Seed})");
            _logger.LogInformation("Navmesh baked for layout seed {Seed} ({ChunkCount} chunks)", layout.Seed, layout.Chunks.Count);
            return result.NavMesh;
        }
        finally
        {
            try { File.Delete(combinedObjPath); } catch { /* best-effort cleanup */ }
        }
    }

    [SuppressMessage("Performance", "MA0045",
        Justification = "CPU-bound navmesh bake runs on a Task.Run worker. " +
                        "File I/O is tiny (KB-sized local chunk objs) and dwarfed by DotRecast bake cost.")]
    private string ComposeCombinedObjToTempFile(ChunkLayout layout)
    {
        var sb = new System.Text.StringBuilder();
        int vOffset = 0;
        foreach (var chunk in layout.Chunks)
        {
            // Chunk objs are stored on disk by ChunkTemplate.Name (matches ChunkImporter
            // which copies <ExportDir>/<name>/chunk.obj → Maps/Chunks/<name>.obj). The
            // ChunkTemplateId is a DB surrogate key, NOT the filename — resolve it through
            // the in-memory chunk library.
            var name = _library.GetById(chunk.TemplateId).Name;
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Maps", "Chunks",
                $"{name}.obj");
            if (!File.Exists(path))
                throw new NavmeshBuildFailedException($"Chunk obj not found: {path}");
            int vCount = AppendTransformed(sb, File.ReadAllText(path), chunk.WorldPos, chunk.Rotation, vOffset);
            vOffset += vCount;
        }
        var tempPath = Path.Combine(Path.GetTempPath(), $"avalon-chunklayout-{layout.Seed}-{Guid.NewGuid():N}.obj");
        File.WriteAllText(tempPath, sb.ToString());
        return tempPath;
    }

    private static int AppendTransformed(System.Text.StringBuilder sb, string objText, Vector3 origin, byte rotation, int vOffset)
    {
        int vCount = 0;
        foreach (var raw in objText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("v "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float x = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                (float rx, float rz) = rotation switch
                {
                    0 => (x, z),
                    1 => (z, -x),
                    2 => (-x, -z),
                    3 => (-z, x),
                    _ => (x, z),
                };
                sb.Append("v ")
                  .Append((origin.x + rx).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                  .Append((origin.y + y).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                  .Append((origin.z + rz).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                vCount++;
            }
            else if (line.StartsWith("f "))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                sb.Append("f");
                for (int i = 1; i < parts.Length; i++)
                {
                    int idx = int.Parse(parts[i].Split('/')[0], System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append(' ').Append(idx + vOffset);
                }
                sb.Append('\n');
            }
        }
        return vCount;
    }

}
