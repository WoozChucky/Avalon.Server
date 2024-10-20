using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
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

public class CreatureSpawner(ILoggerFactory loggerFactory, ICreatureTemplateRepository creatureTemplateRepository)
    : ICreatureSpawner
{
    private readonly ILogger<CreatureSpawner> _logger = loggerFactory.CreateLogger<CreatureSpawner>();
    private IEnumerable<CreatureTemplate> _templates = new List<CreatureTemplate>();

    public async Task LoadAsync()
    {
        _templates = await creatureTemplateRepository.FindAllAsync();

        _logger.LogInformation("Loaded {CreatureCount} creatures template from database", _templates.Count());
    }


    public ICreature Spawn(CreatureInfo virtualCreature)
    {
        ICreature creature = Spawn(virtualCreature.PrototypeIndex);

        creature.Position = virtualCreature.Position;
        creature.Metadata.StartPosition = virtualCreature.Position;

        return creature;
    }

    public ICreature Spawn(CreatureTemplateId templateId)
    {
        CreatureTemplate? template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            _logger.LogWarning("Could not find creature template {CreatureId}", templateId);
            throw new Exception($"Could not find creature template {templateId}");
        }

        Creature creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, IObject.GenerateId()),
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
