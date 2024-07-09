using System.Drawing;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.World.Database.Repositories;
using Avalon.World.Maps.Virtualized;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public interface ICreatureSpawner
{
    Task LoadAsync();

    Creature Spawn(CreatureInfo virtualCreature);
    //Creature Spawn(CreatureTemplateId templateId);
}

public class CreatureSpawner : ICreatureSpawner
{
    private readonly ICreatureTemplateRepository _creatureTemplateRepository;
    private readonly ILogger<CreatureSpawner> _logger;
    private IEnumerable<CreatureTemplate> _templates;

    public CreatureSpawner(ILoggerFactory loggerFactory, ICreatureTemplateRepository creatureTemplateRepository)
    {
        _creatureTemplateRepository = creatureTemplateRepository;
        _logger = loggerFactory.CreateLogger<CreatureSpawner>();
        _templates = new List<CreatureTemplate>();
    }
    
    public async Task LoadAsync()
    {
        _templates = await _creatureTemplateRepository.FindAllAsync();
        
        _logger.LogInformation("Loaded {CreatureCount} creatures template from database", _templates.Count());
    }

    
    public Creature Spawn(CreatureInfo virtualCreature)
    {
        var creature = Spawn(virtualCreature.PrototypeIndex);

        creature.Position = virtualCreature.Position;

        creature.Bounds = new Rectangle(
            (int) virtualCreature.Position.x, 
            (int) virtualCreature.Position.z, 
            1, 
            1
        );
        
        return creature;
    }
    
    public Creature Spawn(CreatureTemplateId templateId)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            _logger.LogWarning("Could not find creature template {CreatureId}", templateId);
            throw new Exception($"Could not find creature template {templateId}");
        }

        var creature = new Creature
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            Name = template.Name,
            Bounds = new Rectangle(0, 0, 0, 0),
            Position = new Vector2(0, 0),
            Speed = template.SpeedWalk,
            Velocity = new Vector2(0, 0),
            ScriptName = template.ScriptName
        };
        
        return creature;
    }
}
