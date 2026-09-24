using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Creatures;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Creatures.Locomotion;

/// <summary>
/// Steers creatures with DotRecast's crowd simulation, so agents heading for the same place push
/// apart instead of stacking on one point. Behaviourally interchangeable with
/// <see cref="WaypointLocomotion" /> — the two are selected between by configuration, so every
/// observable rule below is deliberately the one WaypointLocomotion follows.
/// </summary>
/// <remarks>
/// One crowd per MapInstance: an instance has exactly one navmesh, so an agent never migrates
/// between crowds. The crowd builds and owns its own nav query, so only the mesh is needed here.
/// </remarks>
public sealed class CrowdLocomotion : ICreatureLocomotion
{
    /// <summary>
    /// Half-extents for the nearest-polygon search, matching <see cref="MapNavigator" />'s
    /// <c>PolyPickExt</c> so a destination WaypointLocomotion can path to is one this can too.
    /// </summary>
    private static readonly RcVec3f PolyPickExt = new(2, 4, 2);

    /// <summary>
    /// Floor for the arrival radius. A crowd agent is pushed off its exact target by separation
    /// from its neighbours, so "arrived" has to mean "close enough", not "on the spot" — the
    /// waypoint implementation gets away with 0.1m only because nothing pushes it sideways. The
    /// real tolerance is the agent's own radius when that is larger, which is the distance at which
    /// the creature is already standing on the target.
    /// </summary>
    private const float MinArrivalDistance = 0.3f;

    private readonly ILogger _logger;
    private readonly DtCrowd _crowd;
    private readonly float _agentRadius;
    private readonly Dictionary<ObjectGuid, DtCrowdAgent> _creatureAgents = [];

    /// <summary>
    /// Player-avoidance agents (Task 9), registered by <see cref="SyncPlayer" />. Deliberately a
    /// separate dictionary from <see cref="_creatureAgents" /> rather than a shared one distinguished
    /// by some flag: <see cref="Update" />'s position/velocity copy-back only ever enumerates
    /// <see cref="_creatureAgents" />, so a player agent is structurally unreachable from that loop —
    /// there is no branch to get wrong. A player's position is decided by
    /// <c>PlayerInputHandler</c>; this class must never write it.
    /// </summary>
    private readonly Dictionary<ObjectGuid, DtCrowdAgent> _playerAgents = [];

    private readonly Dictionary<ObjectGuid, ICreature> _creatures = [];

    public CrowdLocomotion(DtNavMesh navMesh, float agentRadius, ILogger logger)
    {
        _logger = logger;
        _agentRadius = agentRadius;
        _crowd = new DtCrowd(new DtCrowdConfig(agentRadius), navMesh);
    }

    public void Register(ICreature creature, float radius, float maxSpeed)
    {
        if (_creatureAgents.ContainsKey(creature.Guid))
            return;

        _creatureAgents[creature.Guid] = _crowd.AddAgent(ToRc(creature.Position), CreatureParams(radius, maxSpeed));
        _creatures[creature.Guid] = creature;
    }

    /// <summary>Idempotent: a leaked agent would keep steering a despawned creature.</summary>
    public void Unregister(ICreature creature)
    {
        if (!_creatureAgents.Remove(creature.Guid, out DtCrowdAgent? agent))
            return;

        _crowd.RemoveAgent(agent);
        _creatures.Remove(creature.Guid);
    }

    /// <summary>
    /// A destination off the navmesh is "nowhere to go", exactly as an empty path is for
    /// WaypointLocomotion: the creature comes to rest and <see cref="HasArrived" /> reports true.
    /// </summary>
    public void MoveTo(ICreature creature, Vector3 destination)
    {
        if (!_creatureAgents.TryGetValue(creature.Guid, out DtCrowdAgent? agent))
            return;

        DtNavMeshQuery query = _crowd.GetNavMeshQuery();
        DtStatus status = query.FindNearestPoly(ToRc(destination), PolyPickExt, _crowd.GetFilter(0),
            out long refs, out RcVec3f nearest, out _);

        if (status.Failed() || refs == 0)
        {
            _logger.LogDebug("No navmesh polygon near {Destination}; creature {Guid} stays put",
                destination, creature.Guid);
            Stop(creature);
            return;
        }

        _crowd.RequestMoveTarget(agent, refs, nearest);
    }

    /// <summary>Discards the destination and comes to rest, leaving the creature where it stands.</summary>
    public void Stop(ICreature creature)
    {
        if (!_creatureAgents.TryGetValue(creature.Guid, out DtCrowdAgent? agent))
            return;

        _crowd.ResetMoveTarget(agent);
        agent.vel = RcVec3f.Zero;
        ComeToRest(creature);
    }

    /// <summary>
    /// Removes and re-adds the agent rather than moving it. A crowd agent carries a path corridor
    /// that assumes continuous motion; repositioning it in place leaves a corridor that no longer
    /// reaches it, and the crowd spends the next ticks repairing one that should not exist.
    /// Re-adding also clears the move target, so a leash-return cannot walk the creature back along
    /// the route it was teleported off — the same guarantee WaypointLocomotion gets by clearing its
    /// queue.
    /// </summary>
    public void Teleport(ICreature creature, Vector3 position)
    {
        if (_creatureAgents.TryGetValue(creature.Guid, out DtCrowdAgent? agent))
        {
            DtCrowdAgentParams option = agent.option;
            _crowd.RemoveAgent(agent);
            _creatureAgents[creature.Guid] = _crowd.AddAgent(ToRc(position), option);
        }

        creature.Position = position;
        ComeToRest(creature);
    }

