using System.Net.Http.Json;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Testing;
using Avalon.Api.Worlds.Controllers;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using ContractStatus = Avalon.Api.Contract.WorldStatus;
using ContractType = Avalon.Api.Contract.WorldType;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Worlds.UnitTests.Worlds;

/// <summary>GET /public/world: the worlds a caller may read from, the default one, and each world's live state.</summary>
public sealed class PublicWorldListShould
{
    private static readonly DateTime s_now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly record struct AsthoriaRow(int? Limit = null, string? Lore = null, bool Maintenance = false, DateTime? Deadline = null);

    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private readonly IWorldReadiness _readiness = Substitute.For<IWorldReadiness>();
    private readonly IWorldOccupancy _occupancy = Substitute.For<IWorldOccupancy>();

    private void Live(ushort id, bool ready, int taken, int queue)
    {
        _readiness.IsReadyAsync(id, Arg.Any<CancellationToken>()).Returns(ready);
        _occupancy.ReadAsync(id, Arg.Any<CancellationToken>()).Returns(new WorldOccupancy(taken, queue));
    }

    private async Task<ApiTestHost> Start(ushort? defaultWorld, PublicWorldSettings? settings = null, AsthoriaRow asthoria = default)
    {
        WorldDatabases databases = new(new ushort[] { 1, 2, 3 }
            .Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}")));
        Row(1, "Development", AccountAccessLevel.Admin);
        Row(2, "Asthoria", AccountAccessLevel.Player, asthoria.Limit, asthoria.Lore, asthoria.Maintenance, asthoria.Deadline);
        Row(3, "Public Test Realm", AccountAccessLevel.PTR);

        return await ApiTestHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(databases);
            services.AddSingleton(_authWorlds);
            services.AddSingleton(settings ?? new PublicWorldSettings(defaultWorld));
            services.AddSingleton(_readiness);
            services.AddSingleton(_occupancy);
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(s_now)));
        });
    }

    private void Row(ushort id, string name, AccountAccessLevel required, int? limit = null, string? lore = null,
        bool maintenanceEnabled = false, DateTime? deadline = null) =>
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntity
            {
                Id = new WorldId(id),
                Name = name,
                AccessLevelRequired = required,
                Host = "h",
                MinVersion = "0.0.1",
                Version = "0.0.1",
                PlayerLimit = limit,
                Lore = lore,
                MaintenanceEnabled = maintenanceEnabled,
                MaintenanceDeadlineUtc = deadline,
            });

    private static async Task<PublicWorldDto> OnlyWorld(ApiTestHost host) =>
        (await host.Client.GetFromJsonAsync<PublicWorldsDto>("/public/world"))!.Worlds.Single();

    [Fact]
    public async Task List_only_the_worlds_every_player_may_enter_for_an_anonymous_caller()
    {
        await using ApiTestHost host = await Start(defaultWorld: 2);

        PublicWorldsDto dto = (await host.Client.GetFromJsonAsync<PublicWorldsDto>("/public/world"))!;

        Assert.Equal((ushort?)2, dto.DefaultWorldId);
        Assert.Equal(["Asthoria"], dto.Worlds.Select(w => w.Name));
    }

    [Fact]
    public async Task Never_be_shared_by_a_cache()
    {
        await using ApiTestHost host = await Start(defaultWorld: 2);

        HttpResponseMessage response = await host.Client.GetAsync("/public/world");

        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoCache);
    }

    [Fact]
    public async Task List_every_world_a_signed_in_caller_may_enter()
    {
        await using ApiTestHost host = await Start(defaultWorld: 2);
        Account admin = ApiTestHost.MakeAccount(AccountAccessLevel.Admin);
        host.AccountNowIs(admin);

        HttpResponseMessage response = await host.GetAsync("/public/world", ApiTestHost.Mint(admin));
        PublicWorldsDto dto = (await response.Content.ReadFromJsonAsync<PublicWorldsDto>())!;

        // AccessLevels.ForWorld(PTR) is PTR | GameMaster, and the GameMaster mask includes Admin.
        Assert.Equal(["Development", "Asthoria", "Public Test Realm"], dto.Worlds.Select(w => w.Name));
        Assert.Equal((ushort?)2, dto.DefaultWorldId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((ushort)1)]
    public async Task Fall_back_to_the_first_readable_world(ushort? configured)
    {
        await using ApiTestHost host = await Start(defaultWorld: configured);

        PublicWorldsDto dto = (await host.Client.GetFromJsonAsync<PublicWorldsDto>("/public/world"))!;

        Assert.Equal((ushort?)2, dto.DefaultWorldId);
    }

    [Fact]
    public async Task Carry_each_worlds_live_state()
    {
        Live(2, ready: true, taken: 30, queue: 0);
        await using ApiTestHost host = await Start(2, asthoria: new AsthoriaRow(Limit: 90, Lore: "The last lit kingdom."));

        PublicWorldDto world = await OnlyWorld(host);

        Assert.Equal(ContractStatus.Online, world.Status);
        Assert.Equal(PublicPopulation.Medium, world.Population);
        Assert.Equal(0, world.QueueLength);
        Assert.Null(world.MaintenanceDeadlineUtc);
        Assert.Equal("The last lit kingdom.", world.Lore);
        Assert.Equal(ContractType.PvE, world.Type);
    }

    [Fact]
    public async Task Report_a_full_world_and_its_queue()
    {
        Live(2, ready: true, taken: 90, queue: 34);
        await using ApiTestHost host = await Start(2, asthoria: new AsthoriaRow(Limit: 90));

        PublicWorldDto world = await OnlyWorld(host);

        Assert.Equal(ContractStatus.Full, world.Status);
        Assert.Equal(PublicPopulation.Full, world.Population);
        Assert.Equal(34, world.QueueLength);
    }

    [Fact]
    public async Task Carry_a_scheduled_maintenance_deadline()
    {
        Live(2, ready: true, taken: 10, queue: 0);
        await using ApiTestHost host = await Start(2,
            asthoria: new AsthoriaRow(Limit: 90, Maintenance: true, Deadline: s_now.AddMinutes(12)));

        PublicWorldDto world = await OnlyWorld(host);

        Assert.Equal(ContractStatus.Online, world.Status);
        Assert.Equal(s_now.AddMinutes(12), world.MaintenanceDeadlineUtc);
    }

    [Fact]
    public async Task Hide_a_stale_deadline_when_maintenance_is_off()
    {
        Live(2, ready: true, taken: 10, queue: 0);
        await using ApiTestHost host = await Start(2,
            asthoria: new AsthoriaRow(Limit: 90, Maintenance: false, Deadline: s_now.AddMinutes(12)));

        Assert.Null((await OnlyWorld(host)).MaintenanceDeadlineUtc);
    }

    [Fact]
    public async Task Show_no_population_without_a_limit_or_reference()
    {
        Live(2, ready: true, taken: 30, queue: 0);
        await using (ApiTestHost host = await Start(2))
            Assert.Null((await OnlyWorld(host)).Population);

        await using ApiTestHost withReference = await Start(2, new PublicWorldSettings(2, PopulationReference: 90));
        Assert.Equal(PublicPopulation.Medium, (await OnlyWorld(withReference)).Population);
    }

    [Fact]
    public async Task Read_everything_as_offline_when_redis_fails()
    {
        _readiness.IsReadyAsync(2, Arg.Any<CancellationToken>()).Returns(false);
        _occupancy.ReadAsync(2, Arg.Any<CancellationToken>()).Returns(default(WorldOccupancy));
        await using ApiTestHost host = await Start(2, asthoria: new AsthoriaRow(Limit: 90));

        HttpResponseMessage response = await host.Client.GetAsync("/public/world");
        PublicWorldDto world = (await response.Content.ReadFromJsonAsync<PublicWorldsDto>())!.Worlds.Single();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ContractStatus.Offline, world.Status);
        Assert.Null(world.Population);
        Assert.Equal(0, world.QueueLength);
    }

    [Fact]
    public async Task Never_expose_infrastructure_or_exact_counts()
    {
        Live(2, ready: true, taken: 30, queue: 3);
        await using ApiTestHost host = await Start(2,
            asthoria: new AsthoriaRow(Limit: 90, Lore: "x", Maintenance: true, Deadline: s_now.AddMinutes(12)));

        string json = await host.Client.GetStringAsync("/public/world");

        // The API omits null-valued properties, so a key may be absent; it may never be anything else.
        string[] allowed = ["id", "name", "type", "status", "population", "queueLength", "maintenanceDeadlineUtc", "lore"];
        string[] forbidden = ["host", "port", "minVersion", "version", "onlineCount", "playerLimit", "maintenanceRevision"];
        using var doc = JsonDocument.Parse(json);
        JsonElement[] worlds = doc.RootElement.GetProperty("worlds").EnumerateArray().ToArray();
        Assert.NotEmpty(worlds);
        // The fixture makes all eight fields non-null, so the key set is exactly the contract.
        Assert.Equal(allowed.Order(), worlds.Single().EnumerateObject().Select(p => p.Name).Order());
        foreach (JsonElement world in worlds)
        {
            foreach (JsonProperty property in world.EnumerateObject())
            {
                Assert.Contains(property.Name, allowed);
                Assert.DoesNotContain(forbidden, f => string.Equals(f, property.Name, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Theory]
    [InlineData(ContractStatus.Online, true, 10, false, false)]
    [InlineData(ContractStatus.Full, true, 90, false, false)]
    [InlineData(ContractStatus.Maintenance, true, 10, true, true)]
    [InlineData(ContractStatus.Offline, false, 0, false, false)]
    public async Task Report_the_same_status_as_the_admin_world_route(ContractStatus expected, bool ready, int taken,
        bool maintenance, bool pastCutoff)
    {
        Live(2, ready, taken, queue: 0);
        DateTime? deadline = maintenance ? (pastCutoff ? s_now.AddMinutes(-1) : s_now.AddMinutes(12)) : null;
        await using ApiTestHost host = await Start(2, asthoria: new AsthoriaRow(Limit: 90, Maintenance: maintenance, Deadline: deadline));
        Account player = ApiTestHost.MakeAccount(AccountAccessLevel.Player);
        host.AccountNowIs(player);
        string token = ApiTestHost.Mint(player);

        HttpResponseMessage admin = await host.GetAsync("/world/2", token);
        HttpResponseMessage publicList = await host.GetAsync("/public/world", token);
        ContractStatus adminStatus = (await admin.Content.ReadFromJsonAsync<WorldDto>())!.Status;
        ContractStatus publicStatus = (await publicList.Content.ReadFromJsonAsync<PublicWorldsDto>())!.Worlds.Single().Status;

        Assert.Equal(adminStatus, publicStatus);
        Assert.Equal(expected, publicStatus);
    }

    [Fact]
    public async Task Read_no_live_state_for_a_world_the_caller_may_not_read()
    {
        Live(2, ready: true, taken: 30, queue: 0);
        await using ApiTestHost host = await Start(2, asthoria: new AsthoriaRow(Limit: 90));

        HttpResponseMessage response = await host.Client.GetAsync("/public/world");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        await _readiness.Received(1).IsReadyAsync(2, Arg.Any<CancellationToken>());
        await _readiness.Received(0).IsReadyAsync(3, Arg.Any<CancellationToken>());
        await _readiness.Received(0).IsReadyAsync(1, Arg.Any<CancellationToken>());
        await _occupancy.Received(0).ReadAsync(3, Arg.Any<CancellationToken>());
        await _occupancy.Received(0).ReadAsync(1, Arg.Any<CancellationToken>());
    }
}
