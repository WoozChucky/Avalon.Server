using System.Runtime.CompilerServices;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Maps.Virtualized;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public interface IAvalonMapManager
{
    /// <summary>All map templates loaded from the database.</summary>
    IReadOnlyList<MapTemplate> Templates { get; }

    /// <summary>All portal definitions loaded from the database.</summary>
    IReadOnlyList<MapPortal> Portals { get; }

    Task LoadAsync();

    /// <summary>Loads the binary map data for a given template.</summary>
    Task<VirtualizedMap> LoadMapDataAsync(MapTemplate template, CancellationToken token = default);

    /// <summary>Returns portals whose source matches <paramref name="sourceMapId" />.</summary>
    IReadOnlyList<MapPortal> GetPortalsFrom(ushort sourceMapId);

    // Used by World.LoadAsync to start Town instances at startup.
    IAsyncEnumerable<(VirtualizedMap map, MapTemplate metadata)> EnumerateTownMapsAsync(
        CancellationToken token = default);
}

public class AvalonMapManager(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger = loggerFactory.CreateLogger<AvalonMapManager>();
    private List<MapPortal> _portals = [];

    private List<MapTemplate> _templates = [];

    public IReadOnlyList<MapTemplate> Templates => _templates;
    public IReadOnlyList<MapPortal> Portals => _portals;

    public async Task LoadAsync()
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IMapTemplateRepository mapRepo = scope.ServiceProvider.GetRequiredService<IMapTemplateRepository>();
        IMapPortalRepository portalRepo = scope.ServiceProvider.GetRequiredService<IMapPortalRepository>();

        _logger.LogInformation("Loading map templates...");
        _templates = (await mapRepo.FindAllAsync()).ToList();
        _logger.LogInformation("Loaded {Count} map templates", _templates.Count);

        _logger.LogInformation("Loading map portals...");
        _portals = (await portalRepo.FindAllAsync()).ToList();
        _logger.LogInformation("Loaded {Count} map portals", _portals.Count);
    }

    public async Task<VirtualizedMap> LoadMapDataAsync(MapTemplate template, CancellationToken token = default)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), template.Directory, template.Name);
        return await BinaryDeserializationHelper.ReadMapFromFile(path, token);
    }

    public IReadOnlyList<MapPortal> GetPortalsFrom(ushort sourceMapId)
        => _portals.Where(p => p.SourceMapId == sourceMapId).ToList();

    public async IAsyncEnumerable<(VirtualizedMap map, MapTemplate metadata)> EnumerateTownMapsAsync(
        [EnumeratorCancellation] CancellationToken token = default)
    {
        List<MapTemplate> townTemplates = _templates.Where(m => m.MapType == MapType.Town).ToList();

        List<Task<VirtualizedMap>> tasks = townTemplates
            .Select(template => LoadMapDataAsync(template, token))
            .ToList();

        while (tasks.Count != 0)
        {
            if (token.IsCancellationRequested)
            {
                yield break;
            }

            Task<VirtualizedMap> completedTask = await Task.WhenAny(tasks);
            tasks.Remove(completedTask);

            VirtualizedMap virtualizedMap = await completedTask;
            MapTemplate metadata = townTemplates.First(m => m.Id == virtualizedMap.Id);

            yield return (virtualizedMap, metadata);
        }
    }
}
