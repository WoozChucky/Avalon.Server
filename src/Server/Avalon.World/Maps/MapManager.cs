using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public interface IAvalonMapManager
{
    /// <summary>All map templates loaded from the database.</summary>
    IReadOnlyList<MapTemplate> Templates { get; }

    Task LoadAsync();
}

public class AvalonMapManager(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger = loggerFactory.CreateLogger<AvalonMapManager>();

    private List<MapTemplate> _templates = [];

    public IReadOnlyList<MapTemplate> Templates => _templates;

    public async Task LoadAsync()
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IMapTemplateRepository mapRepo = scope.ServiceProvider.GetRequiredService<IMapTemplateRepository>();

        _logger.LogInformation("Loading map templates...");
        _templates = (await mapRepo.FindAllAsync()).ToList();
        _logger.LogInformation("Loaded {Count} map templates", _templates.Count);
    }
}
