// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Services;

public class ObservabilityServiceShould
{
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IWorldRepository _worlds = Substitute.For<IWorldRepository>();
    private readonly IMapTemplateRepository _maps = Substitute.For<IMapTemplateRepository>();
    private readonly IProceduralMapConfigRepository _configs = Substitute.For<IProceduralMapConfigRepository>();
    private readonly IChunkPoolRepository _pools = Substitute.For<IChunkPoolRepository>();
    private readonly IChunkTemplateRepository _chunks = Substitute.For<IChunkTemplateRepository>();

    private static readonly Guid InstanceId = Guid.Parse("8f3c1d2e-0000-0000-0000-000000000001");

    private static WorldPresenceSnapshot Snapshot(string configVersion, params CharacterPresenceSnapshot[] characters) => new(
        WorldId: 1,
        CapturedAt: DateTime.UtcNow,
        Instances:
        [
            new InstancePresenceSnapshot(
                InstanceId, TemplateId: 12, Seed: 999, MapType: "Normal",
                ConfigVersion: configVersion, OwnerCharacterId: 4417, Characters: characters)
        ]);

    private static CharacterPresenceSnapshot Char(uint id, string name, ushort level = 34) => new(
        CharacterId: id, Name: name, Class: "Wizard", X: 1f, Y: 2f, Z: 3f, Orientation: 0f,
        Level: level, CurrentHealth: 100, Health: 200,
        MoveState: "Idle", InCombat: false, Dead: false, LastSeen: DateTime.UtcNow);

