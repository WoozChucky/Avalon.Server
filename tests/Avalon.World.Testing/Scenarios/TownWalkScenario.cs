using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// A busy town: one town, 30 players walking an 8 m loop (<see cref="TownNavmesh.LoopCentre" />) through the real input
/// handler, and 10 hostile creatures, each fighting one of them with the real <see cref="CreatureCombatScript" />, so
/// their melee station-keeping never settles. Every player sees every other and every creature: what is measured is
/// input, movement, creature pathing and the state broadcast of a crowded town.
/// </summary>
/// <remarks>
/// The players start on the loop, evenly spaced, so the walk is in its steady state from the first tick. The creatures
/// are crowd-budget's (<c>tools/Avalon.Benchmarking/CrowdBudget</c>): its "Bench Wolf", placed by the same seeded draw
/// 9 to 13 m from the loop's centre, with health no fight ends, engaged as it engages them. Their script reads the
/// world's clock, as the container hands it in production; the default kit is empty, so they chase and keep station
/// but cast nothing.
/// </remarks>
public sealed class TownWalkScenario : IScenario
{
    private const int CreatureCount = 10;
    private const int CreatureSeed = 425;
    private const uint FirstCreatureId = 10_000;

    // How far (X/Z) a wolf may be from the player it fights when checked. A wolf keeping station stays close to a player
    // walking at 4 m/s (at most 1.9 m over 20,000 ticks, measured); one that stopped chasing falls far behind.
    private const float EngagedDistance = 4f;

    public string Name => "town-walk";

    public int Players => 30;

    public ScenarioWorld Build()
    {
        var world = new ScenarioWorld();
        MapInstance town = world.AddInstance(MapType.Town);

        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);
        if (navigator.FindGround(TownNavmesh.LoopCentre, out Vector3 centre) != NavmeshGroundKind.Under)
            throw new InvalidOperationException("The loop's centre is off the town's navmesh");

        PlayerInputHandler handler = LoopWalker.Handler(world);
        var characters = new List<CharacterEntity>(Players);
        for (int p = 0; p < Players; p++)
        {
            CharacterEntity character = world.NewCharacter((uint)(1 + p));
            Vector3 start = LoopWalker.LoopPoint(navigator, centre, p * MathF.Tau / Players);
            world.Join(town, character, start, LoopWalker.Around(handler, centre));
            characters.Add(character);
        }

        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(4),
            Name = "Bench Wolf",
            SpeedWalk = 2.5f,
            SpeedRun = 5f,
            BaseAttackTime = 2.25f,
        };

        var rng = new Random(CreatureSeed);
        for (int i = 0; i < CreatureCount; i++)
        {
            var creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, FirstCreatureId + (uint)i),
                TemplateId = template.Id,
                Metadata = template,
                Name = template.Name,
                Position = CreatureSpawn(navigator, centre, rng),
                Speed = template.SpeedWalk,
                MoveState = MoveState.Idle,
                Level = 1,
                Health = 100_000_000,
                CurrentHealth = 100_000_000,
                DamageMin = 1,
                DamageMax = 1,
                BaseAttackTime = template.BaseAttackTime,
            };
            var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, town, world.Clock);
            creature.Script = script;
            town.AddCreature(creature);
            script.OnEnteredRange(characters[i % Players]);
        }

        return world;
    }

    /// <summary>
    /// Every player sent to and walking, and every wolf still fighting the player it was set on, close behind it: a
    /// wolf that went home or stopped chasing no longer paths, and the scenario would measure cheaper than it is.
    /// </summary>
    public void Verify(ScenarioWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.RequireEveryPresent(Name);
        world.RequireEverySent(Name);
        world.RequireEveryWalked(Name, LoopWalker.MeasuredInputs);

        MapInstance town = world.Maps[0];
        if (town.Creatures.Count != CreatureCount)
            throw new InvalidOperationException($"{Name}: the town holds {town.Creatures.Count} creatures, expected {CreatureCount}");

        foreach (ICreature creature in town.Creatures.Values)
        {
            if (creature.Script is not CreatureCombatScript { State: CreatureCombatScript.CombatState.Combat })
            {
                throw new InvalidOperationException(
                    $"{Name}: creature {creature.Guid.Id} is no longer in combat ({(creature.Script as CreatureCombatScript)?.State}). " +
                    "A wolf that stopped fighting no longer chases, and the scenario would pass the allocation gate as an improvement.");
            }

            int engaged = (int)(creature.Guid.Id - FirstCreatureId) % Players;
            Vector3 player = world.Connections[engaged].Character!.Position;
            float dx = creature.Position.x - player.x;
            float dz = creature.Position.z - player.z;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance > EngagedDistance)
            {
                throw new InvalidOperationException(
                    $"{Name}: creature {creature.Guid.Id} is {distance:F1} m from player {engaged}, the one it fights " +
                    $"(at most {EngagedDistance} m expected). A wolf that stopped chasing no longer paths, and the " +
                    "scenario would pass the allocation gate as an improvement.");
            }
        }
    }

    /// <summary>
    /// Crowd-budget's draw: a point 9 to 13 m from the centre that a path from the centre reaches, to within 0.3 m, so
    /// no creature starts in a wall or out of reach.
    /// </summary>
    private static Vector3 CreatureSpawn(MapNavigator navigator, Vector3 centre, Random rng)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            float angle = (float)(rng.NextDouble() * Math.Tau);
            float distance = 9f + (float)rng.NextDouble() * 4f;
            var candidate = new Vector3(centre.x + distance * MathF.Cos(angle), centre.y,
                centre.z + distance * MathF.Sin(angle));

            List<Vector3> path = navigator.FindPath(centre, candidate);
            if (path.Count > 0 && Vector3.Distance(path[^1], candidate with { y = path[^1].y }) < 0.3f)
                return path[^1];
        }

        throw new InvalidOperationException("No reachable creature spawn around the loop");
    }
}
