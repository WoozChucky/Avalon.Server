using DotRecast.Core;
using DotRecast.Detour.Io;
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
                var result = builder.Build(geometry, NavmeshBuildSettings.Create());

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
}
