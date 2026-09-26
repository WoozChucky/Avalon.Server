using Avalon.Common;
using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Creatures;

/// <summary>
/// Moves creatures. Scripts decide <em>where</em> to go and call <see cref="MoveTo" />; this decides
/// how to get there and is the only thing that writes a creature's position.
/// </summary>
/// <remarks>
/// <para>
/// Also the only thing that writes a creature's <see cref="IWorldObject.Velocity" />, and every
/// implementation writes it in metres per second: the direction of travel scaled by the speed the
/// creature is actually moving at (<see cref="ICreature.Speed" /> once up to speed), and exactly
/// zero whenever the creature comes to rest. Never a bare unit direction — the client extrapolates
/// by <c>Velocity * seconds</c>, so implementations that disagree on the unit make the configured
/// locomotion change how far every creature appears to move (#424).
/// </para>
/// <para>
/// Per-instance rather than per-creature because a crowd simulation steers every agent in one
/// batched call — an abstraction owned by each creature could not express that.
/// </para>
/// </remarks>
public interface ICreatureLocomotion
{
    /// <summary>
    /// Starts moving this creature. Idempotent: registering an already-registered creature leaves
    /// whatever journey is in progress alone rather than restarting it.
    /// </summary>
    /// <remarks>
    /// Deliberately takes no speed. Speed is <see cref="ICreature.Speed" />, read fresh on every
    /// <see cref="Update" />, and that is the whole contract — the calling script owns it (it is the
    /// same field that decides whether the creature broadcasts Walking or Running) and changes it
    /// mid-journey, after this call has already happened. A speed baked in here would have to be
    /// refreshed through some second member the caller is expected to remember, which is the same
    /// bug one step removed; there is nothing to remember when there is nothing to pass.
    /// </remarks>
    void Register(ICreature creature, float radius);

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

    /// <summary>
    /// Tells the locomotion where a player is, so creatures that steer around other agents also
    /// steer around players. Write-only, and deliberately so: a player's position is decided
    /// elsewhere (server-authoritative input handling), never here — this call only ever feeds that
    /// position in, and nothing in this interface's implementations may feed one back out. An
    /// implementation with no notion of other agents has nothing to record this into.
    /// </summary>
    void SyncPlayer(ObjectGuid guid, Vector3 position);

    /// <summary>Idempotent: a disconnect can race the instance's per-tick sync.</summary>
    void RemovePlayer(ObjectGuid guid);
}
