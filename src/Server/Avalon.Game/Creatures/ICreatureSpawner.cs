using System.Drawing;
using System.Numerics;
using Avalon.Database;
using Avalon.Database.World.Model;
using Avalon.Game.Maps.Virtual;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Creatures;

public interface ICreatureSpawner
{
    void LoadCreatures();

    Creature Spawn(MapCreature virtualCreature);
    Creature Spawn(int templateId);
}

public class CreatureSpawner : ICreatureSpawner
{
    private readonly ILogger<CreatureSpawner> _logger;
    private readonly IDatabaseManager _databaseManager;
    private IEnumerable<CreatureTemplate> _templates;

    public CreatureSpawner(ILogger<CreatureSpawner> logger, IDatabaseManager databaseManager)
    {
        _logger = logger;
        _databaseManager = databaseManager;
        _templates = new List<CreatureTemplate>();
    }
    
    public void LoadCreatures()
    {
        _templates = _databaseManager.World.CreatureTemplate.QueryAllAsync().GetAwaiter().GetResult();
        
        _logger.LogInformation("Loaded {CreatureCount} creatures template from database", _templates.Count());
    }

    public Creature Spawn(MapCreature virtualCreature)
    {
        var creature = Spawn(virtualCreature.TemplateId);

        creature.Position = new Vector2(
            virtualCreature.Position.X, 
            virtualCreature.Position.Y
        );

        creature.Bounds = new Rectangle(
            virtualCreature.Bounds.X, 
            virtualCreature.Bounds.Y,
            virtualCreature.Bounds.Width, 
            virtualCreature.Bounds.Height
        );
        
        return creature;
    }

    public Creature Spawn(int templateId)
    {
        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            _logger.LogWarning("Could not find creature template {CreatureId}", templateId);
            throw new Exception($"Could not find creature template {templateId}");
        }

        var creature = new Creature()
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
