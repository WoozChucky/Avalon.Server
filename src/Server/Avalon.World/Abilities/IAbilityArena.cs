using Avalon.Common.Mathematics;
using Avalon.World.Abilities.Targeting;
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

    /// <summary>The instance's living units by shape.</summary>
    IHitQuery Hits { get; }

    IMapNavigator GetNavigatorForPosition(Vector3 position);

    /// <summary>
    /// Tells every client that <paramref name="caster" /> started a cast-time cast (#521 item 9), with its cast id
    /// and, when it has one, the footprint it will land on (#648). Sent by the cast system once it has taken the
    /// cast; the cast time is read from the ability.
    /// </summary>
    void BroadcastUnitStartCast(IUnit caster, IAbility ability, uint castId, AbilityFootprint? footprint);

    /// <summary>
    /// Tells every client in the instance that a circle or cone fired, so it can draw it: the whole footprint
    /// (#648), with the id of the cast firing it (<see cref="CastInFlight" />). Projectiles are world objects and
    /// use entity replication.
    /// </summary>
    void BroadcastAbilityFired(IUnit caster, IAbility ability, AbilityFootprint footprint);

    /// <summary>
    /// The id of the cast whose script the cast system is firing now (#648), set only for that call, 0 otherwise,
    /// so a fired broadcast names its cast without the script knowing it.
    /// </summary>
    uint CastInFlight { set; }

    /// <summary>The finish-cast broadcast, as <c>ISimulationContext.BroadcastFinishCast</c> (#546), with its cast id (#648).</summary>
    void BroadcastFinishCast(IUnit caster, IAbility ability, uint castId);

    /// <summary>The interrupt broadcast, as <c>ISimulationContext.BroadcastInterruptedCast</c> (#546), with its cast id (#648).</summary>
    void BroadcastInterruptedCast(IUnit caster, IAbility ability, uint castId);
}