    private ObservabilityService CreateSut()
    {
        _worlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        [
            new AvalonWorld { Id = new WorldId(1), Name = "Aurora" },
        ]);
        _maps.FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
             .Returns(new MapTemplate { Id = new MapTemplateId(12), Name = "Crypt" });
        return new ObservabilityService(
            _cache, _worlds, _maps, _configs, _pools, _chunks, NullLogger<ObservabilityService>.Instance);
    }

    /// <summary>
    /// Stubs the generator-input repositories with a fixed config and pool, and returns
    /// the stamp those inputs genuinely hash to. Tests then seed the snapshot with either
    /// that value (fresh) or a different one (drifted).
    /// </summary>
    private string GivenGeneratorInputs()
    {
        var config = new ProceduralMapConfig
        {
            MapTemplateId = new MapTemplateId(12),
            ChunkPoolId = new ChunkPoolId(3),
            SpawnTableId = new SpawnTableId(1),
            MainPathMin = 5,
            MainPathMax = 9,
            BranchChance = 0.25f,
            BranchMaxDepth = 2,
            HasBoss = true,
            BackPortalTargetMapId = 1,
            ForwardPortalTargetMapId = 14,
        };

        var template = new ChunkTemplate
        {
            Id = new ChunkTemplateId(1),
            Name = "chunk-1",
            AssetKey = "asset-1",
            GeometryFile = "a.obj",
            CellFootprintX = 1,
            CellFootprintZ = 1,
            CellSize = 30.0f,
            Exits = 0b1010,
        };

        var pool = new ChunkPool
        {
            Id = new ChunkPoolId(3),
            Memberships = [new ChunkPoolMembership { ChunkTemplateId = template.Id, Weight = 1.0f }],
        };

        _configs.FindByTemplateIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
                .Returns(config);
        _pools.FindAllWithMembershipsAsync(Arg.Any<CancellationToken>())
              .Returns<IReadOnlyList<ChunkPool>>([pool]);
        _chunks.FindAllWithSlotsAsync(Arg.Any<CancellationToken>())
               .Returns<IReadOnlyList<ChunkTemplate>>([template]);

        return LayoutConfigVersion.Compute(config, [new ChunkPoolMember(template, 1.0f)]);
    }

    private void GivenWorldSnapshot(WorldPresenceSnapshot snapshot)
    {
        _cache.GetAsync(CacheKeys.WorldPresence(1)).Returns(PresenceJson.Serialize(snapshot));
    }

    private void GivenCharacterIndex(uint characterId, ushort worldId = 1)
    {
        _cache.GetAsync(CacheKeys.CharacterPresenceIndex(characterId))
              .Returns(PresenceJson.Serialize(new CharacterPresenceIndex(worldId, InstanceId)));
    }

    [Fact]
    public async Task Should_return_empty_page_when_no_world_has_presence()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns((string?)null);

        PagedResult<OnlinePlayerDto> page =
            await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Should_flatten_snapshot_into_roster_rows()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(4417, "Nym"), Char(9002, "Kel")));

        PagedResult<OnlinePlayerDto> page =
            await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, r => r.Name == "Nym" && r.WorldName == "Aurora");
        Assert.All(page.Items, r => Assert.Equal(MapType.Normal, r.MapType));
        Assert.All(page.Items, r => Assert.Equal("Crypt", r.TemplateName));
    }

    [Fact]
    public async Task Should_filter_roster_by_name_case_insensitively()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(4417, "Nym"), Char(9002, "Kel")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { NameLike = "ny" }, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal("Nym", page.Items[0].Name);
    }

    [Fact]
    public async Task Should_page_the_roster()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A"), Char(2, "B"), Char(3, "C")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { Page = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task Should_maintain_stable_order_across_page_boundary()
    {
        // Inserted out of alphabetical order so the assertion only passes if the service
        // actually sorts, rather than happening to preserve insertion order.
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "Zed"), Char(2, "Amy"), Char(3, "Mno")));

        ObservabilityService sut = CreateSut();
        PagedResult<OnlinePlayerDto> page1 =
            await sut.GetOnlineAsync(new PresencePaginateFilters { Page = 1, PageSize = 2 }, CancellationToken.None);
        PagedResult<OnlinePlayerDto> page2 =
            await sut.GetOnlineAsync(new PresencePaginateFilters { Page = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(["Amy", "Mno"], page1.Items.Select(r => r.Name).ToArray());
        Assert.Equal(["Zed"], page2.Items.Select(r => r.Name).ToArray());

        // Every row appears exactly once across the two pages combined.
        List<uint> combinedIds = page1.Items.Concat(page2.Items).Select(r => r.CharacterId).ToList();
        Assert.Equal([2u, 3u, 1u], combinedIds);
    }

    [Fact]
    public async Task Should_clamp_page_size_above_fifty_to_fifty()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { PageSize = 200 }, CancellationToken.None);

        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Should_clamp_page_size_below_one_to_fifty()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { PageSize = 0 }, CancellationToken.None);

        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Should_not_clamp_page_size_at_the_fifty_boundary()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { PageSize = 50 }, CancellationToken.None);

        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Should_return_empty_items_when_page_is_past_the_end()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A"), Char(2, "B")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { Page = 99 }, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Should_return_null_map_type_for_unrecognized_value()
    {
        WorldPresenceSnapshot snapshot = new(
            WorldId: 1,
            CapturedAt: DateTime.UtcNow,
            Instances:
            [
                new InstancePresenceSnapshot(
                    InstanceId, TemplateId: 12, Seed: 999, MapType: "Wasteland",
                    ConfigVersion: "a91f3c7e", OwnerCharacterId: 4417,
                    Characters: [Char(4417, "Nym")])
            ]);
        GivenWorldSnapshot(snapshot);

        PagedResult<OnlinePlayerDto> page =
            await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].MapType);
    }

    [Fact]
    public async Task Should_memoize_template_name_lookups_across_instances_in_a_request()
    {
        WorldPresenceSnapshot snapshot = new(
            WorldId: 1,
            CapturedAt: DateTime.UtcNow,
            Instances:
            [
                new InstancePresenceSnapshot(
                    InstanceId, TemplateId: 12, Seed: 1, MapType: "Normal",
                    ConfigVersion: "a", OwnerCharacterId: null, Characters: [Char(1, "A")]),
                new InstancePresenceSnapshot(
                    Guid.NewGuid(), TemplateId: 12, Seed: 2, MapType: "Normal",
                    ConfigVersion: "b", OwnerCharacterId: null, Characters: [Char(2, "B")]),
            ]);
        GivenWorldSnapshot(snapshot);

        await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), CancellationToken.None);

        // Both instances share TemplateId 12 — the repository should be consulted once
        // per request, not once per instance.
        await _maps.Received(1)
            .FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_return_null_when_character_has_no_index_entry()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns((string?)null);

        Assert.Null(await CreateSut().GetPlayerPresenceAsync(4417, CancellationToken.None));
    }

    [Fact]
    public async Task Should_return_null_when_index_points_at_an_expired_snapshot()
    {
        GivenCharacterIndex(4417);
        _cache.GetAsync(CacheKeys.WorldPresence(1)).Returns((string?)null);

        Assert.Null(await CreateSut().GetPlayerPresenceAsync(4417, CancellationToken.None));
    }

    [Fact]
    public async Task Should_return_target_and_instance_mates()
    {
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(4417, "Nym"), Char(9002, "Kel")));

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(4417, CancellationToken.None);

        Assert.NotNull(presence);
        Assert.Equal("Nym", presence!.Target.Name);
        Assert.Equal(2, presence.Instance.Characters.Count);
        Assert.Equal(999, presence.Instance.Seed);
        Assert.Equal((ushort)1, presence.Instance.WorldId);
    }

    [Fact]
    public async Task Should_return_null_when_instance_is_not_present()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns((string?)null);

        Assert.Null(await CreateSut().GetInstancePresenceAsync(InstanceId, CancellationToken.None));
    }

    [Fact]
    public async Task Should_not_flag_stale_when_config_version_still_matches()
    {
        string current = GivenGeneratorInputs();
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot(current, Char(4417, "Nym")));

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(4417, CancellationToken.None);

        Assert.False(presence!.LayoutStale);
    }

    [Fact]
    public async Task Should_flag_stale_when_config_version_has_drifted()
    {
        GivenGeneratorInputs();
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("ffffffff", Char(4417, "Nym")));

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(4417, CancellationToken.None);

        Assert.True(presence!.LayoutStale);
    }

    [Fact]
    public async Task Should_not_flag_stale_when_the_instance_has_no_stamp()
    {
        // Predefined (town) layouts carry an empty ConfigVersion. Absence of a stamp is
        // not evidence of drift, so it must not surface a warning banner.
        GivenGeneratorInputs();
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("", Char(4417, "Nym")));

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(4417, CancellationToken.None);

        Assert.False(presence!.LayoutStale);
    }
}
