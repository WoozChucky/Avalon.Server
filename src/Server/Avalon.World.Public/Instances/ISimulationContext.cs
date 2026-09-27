using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Instances;

/// <summary>
/// The simulation context that AI scripts and the spell system interact with at runtime.
/// Implemented by <c>MapInstance</c>, which is the sole simulation unit for a running map.
/// </summary>
public interface ISimulationContext
{
    IReadOnlyDictionary<ObjectGuid, ICharacter> Characters { get; }
    IReadOnlyDictionary<ObjectGuid, ICreature> Creatures { get; }

    /// <summary>Per-instance combat service: damage/heal/threat application and encounter lifecycle.</summary>
    ICombatService CombatService { get; }

    /// <summary>Returns the navigator whose bounds contain <paramref name="position"/>.</summary>
    IMapNavigator GetNavigatorForPosition(Vector3 position);

    /// <summary>
    ///     Moves creatures. Scripts set a destination through this rather than writing
    ///     <c>Position</c>, so the same script works under either locomotion implementation.
    /// </summary>
    ICreatureLocomotion Locomotion { get; }

    /// <summary>Standing positions around a target, so attackers surround it rather than stack.</summary>
    IMeleeSlots MeleeSlots { get; }

    /// <summary>
    /// Queues a cast-time ability aimed at <paramref name="aim" />, captured now (#164). False, with
    /// nothing spent and <c>Casting</c> left clear, when the cast cannot be taken.
    /// </summary>
    bool QueueAbility(ICharacter caster, AbilityAim aim, IAbility ability);

    /// <summary>
    /// Fires an instant ability this tick, aimed at <paramref name="aim" />. False, with nothing spent,
    /// when the cast cannot run (its script is missing or cannot be built, or the cost cannot be paid).
    /// </summary>
    bool RunInstantAbility(IUnit caster, AbilityAim aim, IAbility ability);

    void AddCreature(ICreature creature);
    void RemoveCreature(ICreature creature);
    void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage);

    /// <summary>
    /// Tells every client in the instance that <paramref name="caster" /> started casting
    /// <paramref name="ability" /> (#521 item 9). The cast time is read from the ability, so the id and
    /// the time always come from the same one.
    /// </summary>
    void BroadcastUnitStartCast(IUnit caster, IAbility ability);

    /// <summary>
    /// Tells every client in the instance that <paramref name="attacker" /> swung, with
    /// <paramref name="ability" />'s animation, or the plain melee one when it is null. Sent only for a
    /// unit in this instance (#546): a unit's broadcasts go through its own instance.
    /// </summary>
    void BroadcastAttackAnimation(IUnit attacker, IAbility? ability);

    /// <summary>
    /// Tells every client in the instance that <paramref name="caster" /> finished casting
    /// <paramref name="ability" />. Sent only for a unit in this instance (#546).
    /// </summary>
    void BroadcastFinishCast(IUnit caster, IAbility ability);

    /// <summary>
    /// Tells every client in the instance that <paramref name="caster" />'s cast of
    /// <paramref name="ability" /> was interrupted, so its cast bar ends. Sent only for a unit in this
    /// instance (#546).
    /// </summary>
    void BroadcastInterruptedCast(IUnit caster, IAbility ability);

    /// <summary>
    /// Broadcasts a death event to all connections in the instance. Called by
    /// <see cref="ICombatService"/> when <see cref="ICombatService.ApplyDamage(IUnit,IUnit,uint,IAbility)"/>
    /// (or its raw-damage overload) brings <paramref name="unit"/> to 0 HP / dead state.
    /// </summary>
    void BroadcastUnitDeath(IUnit unit, IUnit? killer);

    /// <summary>
    /// Broadcasts a revive event to all connections in the instance. Called by
    /// <see cref="ICombatService.RevivePlayer(IUnit, Vector3)"/> after the unit's dead flag
    /// is cleared, position is updated, and <c>CurrentHealth</c> is restored to the
    /// configured fraction of <c>Health</c>.
    /// </summary>
    void BroadcastUnitRevive(IUnit unit, Vector3 position, uint health);
}
