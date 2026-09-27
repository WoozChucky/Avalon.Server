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
    /// Tells every client in the instance that a circle or cone fired, so it can draw it. A circle sends
    /// its centre, a cone its direction. Projectiles are world objects and use entity replication.
    /// </summary>
    void BroadcastAbilityFired(IUnit caster, IAbility ability, Vector3 origin, Vector3? direction, Vector3? centre);

    /// <summary>The finish-cast broadcast, as <c>ISimulationContext.BroadcastFinishCast</c> (#546).</summary>
    void BroadcastFinishCast(IUnit caster, IAbility ability);

    /// <summary>The interrupt broadcast, as <c>ISimulationContext.BroadcastInterruptedCast</c> (#546).</summary>
    void BroadcastInterruptedCast(IUnit caster, IAbility ability);
}
