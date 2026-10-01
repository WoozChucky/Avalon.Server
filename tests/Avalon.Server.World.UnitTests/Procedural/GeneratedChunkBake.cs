using System.Globalization;
using System.Text;
using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// Bakes generated chunk geometry the way ChunkLayoutNavmeshBuilder does (unrotated, cell 30, the shared build
/// settings), from obj text rather than the working directory, which other tests change.
/// </summary>
internal static class GeneratedChunkBake
{
    public static MapNavigator Bake(IEnumerable<(string Obj, int GridX, int GridZ)> chunks)
    {
        var sb = new StringBuilder();
        int offset = 0;
        foreach ((string obj, int gridX, int gridZ) in chunks)
        {
            var origin = new Vector3(gridX * 30f, 0f, gridZ * 30f);
            int count = 0;
            foreach (string raw in obj.Split('\n'))
            {
                string line = raw.Trim();
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    Vector3 w = ChunkRotation.LocalToWorld(float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture), 0, 30f, origin);
                    sb.Append(CultureInfo.InvariantCulture, $"v {w.x} {w.y} {w.z}\n");
                    count++;
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    sb.Append('f');
                    for (int i = 1; i < parts.Length; i++)
                        sb.Append(' ').Append(int.Parse(parts[i], CultureInfo.InvariantCulture) + offset);
                    sb.Append('\n');
                }
            }

            offset += count;
        }

        string path = Path.Combine(Path.GetTempPath(), $"avalon-generated-chunks-{Guid.NewGuid():N}.obj");
        File.WriteAllText(path, sb.ToString());
        try
        {
            var result = new TileNavMeshBuilder().Build(RcSampleInputGeomProvider.LoadFile(path), NavmeshBuildSettings.Create());
            Assert.NotNull(result?.NavMesh);
            var navigator = new MapNavigator(NullLoggerFactory.Instance);
            navigator.LoadFromNavMesh(result!.NavMesh);
            return navigator;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
