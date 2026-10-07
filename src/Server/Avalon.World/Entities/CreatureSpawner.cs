using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Dialogue;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public interface ICreatureSpawner
{
    ICreature Spawn(CreatureInfo virtualCreature);

    /// <summary>
    /// As <see cref="Spawn(CreatureInfo)" />, at <paramref name="level" /> (at least 1) instead of a roll from the
    /// template's MinLevel..MaxLevel: a procedural map's depth band (forest content pass). The creature keeps its
    /// template, so its identity, abilities, loot and quest credit are unchanged; its stats are derived at the level.
    /// </summary>
    ICreature Spawn(CreatureInfo virtualCreature, ushort level);
}

public class CreatureSpawner(ILoggerFactory loggerFactory, IWorld world) : ICreatureSpawner
{
    private readonly ILogger<CreatureSpawner> _logger = loggerFactory.CreateLogger<CreatureSpawner>();

    public ICreature Spawn(CreatureInfo virtualCreature) => Place(Spawn(virtualCreature.PrototypeIndex, level: null), virtualCreature);

    public ICreature Spawn(CreatureInfo virtualCreature, ushort level) =>
        Place(Spawn(virtualCreature.PrototypeIndex, Math.Max((ushort)1, level)), virtualCreature);

    private static ICreature Place(ICreature creature, CreatureInfo virtualCreature)
    {
        creature.Position = virtualCreature.Position;
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

    /// <summary>
    /// The template's body radius, or the default one when it is not a finite value above 0 (#164).
    /// A NaN body would never be hit and an infinite one would be hit by everything. The check
    /// constraint keeps such a row out of Postgres, but a template can still arrive another way.
    /// </summary>
    private float UsableBodyRadius(CreatureTemplate template)
    {
        if (float.IsFinite(template.BodyRadius) && template.BodyRadius > 0f)
            return template.BodyRadius;

        _logger.LogWarning(
            "Creature template {CreatureId} has body radius {BodyRadius}, which is not a finite value above 0; using {DefaultRadius}",
            template.Id, template.BodyRadius, UnitBody.DefaultCreatureRadius);
        return UnitBody.DefaultCreatureRadius;
    }

    public ICreature Spawn(CreatureTemplateId templateId) => Spawn(templateId, level: null);

    private ICreature Spawn(CreatureTemplateId templateId, ushort? level)
    {
        // Instance construction — the caller of Spawn — awaits a database read before reaching here,
        // so this runs on a thread-pool thread, not the tick thread. Read the creatures area once
        // into a local: two separate property reads (CreatureTemplates, then CreatureStats) could
        // each observe a different generation if a reload lands in between, pairing a new template
        // with the old deriver or the reverse.
        CreaturesPatch creatures = world.Data.Creatures;

        // Dialogue is its own reload area, published as its own reference, so it is read once too.
        // Whether this creature can be interacted with is fixed here, at spawn: a later
        // /reload dialogue reaches only creatures spawned after it, and one already standing keeps
        // the flag it spawned with. Creatures never respawn and the town instance persists, so a
        // town NPC's flag stays as it was until the server restarts.
        IDialogueCatalog dialogue = world.Data.Dialogue;

        // The combat area too (#627): the haste cap and movement bounds are fixed at spawn, like its stats.
        CombatFormula formula = world.Data.Combat.Formula;

        CreatureTemplate? template = creatures.Templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
        {
            _logger.LogWarning("Could not find creature template {CreatureId}", templateId);
            throw new Exception($"Could not find creature template {templateId}");
        }

        ushort rolled = level ?? RollLevel(template);
        DerivedCreatureStats stats = creatures.Stats.Derive(template, rolled);

        var creature = new Creature
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
            CanInteract = NpcInteraction.CanInteract(dialogue, template.Id),
            Rarity = template.Rarity,
            BodyRadius = UsableBodyRadius(template),
            MoveState = MoveState.Idle,
            Level = stats.Level,
            Health = stats.Health,
            BaseMaxHealth = stats.Health,
            CurrentHealth = stats.Health,
            DamageMin = stats.DamageMin,
            DamageMax = stats.DamageMax,
            Experience = stats.Experience,
            Armor = stats.Armor,
            CritPct = stats.CritPct,
            DodgePct = stats.DodgePct,
            BlockPct = stats.BlockPct,
            BaseAttackTime = template.BaseAttackTime,
            HasteCap = formula.HasteCap,
            MoveSpeedFloor = formula.MoveSpeedFloor,
            MoveSpeedCap = formula.MoveSpeedCap,

            // A creature's casts are free (#163): it has no pool, so mana stays out of the derivation entirely.
            Power = 0,
            CurrentPower = 0
        };

        return creature;
    }
}
