using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
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

    /// <summary>Returns portals whose source matches <paramref name="sourceMapId" />.</summary>
    IReadOnlyList<MapPortal> GetPortalsFrom(ushort sourceMapId);
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

    public IReadOnlyList<MapPortal> GetPortalsFrom(ushort sourceMapId)
        => _portals.Where(p => p.SourceMapId == sourceMapId).ToList();
}
