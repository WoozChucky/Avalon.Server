using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

public class ChunkLibraryShould
{
    [Fact]
    public async Task Throw_when_pool_has_no_entry_chunk()
    {
        var templateRepo = Substitute.For<IChunkTemplateRepository>();
        var poolRepo     = Substitute.For<IChunkPoolRepository>();
        var configRepo   = Substitute.For<IProceduralMapConfigRepository>();

        var template = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1),
            Name = "NoEntry",
            AssetKey = "Chunks/NoEntry",
            GeometryFile = "Chunks/NoEntry.obj",
            Exits = 0b_000_010_000_010,    // N-center + S-center only
            SpawnSlots = new(),            // no entry
            PortalSlots = new()
        };
        var pool = new ChunkPool
        {
            Id = new ChunkPoolId(1),
            Name = "p1",
            Memberships = new List<ChunkPoolMembership>
            {
                new() { ChunkPoolId = new ChunkPoolId(1), ChunkTemplateId = new ChunkTemplateId(1), Template = template }
            }
        };
        var config = new ProceduralMapConfig
        {
            MapTemplateId = new MapTemplateId(10),
            ChunkPoolId = new ChunkPoolId(1),
            SpawnTableId = new SpawnTableId(1),
            MainPathMin = 2, MainPathMax = 4,
            BackPortalTargetMapId = 1
        };

        templateRepo.FindAllWithSlotsAsync(Arg.Any<CancellationToken>()).Returns([template]);
        poolRepo.FindAllWithMembershipsAsync(Arg.Any<CancellationToken>()).Returns([pool]);
        configRepo.FindAllAsync(Arg.Any<CancellationToken>()).Returns([config]);

        var services = new ServiceCollection()
            .AddScoped(_ => templateRepo)
            .AddScoped(_ => poolRepo)
            .AddScoped(_ => configRepo)
            .BuildServiceProvider();
        var lib = new ChunkLibrary(NullLoggerFactory.Instance, new DummyScopeFactory(services));

        await Assert.ThrowsAsync<InvalidProceduralConfigException>(() => lib.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Load_succeeds_for_valid_pool()
    {
        // Entry chunk with all required slots.
        var entryChunk = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1),
            Name = "Entry", AssetKey = "Chunks/Entry", GeometryFile = "Chunks/Entry.obj",
            Exits = 0b_000_010_000_010,
            SpawnSlots = new List<ChunkSpawnSlot> { new() { Tag = "entry" } },
            PortalSlots = new List<ChunkPortalSlot> { new() { Role = PortalRole.Back } }
        };
        var lib = BuildLibraryWith(
            templates: new List<ChunkTemplate> { entryChunk },
            pools: new List<ChunkPool>
            {
                new()
                {
                    Id = new ChunkPoolId(1), Name = "p1",
                    Memberships = new List<ChunkPoolMembership>
                    {
                        new() { ChunkPoolId = new ChunkPoolId(1), ChunkTemplateId = entryChunk.Id, Template = entryChunk }
                    }
                }
            },
            configs: new List<ProceduralMapConfig>
            {
                new()
                {
                    MapTemplateId = new MapTemplateId(10),
                    ChunkPoolId = new ChunkPoolId(1),
                    SpawnTableId = new SpawnTableId(1),
                    MainPathMin = 2, MainPathMax = 4,
                    BackPortalTargetMapId = 1,
                    HasBoss = false,
                    ForwardPortalTargetMapId = null,
                }
            });

        await lib.LoadAsync(CancellationToken.None);
        Assert.Single(lib.GetByPool(new ChunkPoolId(1)));
    }

    [Fact]
    public async Task Throw_when_HasBoss_but_no_boss_chunk()
    {
        var entryChunk = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1), Name = "Entry",
            SpawnSlots = new List<ChunkSpawnSlot> { new() { Tag = "entry" } },
            PortalSlots = new List<ChunkPortalSlot> { new() { Role = PortalRole.Back } }
        };
        var lib = BuildLibraryWith(
            templates: new List<ChunkTemplate> { entryChunk },
            pools: new List<ChunkPool>
            {
                new()
                {
                    Id = new ChunkPoolId(1),
                    Memberships = new List<ChunkPoolMembership>
                    {
                        new() { ChunkPoolId = new ChunkPoolId(1), ChunkTemplateId = entryChunk.Id, Template = entryChunk }
                    }
                }
            },
            configs: new List<ProceduralMapConfig>
            {
                new()
                {
                    MapTemplateId = new MapTemplateId(11),
                    ChunkPoolId = new ChunkPoolId(1),
                    SpawnTableId = new SpawnTableId(1),
                    MainPathMin = 2, MainPathMax = 2,
                    BackPortalTargetMapId = 1,
                    HasBoss = true,
                }
            });

        await Assert.ThrowsAsync<InvalidProceduralConfigException>(() => lib.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Throw_when_ForwardPortal_set_but_no_forward_chunk()
    {
        var entryChunk = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1), Name = "Entry",
            SpawnSlots = new List<ChunkSpawnSlot> { new() { Tag = "entry" } },
            PortalSlots = new List<ChunkPortalSlot> { new() { Role = PortalRole.Back } }
        };
        var lib = BuildLibraryWith(
            templates: new List<ChunkTemplate> { entryChunk },
            pools: new List<ChunkPool>
            {
                new()
                {
                    Id = new ChunkPoolId(1),
                    Memberships = new List<ChunkPoolMembership>
                    {
                        new() { ChunkPoolId = new ChunkPoolId(1), ChunkTemplateId = entryChunk.Id, Template = entryChunk }
                    }
                }
            },
            configs: new List<ProceduralMapConfig>
            {
                new()
                {
                    MapTemplateId = new MapTemplateId(12),
                    ChunkPoolId = new ChunkPoolId(1),
                    SpawnTableId = new SpawnTableId(1),
                    MainPathMin = 2, MainPathMax = 2,
                    BackPortalTargetMapId = 1,
                    ForwardPortalTargetMapId = 999,
                }
            });

        await Assert.ThrowsAsync<InvalidProceduralConfigException>(() => lib.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Throw_when_a_maps_depth_bands_overlap()
    {
        var entryChunk = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1), Name = "Entry",
            SpawnSlots = new List<ChunkSpawnSlot> { new() { Tag = "entry" } },
            PortalSlots = new List<ChunkPortalSlot> { new() { Role = PortalRole.Back } }
        };
        var lib = BuildLibraryWith(
            templates: new List<ChunkTemplate> { entryChunk },
            pools: new List<ChunkPool>
            {
                new()
                {
                    Id = new ChunkPoolId(1),
                    Memberships = new List<ChunkPoolMembership>
                    {
                        new() { ChunkPoolId = new ChunkPoolId(1), ChunkTemplateId = entryChunk.Id, Template = entryChunk }
                    }
                }
            },
            configs: new List<ProceduralMapConfig>
            {
                new()
                {
                    MapTemplateId = new MapTemplateId(13),
                    ChunkPoolId = new ChunkPoolId(1),
                    SpawnTableId = new SpawnTableId(1),
                    MainPathMin = 2, MainPathMax = 2,
                    BackPortalTargetMapId = 1,
                    DepthBands =
                    [
                        new ProceduralDepthBand { MinDepth = 1, MaxDepth = 5, MinLevel = 1, MaxLevel = 3 },
                        new ProceduralDepthBand { MinDepth = 3, MaxDepth = null, MinLevel = 3, MaxLevel = 6 },
                    ],
                }
            });

        await Assert.ThrowsAsync<InvalidProceduralConfigException>(() => lib.LoadAsync(CancellationToken.None));
    }

    private static (ChunkTemplate Entry, ChunkPool Pool, List<ChunkTemplate> Templates) PoolWithBossGroup()
    {
        var entry = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1), Name = "Entry", Exits = 0b_000_000_000_010,
            SpawnSlots = new List<ChunkSpawnSlot> { new() { Tag = "entry" } },
            PortalSlots = new List<ChunkPortalSlot> { new() { Role = PortalRole.Back } }
        };
        var a = new ChunkTemplate { Id = new ChunkTemplateId(2), Name = "a", Exits = 0b_000_010_000_000 };
        var b = new ChunkTemplate { Id = new ChunkTemplateId(3), Name = "b", SpawnSlots = new List<ChunkSpawnSlot> { new() { Tag = "boss" } } };
        var pool = new ChunkPool
        {
            Id = new ChunkPoolId(1), Name = "p1",
            Memberships = new List<ChunkPoolMembership>
            {
                new() { ChunkPoolId = new ChunkPoolId(1), ChunkTemplateId = entry.Id, Template = entry }
            },
            Groups = new List<ChunkGroup>
            {
                new()
                {
                    Id = 1, Name = "arena", ChunkPoolId = new ChunkPoolId(1),
                    Members = new List<ChunkGroupMember>
                    {
                        new() { ChunkGroupId = 1, ChunkTemplateId = a.Id, CellX = 0, CellZ = 0 },
                        new() { ChunkGroupId = 1, ChunkTemplateId = b.Id, CellX = 1, CellZ = 0 },
                    }
                }
            }
        };
        return (entry, pool, [entry, a, b]);
    }

    [Fact]
    public async Task Hand_out_a_pools_groups_with_their_templates()
    {
        (_, ChunkPool pool, List<ChunkTemplate> templates) = PoolWithBossGroup();
        var lib = BuildLibraryWith(templates, [pool], configs: []);

        await lib.LoadAsync(CancellationToken.None);

        ChunkGroupDefinition group = Assert.Single(lib.GetGroupsByPool(new ChunkPoolId(1)));
        Assert.Equal("arena", group.Name);
        Assert.Equal(["a", "b"], group.Cells.OrderBy(c => c.CellX).Select(c => c.Template.Name));
        Assert.Empty(lib.GetGroupsByPool(new ChunkPoolId(9)));
    }

    [Fact]
    public async Task Count_a_boss_group_as_the_pools_boss()
    {
        (_, ChunkPool pool, List<ChunkTemplate> templates) = PoolWithBossGroup();
        var lib = BuildLibraryWith(templates, [pool], configs:
        [
            new ProceduralMapConfig
            {
                MapTemplateId = new MapTemplateId(14), ChunkPoolId = new ChunkPoolId(1), SpawnTableId = new SpawnTableId(1),
                MainPathMin = 2, MainPathMax = 2, BackPortalTargetMapId = 1, HasBoss = true,
            }
        ]);

        await lib.LoadAsync(CancellationToken.None);   // no InvalidProceduralConfigException
    }

    // Helper extracts repeated ServiceCollection + scope-factory wiring.
    private static ChunkLibrary BuildLibraryWith(
        IReadOnlyList<ChunkTemplate> templates,
        IReadOnlyList<ChunkPool> pools,
        IReadOnlyList<ProceduralMapConfig> configs)
    {
        var templateRepo = Substitute.For<IChunkTemplateRepository>();
        var poolRepo     = Substitute.For<IChunkPoolRepository>();
        var configRepo   = Substitute.For<IProceduralMapConfigRepository>();
        templateRepo.FindAllWithSlotsAsync(Arg.Any<CancellationToken>()).Returns(templates);
        poolRepo.FindAllWithMembershipsAsync(Arg.Any<CancellationToken>()).Returns(pools);
        configRepo.FindAllAsync(Arg.Any<CancellationToken>()).Returns(configs);

        var services = new ServiceCollection()
            .AddScoped(_ => templateRepo)
            .AddScoped(_ => poolRepo)
            .AddScoped(_ => configRepo)
            .BuildServiceProvider();

        return new ChunkLibrary(NullLoggerFactory.Instance, new DummyScopeFactory(services));
    }

    private sealed class DummyScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _sp;
        public DummyScopeFactory(IServiceProvider sp) => _sp = sp;
        public IServiceScope CreateScope() => new Scope(_sp);
        private sealed class Scope : IServiceScope
        {
            public Scope(IServiceProvider sp) => ServiceProvider = sp;
            public IServiceProvider ServiceProvider { get; }
            public void Dispose() { }
        }
    }
}
