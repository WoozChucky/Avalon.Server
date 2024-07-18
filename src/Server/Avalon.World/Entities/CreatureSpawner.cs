using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Database.Repositories;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public interface ICreatureSpawner
{
    Task LoadAsync();

    ICreature Spawn(CreatureInfo virtualCreature);
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

    
    public ICreature Spawn(CreatureInfo virtualCreature)
    {
        var creature = Spawn(virtualCreature.PrototypeIndex);

        creature.Position = virtualCreature.Position;
        creature.Metadata.StartPosition = virtualCreature.Position;
        
        return creature;
    }
    
    public ICreature Spawn(CreatureTemplateId templateId)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            _logger.LogWarning("Could not find creature template {CreatureId}", templateId);
            throw new Exception($"Could not find creature template {templateId}");
        }

        var creature = new Creature
        {
            Id = IGameEntity.GenerateId(),
            TemplateId = template.Id,
            Metadata = template,
            Name = template.Name,
            Position = new Vector2(0, 0),
            Speed = template.SpeedWalk,
            Velocity = new Vector2(0, 0),
            ScriptName = template.ScriptName,
            MoveState = MoveState.Idle,
            Level = 1,
            Health = 100,
            CurrentHealth = 100,
            Power = 0,
            CurrentPower = 0
        };
        
        return creature;
    }
}
