// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Services;

public class ObservabilityServiceShould
{
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IWorldRepository _worlds = Substitute.For<IWorldRepository>();
    private readonly IMapTemplateRepository _maps = Substitute.For<IMapTemplateRepository>();
    private readonly IProceduralMapConfigRepository _configs = Substitute.For<IProceduralMapConfigRepository>();
    private readonly IProceduralLayoutInputsResolver _inputsResolver = Substitute.For<IProceduralLayoutInputsResolver>();
    private readonly IWorldRepositories _perWorld = Substitute.For<IWorldRepositories>();
    private readonly WorldDatabases _databases = new(
    [
        new ConfiguredWorld(new WorldId(1), "Host=w1", "Host=c1"),
        new ConfiguredWorld(new WorldId(2), "Host=w2", "Host=c2"),
    ]);
    private readonly CapturingLogger _logger = new();
    private const AccountAccessLevel Gm = AccountAccessLevel.GameMaster;

    // Real (not substituted) so the caching test exercises genuine TTL/eviction behavior rather
    // than a mock standing in for it.
    private readonly IMemoryCache _poolMemberCache = new MemoryCache(new MemoryCacheOptions());

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
            new AvalonWorld { Id = new WorldId(1), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Player },
        ]);
        _worlds.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<AvalonWorld?>(new AvalonWorld
            {
                Id = call.Arg<WorldId>(), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Player,
            }));
        _maps.FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
             .Returns(new MapTemplate { Id = new MapTemplateId(12), Name = "Crypt" });
        _perWorld.MapTemplates(Arg.Any<WorldId>()).Returns(_maps);
        _perWorld.ProceduralMapConfigs(Arg.Any<WorldId>()).Returns(_configs);
        _perWorld.LayoutInputs(Arg.Any<WorldId>()).Returns(_inputsResolver);
        return new ObservabilityService(
            _cache, _worlds, _databases, _perWorld, _poolMemberCache, _logger);
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

        List<ChunkPoolMember> members = [new ChunkPoolMember(template, 1.0f)];
        Dictionary<ChunkTemplateId, ChunkTemplate> byId = new() { [template.Id] = template };

        _configs.FindByTemplateIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
                .Returns(config);
        _inputsResolver.FindPoolAsync(Arg.Any<ChunkPoolId>(), Arg.Any<CancellationToken>())
                       .Returns(pool);
        _inputsResolver.ResolveMembersAsync(Arg.Any<ChunkPool>(), Arg.Any<CancellationToken>())
                       .Returns(new ProceduralPoolResolution(members, byId));

        return LayoutConfigVersion.Compute(config, members);
    }

    private void GivenWorldSnapshot(WorldPresenceSnapshot snapshot)
    {
        _cache.GetAsync(CacheKeys.WorldPresence(1)).Returns(PresenceJson.Serialize(snapshot));
    }

    private void GivenCharacterIndex(uint characterId, ushort worldId = 1)
    {
        _cache.GetAsync(CacheKeys.CharacterPresenceIndex(worldId, characterId))
              .Returns(PresenceJson.Serialize(new CharacterPresenceIndex(worldId, InstanceId)));
    }

    [Fact]
    public async Task Should_return_empty_page_when_no_world_has_presence()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns((string?)null);

        PagedResult<OnlinePlayerDto> page =
            await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Should_flatten_snapshot_into_roster_rows()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(4417, "Nym"), Char(9002, "Kel")));

        PagedResult<OnlinePlayerDto> page =
            await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

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
            .GetOnlineAsync(new PresencePaginateFilters { NameLike = "ny" }, Gm, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal("Nym", page.Items[0].Name);
    }

    [Fact]
    public async Task Should_page_the_roster()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A"), Char(2, "B"), Char(3, "C")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { Page = 2, PageSize = 2 }, Gm, CancellationToken.None);

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
            await sut.GetOnlineAsync(new PresencePaginateFilters { Page = 1, PageSize = 2 }, Gm, CancellationToken.None);
        PagedResult<OnlinePlayerDto> page2 =
            await sut.GetOnlineAsync(new PresencePaginateFilters { Page = 2, PageSize = 2 }, Gm, CancellationToken.None);

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
            .GetOnlineAsync(new PresencePaginateFilters { PageSize = 200 }, Gm, CancellationToken.None);

        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Should_clamp_page_size_below_one_to_fifty()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { PageSize = 0 }, Gm, CancellationToken.None);

        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Should_not_clamp_page_size_at_the_fifty_boundary()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { PageSize = 50 }, Gm, CancellationToken.None);

        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public async Task Should_return_empty_items_when_page_is_past_the_end()
    {
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "A"), Char(2, "B")));

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { Page = 99 }, Gm, CancellationToken.None);

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
            await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

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

        await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        // Both instances share TemplateId 12 — the repository should be consulted once
        // per request, not once per instance.
        await _maps.Received(1)
            .FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_return_null_when_character_has_no_index_entry()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns((string?)null);

        Assert.Null(await CreateSut().GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Should_return_null_when_index_points_at_an_expired_snapshot()
    {
        GivenCharacterIndex(4417);
        _cache.GetAsync(CacheKeys.WorldPresence(1)).Returns((string?)null);

        Assert.Null(await CreateSut().GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Should_return_target_and_instance_mates()
    {
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(4417, "Nym"), Char(9002, "Kel")));

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);

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

        Assert.Null(await CreateSut().GetInstancePresenceAsync(InstanceId, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Should_not_flag_stale_when_config_version_still_matches()
    {
        string current = GivenGeneratorInputs();
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot(current, Char(4417, "Nym")));

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);

        Assert.False(presence!.LayoutStale);
    }

    [Fact]
    public async Task Should_flag_stale_when_config_version_has_drifted()
    {
        GivenGeneratorInputs();
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("ffffffff", Char(4417, "Nym")));

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);

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

        PlayerPresenceDto? presence = await CreateSut().GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);

        Assert.False(presence!.LayoutStale);
    }

    [Fact]
    public async Task Should_treat_a_snapshot_missing_instances_as_having_none()
    {
        // A blob written from a different build: System.Text.Json binds a positional
        // record's parameters by name and fills `default` for anything absent, so this
        // deserializes to a non-null WorldPresenceSnapshot whose Instances is null rather
        // than throwing. That must read as "no data", not a NullReferenceException.
        string raw = $$"""{"worldId":1,"capturedAt":"{{DateTime.UtcNow:O}}","version":{{WorldPresenceSnapshot.CurrentVersion}}}""";
        _cache.GetAsync(CacheKeys.WorldPresence(1)).Returns(raw);

        PagedResult<OnlinePlayerDto> page =
            await CreateSut().GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Should_treat_an_instance_missing_characters_as_an_empty_roster()
    {
        string raw = $$"""
            {"worldId":1,"capturedAt":"{{DateTime.UtcNow:O}}","version":{{WorldPresenceSnapshot.CurrentVersion}},"instances":[
                {"instanceId":"{{InstanceId}}","templateId":12,"seed":999,"mapType":"Normal","configVersion":"a91f3c7e","ownerCharacterId":4417}
            ]}
            """;
        _cache.GetAsync(CacheKeys.WorldPresence(1)).Returns(raw);

        InstancePresenceDto? instance =
            await CreateSut().GetInstancePresenceAsync(InstanceId, Gm, CancellationToken.None);

        Assert.NotNull(instance);
        Assert.Empty(instance!.Characters);
    }

    [Fact]
    public async Task Should_treat_a_snapshot_with_an_unrecognized_version_as_absent()
    {
        // Otherwise-valid payload, but stamped with a schema version this build does not
        // recognise. It must be discarded wholesale -- the instance inside it is real data
        // and would return non-null if the version gate were missing.
        string raw = $$"""
            {"worldId":1,"capturedAt":"{{DateTime.UtcNow:O}}","version":99999,"instances":[
                {"instanceId":"{{InstanceId}}","templateId":12,"seed":999,"mapType":"Normal","configVersion":"a91f3c7e","ownerCharacterId":4417,"characters":[]}
            ]}
            """;
        _cache.GetAsync(CacheKeys.WorldPresence(1)).Returns(raw);

        InstancePresenceDto? instance =
            await CreateSut().GetInstancePresenceAsync(InstanceId, Gm, CancellationToken.None);

        Assert.Null(instance);
    }

    [Fact]
    public async Task Should_filter_roster_by_world_id()
    {
        ObservabilityService sut = CreateSut();
        _worlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        [
            new AvalonWorld { Id = new WorldId(1), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Player },
            new AvalonWorld { Id = new WorldId(2), Name = "Boreal", AccessLevelRequired = AccountAccessLevel.Player },
        ]);
        GivenWorldSnapshot(Snapshot("a91f3c7e", Char(1, "Nym"))); // world 1
        _cache.GetAsync(CacheKeys.WorldPresence(2)).Returns(PresenceJson.Serialize(new WorldPresenceSnapshot(
            WorldId: 2,
            CapturedAt: DateTime.UtcNow,
            Instances:
            [
                new InstancePresenceSnapshot(
                    Guid.NewGuid(), TemplateId: 12, Seed: 2, MapType: "Normal",
                    ConfigVersion: "b", OwnerCharacterId: null, Characters: [Char(2, "Zed")])
            ])));

        PagedResult<OnlinePlayerDto> page =
            await sut.GetOnlineAsync(new PresencePaginateFilters { WorldId = 2 }, Gm, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal("Zed", page.Items[0].Name);
    }

    [Fact]
    public async Task Should_filter_roster_by_template_id()
    {
        WorldPresenceSnapshot snapshot = new(
            WorldId: 1,
            CapturedAt: DateTime.UtcNow,
            Instances:
            [
                new InstancePresenceSnapshot(
                    InstanceId, TemplateId: 12, Seed: 1, MapType: "Normal",
                    ConfigVersion: "a", OwnerCharacterId: null, Characters: [Char(1, "Nym")]),
                new InstancePresenceSnapshot(
                    Guid.NewGuid(), TemplateId: 99, Seed: 2, MapType: "Normal",
                    ConfigVersion: "b", OwnerCharacterId: null, Characters: [Char(2, "Zed")]),
            ]);
        GivenWorldSnapshot(snapshot);

        PagedResult<OnlinePlayerDto> page = await CreateSut()
            .GetOnlineAsync(new PresencePaginateFilters { TemplateId = 99 }, Gm, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal("Zed", page.Items[0].Name);
    }

    [Fact]
    public async Task Should_cache_pool_member_resolution_across_polls()
    {
        // The admin dashboard polls GetPlayerPresenceAsync every 1.5s. Without caching, an
        // open player-detail view would re-run both full-table reads behind
        // IProceduralLayoutInputsResolver on every single poll.
        string current = GivenGeneratorInputs();
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot(current, Char(4417, "Nym")));

        ObservabilityService sut = CreateSut();
        await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);
        await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);

        await _inputsResolver.Received(1).FindPoolAsync(Arg.Any<ChunkPoolId>(), Arg.Any<CancellationToken>());
        await _inputsResolver.Received(1).ResolveMembersAsync(Arg.Any<ChunkPool>(), Arg.Any<CancellationToken>());
    }

    private void GivenWorldTwoSnapshot(params CharacterPresenceSnapshot[] characters) =>
        _cache.GetAsync(CacheKeys.WorldPresence(2)).Returns(PresenceJson.Serialize(new WorldPresenceSnapshot(
            WorldId: 2,
            CapturedAt: DateTime.UtcNow,
            Instances:
            [
                new InstancePresenceSnapshot(
                    Guid.Parse("8f3c1d2e-0000-0000-0000-000000000002"), TemplateId: 12, Seed: 2, MapType: "Normal",
                    ConfigVersion: "", OwnerCharacterId: null, Characters: characters)
            ])));

    private void GivenTwoWorlds(AccountAccessLevel secondRequires) =>
        _worlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        [
            new AvalonWorld { Id = new WorldId(1), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Player },
            new AvalonWorld { Id = new WorldId(2), Name = "Boreal", AccessLevelRequired = secondRequires },
        ]);

    private static readonly Guid WorldTwoInstanceId = Guid.Parse("8f3c1d2e-0000-0000-0000-000000000002");

    /// <summary>World 1 and world 2 each have a character 7 online (#556: ids are unique only per world).</summary>
    private void GivenCharacterSevenInBothWorlds()
    {
        GivenTwoWorlds(AccountAccessLevel.Player);
        GivenWorldSnapshot(Snapshot("a", Char(7, "Nym")));
        GivenWorldTwoSnapshot(Char(7, "Zed"));
        GivenCharacterIndex(7, worldId: 1);
        _cache.GetAsync(CacheKeys.CharacterPresenceIndex(2, 7))
            .Returns(PresenceJson.Serialize(new CharacterPresenceIndex(2, WorldTwoInstanceId)));
    }

    [Fact]
    public async Task Answer_with_the_named_worlds_character_when_another_world_has_the_same_id()
    {
        ObservabilityService sut = CreateSut();
        GivenCharacterSevenInBothWorlds();

        PlayerPresenceDto? two = await sut.GetPlayerPresenceAsync(new WorldId(2), 7, Gm, CancellationToken.None);
        PlayerPresenceDto? one = await sut.GetPlayerPresenceAsync(new WorldId(1), 7, Gm, CancellationToken.None);

        Assert.Equal(("Zed", (ushort)2, WorldTwoInstanceId), (two!.Target.Name, two.Instance.WorldId, two.Instance.InstanceId));
        Assert.Equal(("Nym", (ushort)1, InstanceId), (one!.Target.Name, one.Instance.WorldId, one.Instance.InstanceId));
    }

    [Fact]
    public async Task Answer_nothing_when_a_worlds_index_entry_names_another_world()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Player);
        GivenWorldSnapshot(Snapshot("a", Char(7, "Nym")));
        _cache.GetAsync(CacheKeys.CharacterPresenceIndex(2, 7))
            .Returns(PresenceJson.Serialize(new CharacterPresenceIndex(1, InstanceId)));

        Assert.Null(await sut.GetPlayerPresenceAsync(new WorldId(2), 7, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Ignore_a_snapshot_stamped_with_another_world_than_its_key()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Player);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));
        // World 2's key holds a snapshot stamped world 1.
        _cache.GetAsync(CacheKeys.WorldPresence(2)).Returns(PresenceJson.Serialize(Snapshot("a", Char(7, "Zed"))));
        _cache.GetAsync(CacheKeys.CharacterPresenceIndex(2, 7))
            .Returns(PresenceJson.Serialize(new CharacterPresenceIndex(2, InstanceId)));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Equal(["Nym"], page.Items.Select(r => r.Name));
        Assert.Null(await sut.GetPlayerPresenceAsync(new WorldId(2), 7, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Answer_nothing_for_a_character_online_only_in_another_world()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Player);
        GivenWorldSnapshot(Snapshot("a", Char(7, "Nym")));
        GivenWorldTwoSnapshot(Char(9, "Zed"));
        GivenCharacterIndex(7, worldId: 1);

        Assert.Null(await sut.GetPlayerPresenceAsync(new WorldId(2), 7, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task List_both_worlds_characters_with_the_same_id_each_with_its_world()
    {
        ObservabilityService sut = CreateSut();
        GivenCharacterSevenInBothWorlds();

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Equal([("Nym", 7u, (ushort)1), ("Zed", 7u, (ushort)2)],
            page.Items.Select(r => (r.Name, r.CharacterId, r.WorldId)));
    }

    [Fact]
    public async Task Hide_presence_in_worlds_the_caller_may_not_enter()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Admin);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));
        GivenWorldTwoSnapshot(Char(2, "Zed"));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Equal(["Nym"], page.Items.Select(r => r.Name));
    }

    /// <summary>
    /// PTR (32) and Tournament (16) are numerically above Admin (4): a <c>&lt;=</c> comparison would
    /// let a game master holding either flag into an Admin-only world. The rule is a mask.
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.GameMaster | AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.GameMaster | AccountAccessLevel.Tournament)]
    public async Task Hide_an_admin_only_world_from_a_caller_whose_flags_are_numerically_above_admin(
        AccountAccessLevel caller)
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Admin);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));
        GivenWorldTwoSnapshot(Char(2, "Zed"));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), caller, CancellationToken.None);

        Assert.Equal(["Nym"], page.Items.Select(r => r.Name));
    }

    [Theory]
    [InlineData(AccountAccessLevel.GameMaster | AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.GameMaster | AccountAccessLevel.Tournament)]
    public async Task Hide_a_player_in_an_admin_only_world_from_a_caller_whose_flags_are_numerically_above_admin(
        AccountAccessLevel caller)
    {
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("a", Char(4417, "Nym")));
        ObservabilityService sut = CreateSut();
        _worlds.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AvalonWorld?>(new AvalonWorld
            {
                Id = new WorldId(1), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Admin,
            }));

        Assert.Null(await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, caller, CancellationToken.None));
    }

    [Theory]
    [InlineData(AccountAccessLevel.GameMaster | AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.GameMaster | AccountAccessLevel.Tournament)]
    public async Task Hide_an_instance_in_an_admin_only_world_from_a_caller_whose_flags_are_numerically_above_admin(
        AccountAccessLevel caller)
    {
        ObservabilityService sut = CreateSut();
        _worlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        [
            new AvalonWorld { Id = new WorldId(1), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Admin },
        ]);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));

        Assert.Null(await sut.GetInstancePresenceAsync(InstanceId, caller, CancellationToken.None));
    }

    [Fact]
    public async Task Show_a_ptr_world_to_a_game_master_holding_the_ptr_flag()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.PTR);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));
        GivenWorldTwoSnapshot(Char(2, "Zed"));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(
            new PresencePaginateFilters(), AccountAccessLevel.GameMaster | AccountAccessLevel.PTR, CancellationToken.None);

        Assert.Equal(["Nym", "Zed"], page.Items.Select(r => r.Name));
    }

    [Fact]
    public async Task Hide_a_players_presence_in_a_world_the_caller_may_not_enter()
    {
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("a", Char(4417, "Nym")));
        ObservabilityService sut = CreateSut();
        _worlds.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AvalonWorld?>(new AvalonWorld
            {
                Id = new WorldId(1), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Admin,
            }));

        Assert.Null(await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Hide_a_players_presence_in_a_world_with_no_row()
    {
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("a", Char(4417, "Nym")));
        ObservabilityService sut = CreateSut();
        _worlds.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AvalonWorld?>(null));

        Assert.Null(await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Hide_an_instance_in_a_world_the_caller_may_not_enter()
    {
        ObservabilityService sut = CreateSut();
        _worlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        [
            new AvalonWorld { Id = new WorldId(1), Name = "Aurora", AccessLevelRequired = AccountAccessLevel.Admin },
        ]);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));

        Assert.Null(await sut.GetInstancePresenceAsync(InstanceId, Gm, CancellationToken.None));
    }

    [Fact]
    public async Task Name_templates_from_each_worlds_own_database()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Player);
        IMapTemplateRepository worldTwoMaps = Substitute.For<IMapTemplateRepository>();
        worldTwoMaps.FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new MapTemplate { Id = new MapTemplateId(12), Name = "Glacier" });
        _perWorld.MapTemplates(Arg.Is<WorldId>(w => w.Value == 2)).Returns(worldTwoMaps);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));
        GivenWorldTwoSnapshot(Char(2, "Zed"));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Equal("Crypt", page.Items.Single(r => r.Name == "Nym").TemplateName);
        Assert.Equal("Glacier", page.Items.Single(r => r.Name == "Zed").TemplateName);
    }

    [Fact]
    public async Task Name_a_template_by_its_id_when_its_world_is_not_readable()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Player);
        _databases.MarkUnavailable(new WorldId(2));
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));
        GivenWorldTwoSnapshot(Char(2, "Zed"));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Equal("#12", page.Items.Single(r => r.Name == "Zed").TemplateName);
        _perWorld.DidNotReceive().MapTemplates(Arg.Is<WorldId>(w => w.Value == 2));
    }

    [Fact]
    public async Task Show_a_player_in_an_unavailable_world_without_reading_its_database()
    {
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("drifted", Char(4417, "Nym")));
        ObservabilityService sut = CreateSut();
        _databases.MarkUnavailable(new WorldId(1));

        PlayerPresenceDto? presence = await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);

        Assert.NotNull(presence);
        Assert.Equal("#12", presence.Instance.TemplateName);
        Assert.False(presence.LayoutStale);
        _perWorld.DidNotReceive().MapTemplates(Arg.Any<WorldId>());
        _perWorld.DidNotReceive().ProceduralMapConfigs(Arg.Any<WorldId>());
        _perWorld.DidNotReceive().LayoutInputs(Arg.Any<WorldId>());
    }

    [Fact]
    public async Task Name_a_template_by_its_id_when_its_worlds_lookup_fails_and_list_the_rest()
    {
        ObservabilityService sut = CreateSut();
        GivenTwoWorlds(AccountAccessLevel.Player);
        IMapTemplateRepository worldTwoMaps = Substitute.For<IMapTemplateRepository>();
        worldTwoMaps.FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Failed to connect to Host=secret;Port=5433"));
        _perWorld.MapTemplates(Arg.Is<WorldId>(w => w.Value == 2)).Returns(worldTwoMaps);
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));
        GivenWorldTwoSnapshot(Char(2, "Zed"));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Equal("Crypt", page.Items.Single(r => r.Name == "Nym").TemplateName);
        Assert.Equal("#12", page.Items.Single(r => r.Name == "Zed").TemplateName);
        (LogLevel level, string text) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Log_a_failed_layout_check_by_exception_type_only()
    {
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("drifted", Char(4417, "Nym")));
        ObservabilityService sut = CreateSut();
        _configs.FindByTemplateIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Failed to connect to Host=secret;Port=5433"));

        PlayerPresenceDto? presence = await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);

        Assert.NotNull(presence);
        Assert.False(presence.LayoutStale);
        (LogLevel level, string text) = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Let_a_cancelled_layout_check_cancel_the_read_and_log_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        GivenCharacterIndex(4417);
        GivenWorldSnapshot(Snapshot("drifted", Char(4417, "Nym")));
        ObservabilityService sut = CreateSut();
        _configs.FindByTemplateIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, cancelled.Token));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Name_a_template_by_its_id_in_a_world_this_api_is_not_configured_for()
    {
        ObservabilityService sut = CreateSut();
        _worlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(
        [
            new AvalonWorld { Id = new WorldId(3), Name = "Cinder", AccessLevelRequired = AccountAccessLevel.Player },
        ]);
        _cache.GetAsync(CacheKeys.WorldPresence(3)).Returns(PresenceJson.Serialize(new WorldPresenceSnapshot(
            WorldId: 3, CapturedAt: DateTime.UtcNow,
            Instances: [new InstancePresenceSnapshot(InstanceId, TemplateId: 12, Seed: 3, MapType: "Normal",
                ConfigVersion: "", OwnerCharacterId: null, Characters: [Char(7, "Ash")])])));

        PagedResult<OnlinePlayerDto> page = await sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, CancellationToken.None);

        Assert.Equal("#12", Assert.Single(page.Items).TemplateName);
        _perWorld.DidNotReceive().MapTemplates(Arg.Is<WorldId>(w => w.Value == 3));
    }

    [Fact]
    public async Task Let_a_cancelled_template_lookup_cancel_the_list()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        ObservabilityService sut = CreateSut();
        _maps.FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        GivenWorldSnapshot(Snapshot("a", Char(1, "Nym")));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.GetOnlineAsync(new PresencePaginateFilters(), Gm, cancelled.Token));
    }

    [Fact]
    public async Task Cache_pool_members_per_world()
    {
        string current = GivenGeneratorInputs();
        IProceduralLayoutInputsResolver worldTwoInputs = Substitute.For<IProceduralLayoutInputsResolver>();
        worldTwoInputs.FindPoolAsync(Arg.Any<ChunkPoolId>(), Arg.Any<CancellationToken>())
            .Returns(new ChunkPool { Id = new ChunkPoolId(3), Memberships = [] });
        worldTwoInputs.ResolveMembersAsync(Arg.Any<ChunkPool>(), Arg.Any<CancellationToken>())
            .Returns(new ProceduralPoolResolution([], new Dictionary<ChunkTemplateId, ChunkTemplate>()));
        GivenCharacterIndex(4417, worldId: 1);
        GivenCharacterIndex(5001, worldId: 2);
        GivenWorldSnapshot(Snapshot(current, Char(4417, "Nym")));
        _cache.GetAsync(CacheKeys.WorldPresence(2)).Returns(PresenceJson.Serialize(new WorldPresenceSnapshot(
            WorldId: 2, CapturedAt: DateTime.UtcNow,
            Instances: [new InstancePresenceSnapshot(InstanceId, TemplateId: 12, Seed: 3, MapType: "Normal",
                ConfigVersion: current, OwnerCharacterId: null, Characters: [Char(5001, "Zed")])])));
        ObservabilityService sut = CreateSut();
        _perWorld.LayoutInputs(Arg.Is<WorldId>(w => w.Value == 2)).Returns(worldTwoInputs);

        await sut.GetPlayerPresenceAsync(new WorldId(1), 4417, Gm, CancellationToken.None);
        await sut.GetPlayerPresenceAsync(new WorldId(2), 5001, Gm, CancellationToken.None);

        // World 2 resolves its own pool 3; world 1's cached pool 3 is not served to it.
        await worldTwoInputs.Received(1).FindPoolAsync(Arg.Any<ChunkPoolId>(), Arg.Any<CancellationToken>());
        await _inputsResolver.Received(1).FindPoolAsync(Arg.Any<ChunkPoolId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Every entry as it would be written: its level, the formatted message and any exception attached.</summary>
    private sealed class CapturingLogger : ILogger<ObservabilityService>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " " + exception)));
    }
}
