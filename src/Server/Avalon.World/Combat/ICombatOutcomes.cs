using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;
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
    /// <paramref name="abilityId" /> (null for a swing), and everyone in the instance is sent the hit, each
    /// marked with how the hit went (#506).
    /// </summary>
    void CharacterDamaged(CharacterEntity character, IUnit attacker, uint damage, AbilityId? abilityId, HitResult result);

    /// <summary>
    /// A hit on a living <paramref name="target" /> was dodged (#506): it is sent like a hit, with 0 and
    /// <see cref="HitResult.Dodged" />, though nothing was dealt and the target's script is not told.
    /// </summary>
    void HitDodged(IUnit attacker, IUnit target, AbilityId? abilityId);

    /// <summary>
    /// How the hit whose creature script is running now went (#506), set by the combat service for exactly
    /// the length of that script's OnHit: the hit the script broadcasts through
    /// <c>ISimulationContext.BroadcastUnitHit</c> is marked with it. None outside a hit.
    /// </summary>
    HitResult HitInFlight { set; }

    /// <summary>
    /// The hit brought a living creature to 0 health: the corpse, its loot and the killer's experience.
    /// Reported before the encounter hears of the death and before the death broadcast.
    /// </summary>
    void CreatureKilled(ICreature creature, IUnit killer);
}
