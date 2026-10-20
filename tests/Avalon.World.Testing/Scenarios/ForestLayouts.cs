using System.Collections.Concurrent;
using System.Diagnostics;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using DotRecast.Detour;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// One seed's forest: its layout as the procedural source generates it (its scenery ring included), the solid doodads' boxes its doodad plan cuts
/// out (none when it plans no solid doodad), the navmesh baked from the layout with those boxes, and how long the bake
/// took.
/// </summary>
public sealed record ForestLayout(int Seed, ChunkLayout Layout, IReadOnlyList<DoodadObstacle> Obstacles, DtNavMesh NavMesh,
    TimeSpan BakeTime);

/// <summary>
/// The forest's layouts by seed, each generated and baked once per process with the World server's own generator and
/// navmesh builder over the committed catalog (<see cref="ScenarioReferenceData" />), and shared by every instance built
/// on that seed: a baked navmesh is only read once built, and each instance queries it through its own navigator. The
/// generation and the bake are setup cost, never part of a measured tick.
/// </summary>
public static class ForestLayouts
{
    private static readonly ConcurrentDictionary<int, Lazy<ForestLayout>> s_bySeed = new();

    // The slot tags whose level no band decides: the entry and an empty slot place nothing, and a boss stands at the
    // highest band's top level (DepthBandLevels.BossLevel) wherever it is.
    private static readonly HashSet<string> s_unrolledTags = new(StringComparer.OrdinalIgnoreCase) { "entry", "empty", "boss" };

    /// <summary>The seed's forest, generated and baked on first use (thread-safe).</summary>
    public static ForestLayout For(int seed) =>
        s_bySeed.GetOrAdd(seed, static s => new Lazy<ForestLayout>(() => Build(s))).Value;

    private static ForestLayout Build(int seed)
    {
        ScenarioReferenceData reference = ScenarioReferenceData.Shared;
        ProceduralMapConfig config = reference.ForestConfig;

        // ProceduralChunkLayoutSource.BuildAsync: the generation step, with the seed given rather than drawn, then the
        // scenery ring built on the layout from the committed pool, so the arrival packet carries it as in play.
        ChunkLayout generated = new ProceduralLayoutGenerator(NullLoggerFactory.Instance).Generate(config,
            reference.Chunks.GetByPool(config.ChunkPoolId), seed, reference.Chunks.GetGroupsByPool(config.ChunkPoolId));
        SceneryPool scenery = reference.Scenery.PoolFor(ScenarioReferenceData.ForestMap.Value)
            ?? throw new InvalidOperationException(
                $"Map {ScenarioReferenceData.ForestMap.Value} has no scenery pool: a forest scenario would measure layouts without their ring");
        ChunkLayout layout = generated with { Scenery = SceneryRing.Build(generated, id => reference.Chunks.GetById(id).Exits, scenery) };

        RequireEveryLevelSeeded(reference, config, layout, seed);

        // ChunkLayoutInstanceFactory.BuildAsync's doodad plan, from the layout, the rules and the catalog alone, so the
        // same seed plans the same boxes there: its solid doodads' boxes are baked in as the factory bakes them.
        var doodads = new DoodadPlacementService(
            new DoodadScripts(new ServiceCollection().BuildServiceProvider(), TimeProvider.System, NullLogger<DoodadScripts>.Instance),
            TimeProvider.System, NullLoggerFactory.Instance, reference.DoodadSpawns);
        IReadOnlyList<DoodadObstacle> obstacles = doodads.Plan(layout, config, reference.Data.Doodads, reference.Chunks).Obstacles;
        ChunkLayout baked = obstacles.Count > 0 ? layout with { Obstacles = obstacles } : layout;

        // The builder reads <content root>/Maps/Chunks/<name>.obj, so it is handed the directory holding Maps.
        var builder = ChunkLayoutNavmeshBuilder.ForTesting(NullLoggerFactory.Instance, reference.Chunks,
            Path.GetDirectoryName(TownNavmesh.ContentRoot)!);

        long start = Stopwatch.GetTimestamp();
        DtNavMesh navMesh = builder.BuildAsync(baked, CancellationToken.None).GetAwaiter().GetResult();
        return new ForestLayout(seed, layout, obstacles, navMesh, Stopwatch.GetElapsedTime(start));
    }

    /// <summary>
    /// Throws unless every spawn slot of the layout that places a creature gets its level from a depth band (or, a boss,
    /// the highest band's): a chunk at a depth no band covers would have its creatures' levels drawn from Random.Shared
    /// by CreatureSpawner.RollLevel (CreaturePlacementService.PlaceAsync), and a seed would no longer fix them. The entry
    /// chunk, at depth 0, holds only its entry slot.
    /// </summary>
    private static void RequireEveryLevelSeeded(ScenarioReferenceData reference, ProceduralMapConfig config,
        ChunkLayout layout, int seed)
    {
        foreach (PlacedChunk chunk in layout.Chunks)
        {
            if (DepthBandLevels.For(config.DepthBands, chunk.Depth, setPiece: chunk.Group is not null) is not null)
                continue;

            foreach (ChunkSpawnSlot slot in reference.Chunks.GetById(chunk.TemplateId).SpawnSlots)
            {
                if (!s_unrolledTags.Contains(slot.Tag))
                {
                    throw new InvalidOperationException(
                        $"Forest seed {seed}: chunk {chunk.TemplateId.Value} at depth {chunk.Depth} is in no depth band, so " +
                        $"its '{slot.Tag}' creatures would have their levels drawn from Random.Shared");
                }
            }
        }
    }
}
