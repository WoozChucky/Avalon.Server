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

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// Forest content pass: on a map with depth bands a creature keeps its template but rolls its level from its piece's
/// band (a set piece from the highest band; the boss at its top), and a leader slot spawns an Alpha with its pack.
/// </summary>
public class DepthBandPlacementShould
{
    private static readonly List<ProceduralDepthBand> s_bands =
    [
        new() { MinDepth = 1, MaxDepth = 3, MinLevel = 1, MaxLevel = 3 },
        new() { MinDepth = 4, MaxDepth = 7, MinLevel = 3, MaxLevel = 6 },
        new() { MinDepth = 8, MaxDepth = null, MinLevel = 5, MaxLevel = 8 },
    ];

    private sealed record Spawned(ulong Template, ushort? Level);

    private static ICreatureSpawner Recording(List<Spawned> spawned)
    {
        ICreatureSpawner spawner = Substitute.For<ICreatureSpawner>();
        uint next = 1;
        ICreature Creature()
        {
            ICreature creature = Substitute.For<ICreature>();
            creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, next++));
            creature.ScriptName.Returns(string.Empty);
            return creature;
        }
        spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(ci =>
        {
            spawned.Add(new Spawned(ci.Arg<CreatureInfo>().PrototypeIndex, null));
            return Creature();
        });
        spawner.Spawn(Arg.Any<CreatureInfo>(), Arg.Any<ushort>()).Returns(ci =>
        {
            spawned.Add(new Spawned(ci.ArgAt<CreatureInfo>(0).PrototypeIndex, ci.ArgAt<ushort>(1)));
            return Creature();
        });
        return spawner;
    }

    private static IMapInstance FlatInstance()
    {
        IMapNavigator navigator = Substitute.For<IMapNavigator>();
        navigator.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(ci => ci.ArgAt<float>(1));
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);
        return instance;
    }

    private static SpawnTableEntry Entry(int id, string tag, ulong creature, byte min, byte max) => new()
    {
        Id = id,
        SpawnTableId = new SpawnTableId(1),
        Tag = tag,
        CreatureId = new CreatureTemplateId(creature),
        Weight = 1f,
        MinCount = min,
        MaxCount = max,
    };

    private static CreaturePlacementService Service(ICreatureSpawner spawner, Dictionary<int, ChunkTemplate> templates, params SpawnTableEntry[] entries)
    {
        ISpawnTableRepository repo = Substitute.For<ISpawnTableRepository>();
        repo.FindByIdAsync(Arg.Any<SpawnTableId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SpawnTable?>(new SpawnTable { Id = new SpawnTableId(1), Entries = entries.ToList() }));
        IChunkLibrary library = Substitute.For<IChunkLibrary>();
        library.GetById(Arg.Any<ChunkTemplateId>()).Returns(ci => templates[ci.Arg<ChunkTemplateId>().Value]);
        return new CreaturePlacementService(spawner, library, repo, Substitute.For<IMapCreatureSpawnRepository>(),
            Substitute.For<IScriptManager>(), Substitute.For<IServiceProvider>(), NullLoggerFactory.Instance);
    }

    private static ChunkTemplate Piece(int id, params string[] tags) => new()
    {
        Id = new ChunkTemplateId(id),
        Name = $"p{id}",
        SpawnSlots = tags.Select((t, i) => new ChunkSpawnSlot { Tag = t, LocalX = 5 + i * 5, LocalY = 1, LocalZ = 10 }).ToList(),
    };

    private static ChunkLayout Layout(params PlacedChunk[] chunks) => new(
        Seed: 1, Chunks: chunks, EntryChunk: chunks[0], BossChunk: null, Portals: [],
        EntrySpawnWorldPos: Vector3.zero, CellSize: 30f);

    private static ProceduralMapConfig Config(List<ProceduralDepthBand> bands) =>
        new() { MapTemplateId = new MapTemplateId(2), SpawnTableId = new SpawnTableId(1), DepthBands = bands };

    [Theory]
    [InlineData(2, null, 1, 3)]
    [InlineData(5, null, 3, 6)]
    [InlineData(9, null, 5, 8)]
    [InlineData(2, "forest_grove_ruin", 5, 8)]   // a set piece takes the highest band whatever its depth
    public async Task Roll_each_creatures_level_from_its_pieces_band(int depth, string? group, int min, int max)
    {
        var spawned = new List<Spawned>();
        var templates = new Dictionary<int, ChunkTemplate> { [1] = Piece(1, "pack", "pack", "pack") };
        CreaturePlacementService service = Service(Recording(spawned), templates, Entry(1, "pack", 4, 2, 3));

        for (int seed = 0; seed < 20; seed++)
        {
            await service.PlaceAsync(FlatInstance(), Layout(new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero, depth, group)),
                Config(s_bands), seed, CancellationToken.None);
        }

        Assert.NotEmpty(spawned);
        Assert.All(spawned, s => Assert.InRange((int)s.Level!.Value, min, max));
        Assert.Equal(Enumerable.Range(min, max - min + 1), spawned.Select(s => (int)s.Level!.Value).Distinct().Order());
    }

    [Fact]
    public async Task Stand_the_boss_at_the_top_of_the_highest_band()
    {
        var spawned = new List<Spawned>();
        var templates = new Dictionary<int, ChunkTemplate> { [1] = Piece(1, "boss") };

        await Service(Recording(spawned), templates, Entry(7, "boss", 10, 1, 1))
            .PlaceAsync(FlatInstance(), Layout(new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero, 12, "forest_arena")),
                Config(s_bands), seed: 3, CancellationToken.None);

        Assert.Equal([new Spawned(10, 8)], spawned);
    }

    [Fact]
    public async Task Spawn_a_leader_with_its_own_pack_at_the_band_level()
    {
        var spawned = new List<Spawned>();
        var templates = new Dictionary<int, ChunkTemplate> { [1] = Piece(1, "leader") };

        await Service(Recording(spawned), templates, Entry(8, "leader", 8, 1, 1), Entry(9, "leader_pack", 5, 2, 3))
            .PlaceAsync(FlatInstance(), Layout(new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero, 5, null)),
                Config(s_bands), seed: 11, CancellationToken.None);

        Assert.Equal(8ul, spawned[0].Template);
        var pack = spawned.Skip(1).ToList();
        Assert.InRange(pack.Count, 2, 3);
        Assert.All(pack, s => Assert.Equal(5ul, s.Template));
        Assert.All(spawned, s => Assert.InRange((int)s.Level!.Value, 3, 6));
    }

    /// <summary>Review Focus 5: a map without bands places exactly as before, through the one-argument Spawn.</summary>
    [Fact]
    public async Task Roll_template_levels_and_draw_nothing_more_when_the_map_has_no_bands()
    {
        var spawned = new List<Spawned>();
        var templates = new Dictionary<int, ChunkTemplate> { [1] = Piece(1, "pack", "boss") };

        await Service(Recording(spawned), templates, Entry(1, "pack", 4, 2, 3), Entry(7, "boss", 10, 1, 1))
            .PlaceAsync(FlatInstance(), Layout(new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero, 9, "forest_arena")),
                Config([]), seed: 5, CancellationToken.None);

        Assert.NotEmpty(spawned);
        Assert.All(spawned, s => Assert.Null(s.Level));
    }

    /// <summary>
    /// What the placement before depth bands (commit 4722e94d) spawned for this layout and seed: which template, where.
    /// Any change to the order or number of random draws on a map without bands changes it, so an old seed would no
    /// longer place the same creatures in the same spots.
    /// </summary>
    [Fact]
    public async Task Place_the_creatures_a_seed_placed_before_depth_bands_when_the_map_has_none()
    {
        var spawned = new List<(ulong Template, float X, float Y, float Z)>();
        ICreatureSpawner spawner = Substitute.For<ICreatureSpawner>();
        uint next = 1;
        spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(ci =>
        {
            CreatureInfo info = ci.Arg<CreatureInfo>();
            spawned.Add((info.PrototypeIndex, info.Position.x, info.Position.y, info.Position.z));
            ICreature creature = Substitute.For<ICreature>();
            creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, next++));
            creature.ScriptName.Returns(string.Empty);
            return creature;
        });
        var templates = new Dictionary<int, ChunkTemplate>
        {
            [1] = Slots(1, ("entry", 15, 15), ("pack", 8, 20), ("pack", 22, 9), ("rare", 15, 25)),
            [2] = Slots(2, ("pack", 10, 12), ("boss", 20, 22)),
        };
        PlacedChunk[] chunks =
        [
            new(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero, 0, null),
            new(new ChunkTemplateId(2), 1, 0, 1, new Vector3(30, 0, 0), 1, null),
        ];
        var layout = new ChunkLayout(Seed: 1, Chunks: chunks, EntryChunk: chunks[0], BossChunk: chunks[1], Portals: [],
            EntrySpawnWorldPos: Vector3.zero, CellSize: 30f);
        SpawnTableEntry pack = Entry(1, "pack", 4, 2, 3);
        pack.Weight = 3f;

        await Service(spawner, templates, pack, Entry(2, "pack", 5, 1, 2), Entry(3, "rare", 6, 1, 1), Entry(4, "boss", 10, 1, 1))
            .PlaceAsync(FlatInstance(), layout, Config([]), seed: 5, CancellationToken.None);

        Assert.Equal(
        [
            (4, 7.288888f, 1f, 20.376127f), (4, 7.8903856f, 1f, 21.285122f), (4, 22.2884f, 1f, 7.85238f),
            (4, 23.426678f, 1f, 8.61266f), (4, 20.709743f, 1f, 7.7123647f), (6, 15f, 1f, 25f), (5, 42f, 1f, 20f),
            (10, 52f, 1f, 10f),
        ], spawned);
    }

    private static ChunkTemplate Slots(int id, params (string Tag, float X, float Z)[] slots) => new()
    {
        Id = new ChunkTemplateId(id),
        Name = $"p{id}",
        SpawnSlots = slots.Select(s => new ChunkSpawnSlot { Tag = s.Tag, LocalX = s.X, LocalY = 1, LocalZ = s.Z }).ToList(),
    };

    [Fact]
    public async Task Leave_a_depth_no_band_covers_to_the_template()
    {
        var spawned = new List<Spawned>();
        var templates = new Dictionary<int, ChunkTemplate> { [1] = Piece(1, "pack") };

        await Service(Recording(spawned), templates, Entry(1, "pack", 4, 1, 1))
            .PlaceAsync(FlatInstance(), Layout(new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero, 0, null)),
                Config(s_bands), seed: 5, CancellationToken.None);

        Assert.Equal([new Spawned(4, null)], spawned);
    }

    /// <summary>
    /// The members of one set piece sit up to two steps apart; every one of them rolls from the highest band, decided by
    /// the chunk's group, never by its own depth.
    /// </summary>
    [Fact]
    public async Task Roll_every_member_of_a_set_piece_from_the_highest_band_whatever_its_depth()
    {
        var spawned = new List<Spawned>();
        var templates = new Dictionary<int, ChunkTemplate>
        {
            [1] = Piece(1, "pack"),
            [2] = Piece(2, "pack"),
            [3] = Piece(3, "pack"),
            [4] = Piece(4, "pack"),
        };
        CreaturePlacementService service = Service(Recording(spawned), templates, Entry(1, "pack", 4, 1, 1));

        for (int seed = 0; seed < 20; seed++)
        {
            spawned.Clear();
            await service.PlaceAsync(FlatInstance(), Layout(
                    new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero, 2, "forest_grove_ruin"),
                    new PlacedChunk(new ChunkTemplateId(2), 1, 0, 0, Vector3.zero, 3, "forest_grove_ruin"),
                    new PlacedChunk(new ChunkTemplateId(3), 0, 1, 0, Vector3.zero, 3, "forest_grove_ruin"),
                    new PlacedChunk(new ChunkTemplateId(4), 1, 1, 0, Vector3.zero, 4, "forest_grove_ruin")),
                Config(s_bands), seed, CancellationToken.None);

            Assert.Equal(4, spawned.Count);
            Assert.All(spawned, s => Assert.InRange((int)s.Level!.Value, 5, 8));
        }
    }
}
