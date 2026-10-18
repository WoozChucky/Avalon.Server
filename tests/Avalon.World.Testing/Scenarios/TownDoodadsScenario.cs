using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// Doodads in a busy town: 30 players walking the 8 m loop (<see cref="TownNavmesh.LoopCentre" />) through the real
/// input handler, as in <c>town-walk</c> but with no creatures, among 20 doodads without rules and 20 behind a quest
/// gate, on a ring 20 m from the loop's centre, so every player has all 40 in range on every view decision. Every other
/// player has taken the gate's quest and is refused the gated ones: what is measured is the view decision over doodads,
/// the ungated ones listed by distance alone and the gated ones by the answer each view holds, in steady state.
/// </summary>
public sealed class TownDoodadsScenario : IScenario
{
    private const int Ungated = 20;
    private const int Gated = 20;
    private const float RingRadius = 20f;
    private const uint GateQuest = 1;
    private const uint FirstDoodadId = 20_000;

    // Seen by a player that has not taken quest 1: every player of an odd index.
    private static readonly DoodadGate s_gate = new(GateQuest, DoodadGateCondition.NotStarted, null);

    public string Name => "town-doodads";

    public int Players => 30;

    public ScenarioWorld Build()
    {
        var world = new ScenarioWorld();

        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);
        if (navigator.FindGround(TownNavmesh.LoopCentre, out Vector3 centre) != NavmeshGroundKind.Under)
            throw new InvalidOperationException("The loop's centre is off the town's navmesh");

        // Placed as an instance build places them, before the registry publishes the town.
        DoodadTemplateView plain = Template(1, visible: null);
        DoodadTemplateView gated = Template(2, visible: s_gate);
        MapInstance town = world.AddInstance(MapType.Town, build: instance =>
        {
            for (int i = 0; i < Ungated + Gated; i++)
            {
                float angle = i * MathF.Tau / (Ungated + Gated);
                var position = new Vector3(centre.x + RingRadius * MathF.Cos(angle), centre.y,
                    centre.z + RingRadius * MathF.Sin(angle));
                instance.AddDoodad(new Doodad(new ObjectGuid(ObjectType.Doodad, FirstDoodadId + (uint)i),
                    i % 2 == 0 ? plain : gated, position, angle));
            }
        });

        PlayerInputHandler handler = LoopWalker.Handler(world);
        for (int p = 0; p < Players; p++)
        {
            CharacterEntity character = world.NewCharacter((uint)(1 + p));
            if (p % 2 == 0)
                character.Quests.Start(GateQuest, world.Clock.GetUtcNow().UtcDateTime);

            Vector3 start = LoopWalker.LoopPoint(navigator, centre, p * MathF.Tau / Players);
            world.Join(town, character, start, LoopWalker.Around(handler, centre));
        }

        return world;
    }

    /// <summary>
    /// Every player sent to and walking, and each one's view holding exactly the doodads its rules allow: every ungated
    /// one, and the gated ones only for a player that has not taken the quest. A view that lost them would measure
    /// cheaper than it is.
    /// </summary>
    public void Verify(ScenarioWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.RequireEveryPresent(Name);
        world.RequireEverySent(Name);
        world.RequireEveryWalked(Name, LoopWalker.MeasuredInputs);

        MapInstance town = world.Maps[0];
        if (town.Doodads.Count != Ungated + Gated)
            throw new InvalidOperationException($"{Name}: the town holds {town.Doodads.Count} doodads, expected {Ungated + Gated}");

        for (int p = 0; p < world.Connections.Count; p++)
        {
            var character = (CharacterEntity)world.Connections[p].Character!;
            bool seesGated = p % 2 != 0;
            foreach (Doodad doodad in town.Doodads.All)
            {
                bool expected = doodad.VisibleGate is null || seesGated;
                if (character.CharacterGameState.Knows(doodad.Guid) != expected)
                {
                    throw new InvalidOperationException(
                        $"{Name}: player {p}'s view {(expected ? "lost" : "holds")} doodad {doodad.Guid.Id}. A view that " +
                        "no longer lists the doodads its rules allow measures cheaper than it is and would pass the " +
                        "allocation gate as an improvement.");
                }
            }
        }
    }

    private static DoodadTemplateView Template(uint id, DoodadGate? visible) => new(
        new DoodadTemplateId(id), "Scenario crate", "props-kit", "Crate", null, null, null, null,
        DoodadInteraction.Click, 0, 5f, 0f, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, null, null,
        null, 0, 0, 0, DoodadCollision.None, 0f, 0f, 0f, visible, null, [], 0, ScriptCanSee: false);
}
