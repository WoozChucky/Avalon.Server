using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
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
        public Queue<Vector3> Path { get; } = new();
    }

    /// <summary>
    /// No-ops if the creature is already registered, matching CrowdLocomotion. Replacing an
    /// in-progress Agent with a fresh empty one would freeze the creature in place without coming
    /// to rest — Velocity and MoveState stay at whatever moving value the caller last set, and the
    /// client extrapolates a creature whose position never changes again.
    /// </summary>
    public void Register(ICreature creature, float radius, float maxSpeed)
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

        agent.Path.Clear();
        foreach (Vector3 waypoint in _navigatorFor(creature.Position).FindPath(creature.Position, destination))
            agent.Path.Enqueue(waypoint);

        if (agent.Path.Count == 0)
            ComeToRest(creature);
    }

    public void Stop(ICreature creature)
    {
        if (!_agents.TryGetValue(creature.Guid, out Agent? agent))
            return;

        agent.Path.Clear();
        ComeToRest(creature);
    }

    public void Teleport(ICreature creature, Vector3 position)
    {
        if (_agents.TryGetValue(creature.Guid, out Agent? agent))
            agent.Path.Clear();

        creature.Position = position;
        ComeToRest(creature);
    }

    public bool HasArrived(ICreature creature) =>
        !_agents.TryGetValue(creature.Guid, out Agent? agent) || agent.Path.Count == 0;

    /// <summary>Same constant Advance uses to decide a waypoint has been reached, read from one place.</summary>
    public float ArrivalTolerance(ICreature creature) => WaypointReachedDistance;

    public void Update(TimeSpan deltaTime)
    {
        foreach (Agent agent in _agents.Values)
            Advance(agent, deltaTime);
    }

    private void Advance(Agent agent, TimeSpan deltaTime)
    {
        if (agent.Path.Count == 0)
            return;

        ICreature creature = agent.Creature;
        Vector3 next = agent.Path.Peek();

        if (Vector3.Distance(creature.Position, next) < WaypointReachedDistance)
        {
            agent.Path.Dequeue();
            if (agent.Path.Count == 0)
            {
                ComeToRest(creature);
                return;
            }

            next = agent.Path.Peek();
        }

        Vector3 direction = Vector3.Normalize(next - creature.Position);

        creature.LookAt(next);
        creature.Velocity = direction;
        creature.Position += direction * creature.Speed * (float)deltaTime.TotalSeconds;
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
