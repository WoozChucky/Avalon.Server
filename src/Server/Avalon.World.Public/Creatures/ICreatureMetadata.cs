using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Creatures;

/// <summary>
/// A creature type's shared, read-only definition. Every creature of a type holds the same
/// instance, so this interface has no setters: per-creature state belongs on <see cref="ICreature"/>,
/// and a write here would change every creature of the type at once (#438).
/// </summary>
public interface ICreatureMetadata
{
    public CreatureTemplateId Id { get; }
    public float SpeedWalk { get; }
    public float SpeedRun { get; }
    public float SpeedSwim { get; }

    /// <summary>How dangerous this creature is, scaling its derived stats.</summary>
    CreatureRarity Rarity { get; }

    /// <summary>Scales the level-derived base health. 1.0 leaves it alone.</summary>
    float HealthModifier { get; }

    /// <summary>Scales the level-derived base damage range. 1.0 leaves it alone.</summary>
    float DamageModifier { get; }

    /// <summary>Scales the level-derived base experience. 1.0 leaves it alone.</summary>
    float ExperienceModifier { get; }

    /// <summary>
    /// Experience awarded to the killer. <c>null</c> means derive from base stats by level.
    /// </summary>
    uint? Experience { get; }

    /// <summary>How long before this creature re-spawns after death.</summary>
    TimeSpan RespawnTimer { get; }

    /// <summary>How long before this creature's corpse is removed from the world.</summary>
    TimeSpan BodyRemoveTimer { get; }

    /// <summary>Aggro radius. Creatures with 0 fall back to a script-defined default.</summary>
    float DetectionRange { get; }

    /// <summary>
    /// True for creatures that can never be damaged — town NPCs. See ICreature.Invulnerable for
    /// where the guard is applied.
    /// </summary>
    bool Invulnerable { get; }
}
