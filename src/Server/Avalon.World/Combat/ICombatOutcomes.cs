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
    /// A living character took a hit of <paramref name="damage" />, the whole hit, its ward's part included (#1178): it is
    /// sent its own damage packet, naming <paramref name="abilityId" /> (null for a swing), and everyone in the instance is
    /// sent the hit, each marked with how the hit went (#506) in <paramref name="report" />.
    /// </summary>
    void CharacterDamaged(CharacterEntity character, IUnit attacker, uint damage, AbilityId? abilityId, in HitReport report);

    /// <summary>
    /// A hit of <paramref name="type" /> on a living <paramref name="target" /> was dodged (#506): it is sent like a hit,
    /// with 0 and <see cref="HitResult.Dodged" />, though nothing was dealt and the target's script is not told.
    /// <paramref name="types" /> are the parts it would have dealt (#1178), its base parts above 0: None for an ability that
    /// deals no damage, whose <paramref name="type" /> is still its own or its attacker's.
    /// </summary>
    void HitDodged(IUnit attacker, IUnit target, AbilityId? abilityId, DamageType type, DamageTypeMask types);

    /// <summary>
    /// <paramref name="target" /> resisted the crowd control of <paramref name="caster" />'s ability whole (#770): a
    /// creature immune to it, a character at the diminishing step that refuses it, or a creature walking home. It is
    /// sent like a dodge, with 0 and <see cref="HitResult.Immune" />, to whoever would have seen the hit land.
    /// </summary>
    void ControlResisted(IUnit caster, IUnit target, AbilityId? abilityId);

    /// <summary>
    /// A heal restored <paramref name="restored" /> health, more than 0, to a living <paramref name="target" />
    /// (#506): sent to the healer, the target, and everyone within the interest radius of the target, marked
    /// Crit for a critical heal.
    /// </summary>
    void UnitHealed(IUnit healer, IUnit target, uint restored, AbilityId? abilityId, HitResult result);

    /// <summary>
    /// How the hit whose creature script is running now went (#506, #1178), set by the combat service for exactly
    /// the length of that script's OnHit: the hit the script broadcasts through
    /// <c>ISimulationContext.BroadcastUnitHit</c> is marked with it, and the ward's part is added back to the damage the
    /// script was handed, so watchers see the whole hit. None outside a hit.
    /// </summary>
    HitReport HitInFlight { set; }

    /// <summary>
    /// The hit brought a living creature to 0 health: the corpse, its loot and the killer's experience. Reported before
    /// the encounter hears of the death and before the death broadcast. <paramref name="killer" /> is null for an
    /// aura's tick whose caster is gone (auras): nobody is credited, and every drop is free for all at once.
    /// </summary>
    void CreatureKilled(ICreature creature, IUnit? killer);

    /// <summary>
    /// An aura's tick dealt or restored <paramref name="amount" /> (auras): a character hurt is sent its own damage
    /// packet, and everyone who hears it the hit or the heal, each naming <paramref name="aura" />. A damage tick's amount
    /// is the whole tick, its ward's part included (#1178). A tick worth less than a whole point is not reported, and a
    /// heal only when it restored more than 0. <paramref name="caster" /> is null when nobody applied it, or its caster is
    /// gone. A heal's <paramref name="report" /> carries no type.
    /// </summary>
    void PeriodicTick(IUnit? caster, IUnit target, uint amount, AuraId aura, in HitReport report, bool heal);

    /// <summary>A unit died (auras): every death passes through the combat service, and death ends every aura.</summary>
    void UnitDied(IUnit unit);

    /// <summary>
    /// A hit or an aura's tick dealt more than 0 to <paramref name="unit" />, which is still alive (#770), reported once
    /// per hit or tick: each aura it holds rolls its chance to break on damage.
    /// </summary>
    void DamageTaken(IUnit unit);
}
