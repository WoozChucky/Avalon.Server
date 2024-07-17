using System.Runtime.CompilerServices;
using Avalon.Domain.World;
using Avalon.World.Database.Repositories;
using Avalon.World.Maps.Virtualized;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public interface IAvalonMapManager
{
    Task LoadAsync();
    IAsyncEnumerable<(VirtualizedMap map, MapTemplate metadata)> EnumerateOpenWorldAsync(CancellationToken token = default);
}

public class AvalonMapManager : IAvalonMapManager
{
    private readonly ILogger<AvalonMapManager> _logger;
    private readonly IMapTemplateRepository _mapTemplateRepository;

    private readonly ReaderWriterLockSlim _lock;

    // Map template loaded from database
    private IList<MapTemplate> _mapTemplates = null!;

    public AvalonMapManager(ILoggerFactory loggerFactory, IMapTemplateRepository mapTemplateRepository)
    {
        _logger = loggerFactory.CreateLogger<AvalonMapManager>();
        _mapTemplateRepository = mapTemplateRepository;
        
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    }
    
    public async Task LoadAsync()
    {
        _logger.LogInformation("Loading maps...");

        _mapTemplates = await _mapTemplateRepository.FindAllAsync();
        
        _logger.LogInformation("Loaded {MapCount} maps from database", _mapTemplates.Count());
    }
    
    public async IAsyncEnumerable<(VirtualizedMap map, MapTemplate metadata)> EnumerateOpenWorldAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        var tasks = _mapTemplates
            .Where(m => m.InstanceType == MapInstanceType.OpenWorld)
            .Select(template => LoadMapAsync(template, token))
            .ToList();
        
        if (token.IsCancellationRequested)
        {
            yield break;
        }

        while (tasks.Count != 0)
        {
            // Wait for any of the tasks to complete
            var completedTask = await Task.WhenAny(tasks);
        
            // Remove the completed task from the list
            tasks.Remove(completedTask);
            
            // Yield the result of the completed task
            var virtualizedMap = await completedTask;
            
            if (token.IsCancellationRequested)
            {
                yield break;
            }
            
            yield return (virtualizedMap, _mapTemplates.First(m => m.Id == virtualizedMap.Id));
        }
    }

    private async Task<VirtualizedMap> LoadMapAsync(MapTemplate template, CancellationToken token)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), template.Directory, template.Name);
        return await BinaryDeserializationHelper.ReadMapFromFile(path, token);
    }

}
