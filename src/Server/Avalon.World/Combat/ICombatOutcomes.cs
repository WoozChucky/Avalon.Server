using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

/// <summary>
/// What a hit led to, reported by <see cref="CombatService" /> to its own instance right after the
/// target's <c>OnHit</c> returns (#546). World-side on purpose, like <c>IAbilityArena</c> and
/// <c>IVendorHost</c>: a kill grants loot and experience, so no part of the modding API may raise one.
/// <c>MapInstance</c> implements it.
/// </summary>
public interface ICombatOutcomes
{
    /// <summary>
    /// A living character took <paramref name="damage" />: it is sent its own damage packet, naming
    /// <paramref name="abilityId" /> (null for a swing), and everyone in the instance is sent the hit.
    /// </summary>
    void CharacterDamaged(CharacterEntity character, IUnit attacker, uint damage, AbilityId? abilityId);

    /// <summary>
    /// The hit brought a living creature to 0 health: the corpse, its loot and the killer's experience.
    /// Reported before the encounter hears of the death and before the death broadcast.
    /// </summary>
    void CreatureKilled(ICreature creature, IUnit killer);
}
