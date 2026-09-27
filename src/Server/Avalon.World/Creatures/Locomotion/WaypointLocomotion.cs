using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;

namespace Avalon.World.Creatures.Locomotion;

/// <summary>
/// Walks a navmesh path waypoint by waypoint, exactly as CreatureCombatScript and
/// CreaturePatrolScript each did for themselves. No awareness of other agents: two creatures given
/// the same destination will occupy the same point.
/// </summary>
public sealed class WaypointLocomotion : ICreatureLocomotion
{
    private const float WaypointReachedDistance = 0.1f;

    private readonly Func<Vector3, IMapNavigator> _navigatorFor;
    private readonly Dictionary<ObjectGuid, Agent> _agents = [];

    public WaypointLocomotion(Func<Vector3, IMapNavigator> navigatorFor) => _navigatorFor = navigatorFor;

    private sealed class Agent
    {
        public required ICreature Creature { get; init; }

        /// <summary>
        /// The route, refilled in place by every MoveTo rather than replaced (#638), so a re-path in
        /// steady state allocates nothing once the list has grown to the longest route it has held.
        /// </summary>
        public List<Vector3> Path { get; } = [];

        /// <summary>The index in <see cref="Path" /> of the waypoint being walked to.</summary>
        public int Next { get; set; }

        public bool HasRoute => Next < Path.Count;

        public void ClearRoute()
        {
            Path.Clear();
            Next = 0;
            RouteEnd = null;
        }

        /// <summary>The last point of the route the last MoveTo found, kept once walked (#606).</summary>
        public Vector3? RouteEnd { get; set; }
    }

    /// <summary>
    /// No-ops if the creature is already registered, matching CrowdLocomotion. Replacing an
    /// in-progress Agent with a fresh empty one would freeze the creature in place without coming
    /// to rest — Velocity and MoveState stay at whatever moving value the caller last set, and the
    /// client extrapolates a creature whose position never changes again.
    /// </summary>
    public void Register(ICreature creature, float radius)
    {
        if (_agents.ContainsKey(creature.Guid))
            return;

        _agents[creature.Guid] = new Agent { Creature = creature };
    }

    public void Unregister(ICreature creature) => _agents.Remove(creature.Guid);

    public void MoveTo(ICreature creature, Vector3 destination)
    {
        if (!_agents.TryGetValue(creature.Guid, out Agent? agent))
            return;

        agent.ClearRoute();
        IMapNavigator navigator = _navigatorFor(creature.Position);
        if (navigator is IPathBufferNavigator buffered)
            buffered.FindPath(creature.Position, destination, agent.Path);
        else
            agent.Path.AddRange(navigator.FindPath(creature.Position, destination));

        if (agent.Path.Count == 0)
        {
            ComeToRest(creature);
            return;
        }

        agent.RouteEnd = agent.Path[^1];
    }

    public void Stop(ICreature creature)
    {
        if (!_agents.TryGetValue(creature.Guid, out Agent? agent))
            return;

        agent.ClearRoute();
        ComeToRest(creature);
    }

    public void Teleport(ICreature creature, Vector3 position)
    {
        if (_agents.TryGetValue(creature.Guid, out Agent? agent))
            agent.ClearRoute();

        creature.Position = position;
        ComeToRest(creature);
    }

    public bool HasArrived(ICreature creature) =>
        !_agents.TryGetValue(creature.Guid, out Agent? agent) || !agent.HasRoute;

    /// <summary>Same constant Advance uses to decide a waypoint has been reached, read from one place.</summary>
    public float ArrivalTolerance(ICreature creature) => WaypointReachedDistance;

    public Vector3? ResolvedDestination(ICreature creature) =>
        _agents.TryGetValue(creature.Guid, out Agent? agent) ? agent.RouteEnd : null;

    public void Update(TimeSpan deltaTime)
    {
        foreach (Agent agent in _agents.Values)
            Advance(agent, deltaTime);
    }

    /// <summary>
    /// No-op: this implementation has no notion of other agents to steer around in the first place —
    /// each creature walks its own waypoint queue independently, blind to every other creature and
    /// every player. There is nothing here for a player's position to be recorded into, so recording
    /// it would be dead state with no reader. Not a missing feature; the crowd is what
    /// player-awareness needs, and that is exactly what <see cref="CrowdLocomotion" /> is for.
    /// </summary>
    public void SyncPlayer(ObjectGuid guid, Vector3 position)
    {
    }

    /// <summary>See <see cref="SyncPlayer" />: nothing was ever recorded, so there is nothing to remove.</summary>
    public void RemovePlayer(ObjectGuid guid)
    {
    }

    private void Advance(Agent agent, TimeSpan deltaTime)
    {
        if (!agent.HasRoute)
            return;

        ICreature creature = agent.Creature;
        Vector3 next = agent.Path[agent.Next];

        if (Vector3.Distance(creature.Position, next) < WaypointReachedDistance)
        {
            agent.Next++;
            if (!agent.HasRoute)
            {
                ComeToRest(creature);
                return;
            }

            next = agent.Path[agent.Next];
        }

        Vector3 direction = Vector3.Normalize(next - creature.Position);

        creature.LookAt(next);
        // Metres per second, not the bare direction (#424): the client extrapolates a creature by
        // Velocity * seconds since the last broadcast, and CrowdLocomotion publishes agent.vel in
        // the same unit. A unit vector here extrapolated every creature at 1 m/s.
        creature.Velocity = direction * creature.Speed;

        // Clamped to the remaining distance: an unclamped step overshoots next every tick once
        // Speed * deltaTime exceeds it (SpeedRun above ~6 at a 60Hz tick), so the creature never
        // lands within WaypointReachedDistance and HasArrived never reports true.
        creature.Position = Vector3.MoveTowards(creature.Position, next, creature.Speed * (float)deltaTime.TotalSeconds);
    }

    /// <summary>
    /// Never leave MoveState at a moving value with nothing to walk towards: the client would
    /// animate a creature running on the spot while its position never changes.
    /// </summary>
    private static void ComeToRest(ICreature creature)
    {
        creature.Velocity = Vector3.zero;
        creature.MoveState = MoveState.Idle;
    }
}
