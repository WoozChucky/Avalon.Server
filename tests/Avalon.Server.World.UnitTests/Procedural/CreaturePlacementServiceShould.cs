using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Procedural;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Procedural;

public class CreaturePlacementServiceShould
{
    [Fact]
    public async Task Spawn_creature_for_each_spawn_slot_using_SpawnTable_weights()
    {
        var creature = Substitute.For<ICreature>();
        var spawner = Substitute.For<ICreatureSpawner>();
        spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(creature);

        var library = Substitute.For<IChunkLibrary>();
        var chunkTpl = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1),
            SpawnSlots = new List<ChunkSpawnSlot>
            {
                new() { Tag = "pack", LocalX = 5, LocalY = 0, LocalZ = 5 }
            }
        };
        library.GetById(new ChunkTemplateId(1)).Returns(chunkTpl);

        var spawnTableRepo = Substitute.For<ISpawnTableRepository>();
        spawnTableRepo.FindByIdAsync(new SpawnTableId(1), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SpawnTable
            {
                Id = new SpawnTableId(1),
                Entries = new List<SpawnTableEntry>
                {
                    new() { Tag = "pack", CreatureId = new CreatureTemplateId(100), Weight = 1, MinCount = 1, MaxCount = 1 }
                }
            });

        var instance = Substitute.For<IMapInstance>();
        var layout = new ProceduralLayout(
            Seed: 1,
            Chunks: new[] { new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero) },
            EntryChunk: new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero),
            BossChunk: null, Portals: Array.Empty<PortalPlacement>(),
            EntrySpawnWorldPos: Vector3.zero, CellSize: 30f);
        var cfg = new ProceduralMapConfig { SpawnTableId = new SpawnTableId(1) };

        var svc = new CreaturePlacementService(spawner, library, spawnTableRepo);
        await svc.PlaceAsync(instance, layout, cfg, seed: 7, CancellationToken.None);

        instance.Received(1).AddCreature(Arg.Any<ICreature>());
    }

    [Fact]
    public async Task Skip_entry_and_empty_slots()
    {
        var creature = Substitute.For<ICreature>();
        var spawner = Substitute.For<ICreatureSpawner>();
        spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(creature);

        var library = Substitute.For<IChunkLibrary>();
        var chunkTpl = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1),
            SpawnSlots = new List<ChunkSpawnSlot>
            {
                new() { Tag = "entry", LocalX = 1, LocalY = 0, LocalZ = 1 },
                new() { Tag = "empty", LocalX = 2, LocalY = 0, LocalZ = 2 }
            }
        };
        library.GetById(new ChunkTemplateId(1)).Returns(chunkTpl);

        var spawnTableRepo = Substitute.For<ISpawnTableRepository>();
        spawnTableRepo.FindByIdAsync(new SpawnTableId(1), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SpawnTable { Id = new SpawnTableId(1), Entries = new List<SpawnTableEntry>() });

        var instance = Substitute.For<IMapInstance>();
        var layout = new ProceduralLayout(
            Seed: 1,
            Chunks: new[] { new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero) },
            EntryChunk: new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero),
            BossChunk: null, Portals: Array.Empty<PortalPlacement>(),
            EntrySpawnWorldPos: Vector3.zero, CellSize: 30f);
        var cfg = new ProceduralMapConfig { SpawnTableId = new SpawnTableId(1) };

        var svc = new CreaturePlacementService(spawner, library, spawnTableRepo);
        await svc.PlaceAsync(instance, layout, cfg, seed: 7, CancellationToken.None);

        instance.DidNotReceive().AddCreature(Arg.Any<ICreature>());
    }
}
