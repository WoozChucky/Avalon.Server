using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// Who a skill may damage and who it may heal (#164). World-side, because PvP is.
/// </summary>
public static class Hostility
{
    /// <summary>
    /// A creature is hostile to a player caster unless it is invulnerable. Two players are hostile only
    /// when both are flagged and the map is not a town. Nothing is hostile to itself. A creature caster
    /// finds nothing hostile: creature casting is #163. A character that is not the World-side entity
    /// is never hostile, which fails safe.
    /// </summary>
    public static bool IsHostile(IUnit caster, IUnit unit, MapType mapType)
    {
        if (ReferenceEquals(caster, unit) || caster.Guid == unit.Guid)
            return false;

        if (caster is not ICharacter)
            return false;

        return unit switch
        {
            ICreature creature => !creature.Invulnerable,
            CharacterEntity target => mapType != MapType.Town
                                      && caster is CharacterEntity { PvpEnabled: true }
                                      && target.PvpEnabled,
            _ => false,
        };
    }

    /// <summary>The caster itself, and every player not hostile to it. Creatures are never allies.</summary>
    public static bool IsAlly(IUnit caster, IUnit unit, MapType mapType)
    {
        if (ReferenceEquals(caster, unit) || caster.Guid == unit.Guid)
            return true;

        return caster is ICharacter && unit is ICharacter && !IsHostile(caster, unit, mapType);
    }
}
