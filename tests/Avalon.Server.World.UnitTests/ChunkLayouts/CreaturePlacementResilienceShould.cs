using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

/// <summary>
/// One bad spawn-table row must cost one creature, not the whole map.
/// </summary>
/// <remarks>
/// <c>SpawnTableEntry</c> rows are inserted by migration SQL rather than declared as model seed data,
/// so no seed test can prove every <c>CreatureId</c> resolves — a mistyped one is exactly the mistake
/// that survives review. Placement runs inside <c>MapInstance</c> construction, so a throw there makes
/// the map unenterable for every player, not just the one who would have met that creature.
/// </remarks>
public class CreaturePlacementResilienceShould
{
    [Fact]
    public async Task Skip_A_Creature_Whose_Template_Is_Missing_Rather_Than_Failing_The_Whole_Map()
    {
        var goodId = new CreatureTemplateId(4);
        var missingId = new CreatureTemplateId(999);

        var spawner = Substitute.For<ICreatureSpawner>();
        spawner.Spawn(Arg.Is<CreatureInfo>(info => info.PrototypeIndex == missingId.Value))
            .Returns(_ => throw new Exception($"Could not find creature template {missingId}"));

        ICreature spawned = Substitute.For<ICreature>();
        spawned.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        spawned.Metadata.Returns(Substitute.For<ICreatureMetadata>());
        spawner.Spawn(Arg.Is<CreatureInfo>(info => info.PrototypeIndex == goodId.Value)).Returns(spawned);

        var instance = Substitute.For<IMapInstance>();

        // Separate tags, so each entry owns a slot: WeightedPick chooses ONE entry per slot, so two
        // entries sharing a tag would let the good one be picked and the throw never happen.
        await BuildService(spawner, Entry(1, "pack", missingId), Entry(2, "boss", goodId))
            .PlaceAsync(instance, Layout(), Config(), seed: 0, CancellationToken.None);

        // The good creature still reaches the instance; the bad row cost only itself.
        instance.Received().AddCreature(spawned);
    }

    private static SpawnTableEntry Entry(int id, string tag, CreatureTemplateId creature) => new()
    {
        Id = id,
        SpawnTableId = new SpawnTableId(1),
        Tag = tag,
        CreatureId = creature,
        Weight = 1f,
        MinCount = 1,
        MaxCount = 1
    };

    private static CreaturePlacementService BuildService(ICreatureSpawner spawner, params SpawnTableEntry[] entries)
    {
        var table = new SpawnTable { Id = new SpawnTableId(1), Name = "test", Entries = entries.ToList() };

        var repo = Substitute.For<ISpawnTableRepository>();
        repo.FindByIdAsync(Arg.Any<SpawnTableId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SpawnTable?>(table));

        var library = Substitute.For<IChunkLibrary>();
        library.GetById(Arg.Any<ChunkTemplateId>()).Returns(ChunkTemplateWithPackSlot());

        return new CreaturePlacementService(
            spawner,
            library,
            repo,
            Substitute.For<IMapCreatureSpawnRepository>(),
            Substitute.For<IScriptManager>(),
            Substitute.For<IServiceProvider>(),
            NullLoggerFactory.Instance);
    }

    private static ChunkTemplate ChunkTemplateWithPackSlot() => new()
    {
        Id = new ChunkTemplateId(1),
        Name = "test_chunk",
        SpawnSlots =
        [
            new ChunkSpawnSlot { Tag = "pack", LocalX = 0, LocalY = 0, LocalZ = 0 },
            new ChunkSpawnSlot { Tag = "boss", LocalX = 5, LocalY = 0, LocalZ = 5 }
        ]
    };

    private static ChunkLayout Layout()
    {
        var chunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);

        return new ChunkLayout(
            Seed: 0,
            Chunks: [chunk],
            EntryChunk: chunk,
            BossChunk: null,
            Portals: [],
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: null);
    }

    private static ProceduralMapConfig Config() => new()
    {
        MapTemplateId = new MapTemplateId(2),
        SpawnTableId = new SpawnTableId(1)
    };
}