    /// <summary>
    /// True when there is no destination left, which includes a destination that turned out to be
    /// unreachable. DotRecast never clears <c>targetState</c> by itself on arrival, so
    /// <see cref="Update" /> clears it — see <see cref="Arrived" />.
    /// </summary>
    public bool HasArrived(ICreature creature) =>
        !_creatureAgents.TryGetValue(creature.Guid, out DtCrowdAgent? agent)
        || agent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;

    /// <summary>
    /// Same floor-of-agent-radius value <see cref="Arrived" /> uses to decide arrival, read from one
    /// place. An unregistered creature has no agent to read a radius from, so it gets the floor —
    /// the smallest tolerance any registered agent could report.
    /// </summary>
    public float ArrivalTolerance(ICreature creature) =>
        ArrivalToleranceFor(_creatureAgents.TryGetValue(creature.Guid, out DtCrowdAgent? agent)
            ? agent.option.radius
            : 0f);

    public void Update(TimeSpan deltaTime)
    {
        _crowd.Update((float)deltaTime.TotalSeconds, null);

        foreach ((ObjectGuid guid, DtCrowdAgent agent) in _creatureAgents)
        {
            if (!_creatures.TryGetValue(guid, out ICreature? creature))
                continue;

            // The creature entity, not the agent, is what the rest of the server reads; this
            // copy-back is what makes crowd steering visible.
            creature.Position = FromRc(agent.npos);
            creature.Velocity = FromRc(agent.vel);

            if (agent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE)
                continue;

            if (Arrived(agent))
            {
                _crowd.ResetMoveTarget(agent);
                agent.vel = RcVec3f.Zero;
                ComeToRest(creature);
                continue;
            }

            // Face where it is actually going. The crowd's steering direction is the velocity, not
            // the straight line to the target, so a creature rounding a corner faces the corner.
            if (agent.vel.Length() > 0.01f)
                creature.LookAt(FromRc(RcVec3f.Add(agent.npos, agent.vel)));
        }
    }

    /// <summary>
    /// Tells the crowd where a player is. Write-only: PlayerInputHandler already decided, so
    /// whatever Integrate and HandleCollisions compute for this agent is discarded next tick.
    /// </summary>
    /// <remarks>
    /// Zero speed and no move target are deliberate. The crowd registers every agent to its
    /// proximity grid regardless of whether it has a target, and that registration is what makes an
    /// agent visible to others — so a zero-speed agent is seen and avoided by creatures while
    /// owning no path corridor for the repositioning below to invalidate.
    /// </remarks>
    public void SyncPlayer(ObjectGuid guid, Vector3 position)
    {
        if (_playerAgents.TryGetValue(guid, out DtCrowdAgent? existing))
        {
            existing.npos = ToRc(position);
            return;
        }

        _playerAgents[guid] = _crowd.AddAgent(ToRc(position), new DtCrowdAgentParams
        {
            radius = _agentRadius,
            height = NavmeshBuildSettings.AgentHeight,
            maxSpeed = 0f,
            maxAcceleration = 0f,
            collisionQueryRange = _agentRadius * 12f,
            pathOptimizationRange = _agentRadius * 30f,
            separationWeight = 2f,
            updateFlags = 0,
        });
    }

    /// <summary>Idempotent: a disconnect can race the instance's per-tick sync.</summary>
    public void RemovePlayer(ObjectGuid guid)
    {
        if (_playerAgents.Remove(guid, out DtCrowdAgent? agent))
            _crowd.RemoveAgent(agent);
    }

    /// <summary>
    /// A move request DotRecast gave up on counts as arrived — the creature has no destination left,
    /// which is what <see cref="ICreatureLocomotion.HasArrived" /> promises and what
    /// WaypointLocomotion reports for an empty path. The caller re-issues MoveTo each tick, so a
    /// transient failure is retried; what would not be survivable is leaving MoveState at a moving
    /// value with nowhere to walk.
    /// </summary>
    private static bool Arrived(DtCrowdAgent agent)
    {
        if (agent.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED)
            return true;

        if (agent.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID)
            return false;

        return RcVec3f.Subtract(agent.targetPos, agent.npos).Length() <= ArrivalToleranceFor(agent.option.radius);
    }

    /// <summary>The one place this class computes an arrival tolerance from an agent radius.</summary>
    private static float ArrivalToleranceFor(float radius) => MathF.Max(radius, MinArrivalDistance);

    /// <summary>
    /// The only MoveState this class ever writes. Scripts own the moving states (Walking vs
    /// Running) and must not have them overwritten every tick; but never leave MoveState at a moving
    /// value with nothing to walk towards, or the client animates a creature running on the spot
    /// while its position never changes.
    /// </summary>
    private static void ComeToRest(ICreature creature)
    {
        creature.Velocity = Vector3.zero;
        creature.MoveState = MoveState.Idle;
    }

    /// <summary>
    /// Height and acceleration come from <see cref="NavmeshBuildSettings" /> rather than literals:
    /// the crowd has to agree with the mesh it steers on, and that class is the single source of
    /// truth for what the mesh was baked for. Radius and max speed are per-creature and supplied by
    /// the caller.
    /// </summary>
    private static DtCrowdAgentParams CreatureParams(float radius, float maxSpeed) => new()
    {
        radius = radius,
        height = NavmeshBuildSettings.AgentHeight,
        maxSpeed = maxSpeed,
        maxAcceleration = NavmeshBuildSettings.AgentMaxAcceleration,
        collisionQueryRange = radius * 12f,
        pathOptimizationRange = radius * 30f,
        separationWeight = 2f,
        updateFlags = DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS
                      | DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE
                      | DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION,
    };

    private static RcVec3f ToRc(Vector3 v) => new(v.x, v.y, v.z);

    private static Vector3 FromRc(RcVec3f v) => new(v.X, v.Y, v.Z);
}
