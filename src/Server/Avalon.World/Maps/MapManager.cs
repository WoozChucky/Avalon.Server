using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Reload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public interface IAvalonMapManager
{
    /// <summary>
    /// All map templates loaded from the database. One of them is found with <see cref="MapTemplateRows.FindById(IReadOnlyList{MapTemplate}, Avalon.Common.ValueObjects.MapTemplateId)" />.
    /// </summary>
    IReadOnlyList<MapTemplate> Templates { get; }

    Task LoadAsync();
}

public class AvalonMapManager(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
    : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger = loggerFactory.CreateLogger<AvalonMapManager>();

    // The templates and their lookup by id, one object (#883): replaced whole by the one load, never changed.
    private KeyedRows<ushort, MapTemplate> _templates = MapTemplateRows.Keyed([]);

    public IReadOnlyList<MapTemplate> Templates => _templates;

    public async Task LoadAsync()
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IMapTemplateRepository mapRepo = scope.ServiceProvider.GetRequiredService<IMapTemplateRepository>();

        _logger.LogInformation("Loading map templates...");
        _templates = MapTemplateRows.Keyed(await mapRepo.FindAllAsync());
        _logger.LogInformation("Loaded {Count} map templates", _templates.Count);
    }
}
