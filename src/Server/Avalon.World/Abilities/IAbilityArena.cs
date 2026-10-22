using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Combat;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

/// <summary>
/// What a shape script may use (#164). World-side on purpose: it is not part of the modding API.
/// <c>MapInstance</c> implements it.
/// </summary>
public interface IAbilityArena
{
    /// <summary>A town never allows player hostility.</summary>
    MapType MapType { get; }

    ICombatService CombatService { get; }

    /// <summary>
    /// An ability's direct damage on one unit (its EffectValue, scaled), through this instance's combat service,
    /// answering how it went: only a hit that landed applies the ability's aura (auras).
    /// </summary>
    HitOutcome DamageForAbility(IUnit caster, IUnit target, IAbility ability);

    /// <summary>An ability's direct heal on one unit, through this instance's combat service (auras).</summary>
    void HealForAbility(IUnit caster, IUnit target, IAbility ability);

    /// <summary>
    /// The aura the ability's row names, if any, on one unit, from <paramref name="caster" /> (auras). Read from the live
    /// ability catalog, so the modding API's ability metadata never carries one.
    /// </summary>
    void ApplyAbilityAura(IUnit caster, IUnit target, IAbility ability);

    /// <summary>The instance's living units by shape.</summary>
    IHitQuery Hits { get; }

    /// <summary>
    /// How many world ticks <see cref="Hits" /> is rewound (#1082): 0, the present, except while the cast system fires a
    /// player's instant cast, when it is that cast's <see cref="CastView.HitRewindTicks" />. The cast system saves the
    /// value it finds and puts it back after, so a cast fired inside another's hit keeps the outer one's once it returns.
    /// </summary>
    int HitRewindTicks { get; set; }

    /// <summary>
    /// The one unit <see cref="Hits" /> tests at its present position under a rewind (#1082): the caster of the cast being
    /// fired, whose own inputs reach the server in order before its cast, so where it stands now is where it stood on its
    /// own screen (a self-including Ally circle must not test it where it was ticks ago). Set and put back with
    /// <see cref="HitRewindTicks" />, as a pair.
    /// </summary>
    IUnit? HitRewindExempt { get; set; }

    /// <summary>
    /// The footprint of the telegraphed cast being fired (#1082): set by the cast system only around a queued hostile
    /// circle's or cone's <c>Prepare</c>, null otherwise, and put back after as the rewind is. While set, a player it hits
    /// is judged by that player's screen (<see cref="TryDeferHit" />).
    /// </summary>
    /// <remarks>
    /// It is the footprint the cast system resolves from the ability's row, its aim and its origin
    /// (<see cref="AbilityFootprint" />), not the area the script hits: the two are the same only because the shipped
    /// circle and cone scripts fire with that same footprint. A script that hit elsewhere through
    /// <see cref="AbilityEffect.ApplyToAll" /> would have each player it holds judged against this footprint when the hit
    /// falls due, and one outside it would be a silent dodge.
    /// </remarks>
    AbilityFootprint? DodgeFootprint { get; set; }

    /// <summary>
    /// Holds <paramref name="ability" />'s hit on <paramref name="target" /> for the dodge wait (#1082), answering whether it
    /// did. True only while <see cref="DodgeFootprint" /> is set, for a character target with a one-way delay of at least a
    /// tick (at most <see cref="LagCompensation.MaxDodgeWaitTicks" />): the hit is then applied once the delay has passed,
    /// if the target is still alive and inside the footprint. False, to be applied now, for any other.
    /// </summary>
    bool TryDeferHit(IUnit caster, IAbility ability, IUnit target);

    IMapNavigator GetNavigatorForPosition(Vector3 position);

    /// <summary>
    /// Tells every client that <paramref name="caster" /> started a cast-time cast (#521 item 9), with its cast id
    /// and, when it has one, the footprint it will land on (#648). Sent by the cast system once it has taken the
    /// cast; the cast time is read from the ability.
    /// </summary>
    void BroadcastUnitStartCast(IUnit caster, IAbility ability, uint castId, AbilityFootprint? footprint);

    /// <summary>
    /// Tells every client that hears it that an ability fired, so it can draw it: the whole footprint (#648), a
    /// projectile's lane included (#479), with the id of the cast firing it (<see cref="CastInFlight" />). A
    /// projectile's flight itself is a world object and uses entity replication.
    /// </summary>
    void BroadcastAbilityFired(IUnit caster, IAbility ability, AbilityFootprint footprint);

    /// <summary>
    /// Tells every client that hears <paramref name="position" /> that a projectile struck a unit or ended (#479),
    /// on the tick it happened. Sent before the damage of a hit, so the damage that follows belongs to it.
    /// </summary>
    void BroadcastProjectileImpact(ObjectGuid projectile, IUnit caster, IAbility ability, uint castId,
        Vector3 position, ProjectileImpactReason reason, IUnit? target);

    /// <summary>
    /// The id of the cast whose script the cast system is firing now (#648), set only for that call, 0 otherwise,
    /// so a fired broadcast names its cast without the script knowing it, and a projectile keeps it for its flight
    /// (#479).
    /// </summary>
    uint CastInFlight { get; set; }

    /// <summary>
    /// The footprint the cast being fired now was started with, set only for that call (#479): a queued cast's, which
    /// its start broadcast already resolved, so its script need not resolve it again. Null for an instant cast.
    /// </summary>
    AbilityFootprint? FootprintInFlight { get; set; }

    /// <summary>The finish-cast broadcast, as <c>ISimulationContext.BroadcastFinishCast</c> (#546), with its cast id (#648).</summary>
    void BroadcastFinishCast(IUnit caster, IAbility ability, uint castId);

    /// <summary>The interrupt broadcast, as <c>ISimulationContext.BroadcastInterruptedCast</c> (#546), with its cast id (#648).</summary>
    void BroadcastInterruptedCast(IUnit caster, IAbility ability, uint castId);
}
