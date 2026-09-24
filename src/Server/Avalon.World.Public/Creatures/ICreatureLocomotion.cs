using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Creatures;

/// <summary>
/// Moves creatures. Scripts decide <em>where</em> to go and call <see cref="MoveTo" />; this decides
/// how to get there and is the only thing that writes a creature's position.
/// </summary>
/// <remarks>
/// Per-instance rather than per-creature because a crowd simulation steers every agent in one
/// batched call — an abstraction owned by each creature could not express that.
/// </remarks>
public interface ICreatureLocomotion
{
    void Register(ICreature creature, float radius, float maxSpeed);

    /// <summary>Idempotent: despawn can race a script update.</summary>
    void Unregister(ICreature creature);

    /// <summary>Sets or refreshes where this creature is trying to get to.</summary>
    void MoveTo(ICreature creature, Vector3 destination);

    /// <summary>Stops the creature where it stands, discarding any destination.</summary>
    void Stop(ICreature creature);

    /// <summary>
    /// Places a creature directly, bypassing movement — for spawns and leash-returns, which are not
    /// journeys. Under a crowd this moves the agent too, not just the entity.
    /// </summary>
    void Teleport(ICreature creature, Vector3 position);

    /// <summary>True when the creature has no further destination, including when none was reachable.</summary>
    bool HasArrived(ICreature creature);

    /// <summary>
    /// The distance a creature reported as arrived (<see cref="HasArrived" /> true) may actually be
    /// standing from the destination it was given — the slack a caller needs to allow when deciding
    /// whether "arrived" also means "close enough" for its own purposes (e.g. within melee range of
    /// a slot). Takes the creature rather than being a bare constant because a crowd implementation's
    /// tolerance depends on the per-creature radius supplied to <see cref="Register" />: it cannot be
    /// answered without knowing which creature is asking.
    /// </summary>
    float ArrivalTolerance(ICreature creature);

    /// <summary>Advances every registered creature. Called once per MapInstance tick.</summary>
    void Update(TimeSpan deltaTime);
}
