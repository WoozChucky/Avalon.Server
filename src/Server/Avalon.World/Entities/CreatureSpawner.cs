using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public interface ICreatureSpawner
{
    ICreature Spawn(CreatureInfo virtualCreature);
}

public class CreatureSpawner(ILoggerFactory loggerFactory, IWorld world) : ICreatureSpawner
{
    private readonly ILogger<CreatureSpawner> _logger = loggerFactory.CreateLogger<CreatureSpawner>();

    public ICreature Spawn(CreatureInfo virtualCreature)
    {
        ICreature creature = Spawn(virtualCreature.PrototypeIndex);

        creature.Position = virtualCreature.Position;
        creature.Metadata.StartPosition = virtualCreature.Position;

        return creature;
    }

    /// <summary>
    /// A level from the template's own range. Clamped at 1 below, and to at least the minimum above, so
    /// seed data with the two the wrong way round cannot produce an empty range.
    /// </summary>
    private static ushort RollLevel(CreatureTemplate template)
    {
        short min = Math.Max((short)1, template.MinLevel);
        short max = Math.Max(min, template.MaxLevel);

        return (ushort)Random.Shared.Next(min, max + 1);
    }

    public ICreature Spawn(CreatureTemplateId templateId)
    {
        // Instance construction — the caller of Spawn — awaits a database read before reaching here,
        // so this runs on a thread-pool thread, not the tick thread. Read the creatures area once
        // into a local: two separate property reads (CreatureTemplates, then CreatureStats) could
        // each observe a different generation if a reload lands in between, pairing a new template
        // with the old deriver or the reverse.
        CreaturesPatch creatures = world.Data.Creatures;

        CreatureTemplate? template = creatures.Templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            _logger.LogWarning("Could not find creature template {CreatureId}", templateId);
            throw new Exception($"Could not find creature template {templateId}");
        }

        ushort level = RollLevel(template);
        DerivedCreatureStats stats = creatures.Stats.Derive(template, level);

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
            Invulnerable = template.Invulnerable,
            MoveState = MoveState.Idle,
            Level = stats.Level,
            Health = stats.Health,
            CurrentHealth = stats.Health,
            DamageMin = stats.DamageMin,
            DamageMax = stats.DamageMax,
            Experience = stats.Experience,

            // Creatures cannot cast, so mana stays out of the derivation entirely.
            Power = 0,
            CurrentPower = 0
        };

        return creature;
    }
}
