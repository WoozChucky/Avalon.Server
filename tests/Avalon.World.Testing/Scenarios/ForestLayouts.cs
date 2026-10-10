using System.Collections.Concurrent;
using System.Diagnostics;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>One seed's forest: its layout, the navmesh baked from it, and how long the bake took.</summary>
public sealed record ForestLayout(int Seed, ChunkLayout Layout, DtNavMesh NavMesh, TimeSpan BakeTime);

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

        // ProceduralChunkLayoutSource.BuildAsync's generation, with the seed given rather than drawn.
        ChunkLayout layout = new ProceduralLayoutGenerator(NullLoggerFactory.Instance).Generate(config,
            reference.Chunks.GetByPool(config.ChunkPoolId), seed, reference.Chunks.GetGroupsByPool(config.ChunkPoolId));

        RequireEveryLevelSeeded(reference, config, layout, seed);

        // The builder reads <content root>/Maps/Chunks/<name>.obj, so it is handed the directory holding Maps.
        var builder = ChunkLayoutNavmeshBuilder.ForTesting(NullLoggerFactory.Instance, reference.Chunks,
            Path.GetDirectoryName(TownNavmesh.ContentRoot)!);

        long start = Stopwatch.GetTimestamp();
        DtNavMesh navMesh = builder.BuildAsync(layout, CancellationToken.None).GetAwaiter().GetResult();
        return new ForestLayout(seed, layout, navMesh, Stopwatch.GetElapsedTime(start));
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
