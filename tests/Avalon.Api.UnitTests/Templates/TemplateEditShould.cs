using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Templates;
using Avalon.Api.Testing;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World;
using Avalon.Database.World.Extensions;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Scripts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using StackExchange.Redis;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Templates;

/// <summary>
/// The template PUT endpoints through the real pipeline (authentication, the world check, the Admin policy, the
/// editable-world guard) and the real repositories and service over a seeded SQLite world.
/// </summary>
public sealed class TemplateEditShould : IAsyncLifetime
{
    private const ushort Editable = 1;
    private const ushort ReadOnly = 2; // configured, but with no database behind it: touching it would throw

    private readonly SqliteWorlds _sqlite = new(Editable);
    private readonly CountingWorlds _worlds;
    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private readonly RecordingSignal _signal = new();
    private readonly FakeCatalog _catalog = new();
    private readonly CapturingLogs _logs = new();
    private ApiTestHost _host = null!;

    public TemplateEditShould() => _worlds = new CountingWorlds(_sqlite);

    public static TheoryData<string> Kinds => new() { "item", "ability", "creature", "aura" };

    /// <summary>A helpful aura every test world holds, whatever the seed has: the one a Hostile ability cannot apply.</summary>
    private const uint TestWard = 990;

    public async Task InitializeAsync()
    {
        Row(Editable);
        Row(ReadOnly);
        using (WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable)))
        {
            db.AuraTemplates.Add(new Avalon.Domain.World.AuraTemplate
            {
                Id = new Avalon.Common.ValueObjects.AuraId(TestWard),
                Name = "Test Ward",
                Icon = "ward",
                Kind = Avalon.Domain.World.AuraKind.Helpful,
                DurationMs = 10000,
                Stacking = Avalon.Domain.World.AuraStacking.Refresh,
                MaxStacks = 1,
                Modifiers =
                [
                    new Avalon.Domain.World.AuraStatModifier
                    {
                        AuraId = new Avalon.Common.ValueObjects.AuraId(TestWard), Stat = Avalon.Domain.World.AuraStat.Armor,
                        Kind = Avalon.Domain.World.AuraModifierKind.Percent, Value = 10f,
                    },
                ],
            });
            db.SaveChanges();
        }

        _host = await ApiTestHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(new WorldDatabases(
            [
                new ConfiguredWorld(new WorldId(Editable), "Host=w1", "Host=c1"),
                new ConfiguredWorld(new WorldId(ReadOnly), "Host=w2", "Host=c2"),
            ]));
            services.AddSingleton<IWorldDbContextFactory>(_worlds);
            services.AddWorldRepositories();
            services.AddSingleton(_authWorlds);
            services.AddOptions<TemplateEditingOptions>().Configure(o =>
            {
                o.EditableWorlds = [Editable];
                o.ReloadTimeout = TimeSpan.FromSeconds(10);
            });
            services.AddSingleton<ITemplateReloadSignal>(_signal);
            services.AddSingleton<IWorldScriptCatalog>(_catalog);
            services.AddTemplateEditing();
            services.AddLogging(b => b.AddProvider(_logs));
        });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _sqlite.Dispose();
    }

    private void Row(ushort id) =>
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntity
            {
                Id = new WorldId(id),
                Name = $"World{id}",
                AccessLevelRequired = AccountAccessLevel.Player,
                Host = "h",
                MinVersion = "0.0.1",
                Version = "0.0.1",
            });

    private string Token(AccountAccessLevel level)
    {
        Account account = ApiTestHost.MakeAccount(level);
        _host.AccountNowIs(account);
        return ApiTestHost.Mint(account);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, AccountAccessLevel level,
        string? body = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(level));
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _host.Client.SendAsync(request);
    }

    /// <summary>The id of the first seeded template of a kind.</summary>
    private ulong FirstId(string kind)
    {
        using WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable));
        return kind switch
        {
            "item" => db.ItemTemplates.AsEnumerable().Min(t => t.Id.Value),
            "ability" => db.AbilityTemplates.AsEnumerable().Min(t => (ulong)t.Id.Value),
            "aura" => db.AuraTemplates.AsEnumerable().Min(t => (ulong)t.Id.Value),
            _ => db.CreatureTemplates.AsEnumerable().Min(t => t.Id.Value),
        };
    }

    private static string Path(ushort world, string kind, ulong id) => $"/world/{world}/{kind}-template/{id}";

    /// <summary>A template as the read endpoint gives it: its JSON, which an edit sends back changed.</summary>
    private async Task<(JsonObject Json, string Version)> ReadAsync(string kind, ulong id)
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, Path(Editable, kind, id), AccountAccessLevel.Admin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        return (json, json["version"]!.GetValue<string>());
    }

    private Task<HttpResponseMessage> PutAsync(ushort world, string kind, ulong id, JsonObject body, string? ifMatch,
        AccountAccessLevel level = AccountAccessLevel.Admin) =>
        SendAsync(HttpMethod.Put, Path(world, kind, id), level, body.ToJsonString(), ifMatch);

    private static string Tag(string version) => $"\"{version}\"";

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Save_a_valid_edit_and_return_the_stored_template_with_a_new_version(string kind)
    {
        ulong id = FirstId(kind);
        (JsonObject json, string version) = await ReadAsync(kind, id);
        json["name"] = "Renamed by an admin";

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject result = await BodyAsync(response);
        Assert.Equal("Renamed by an admin", result["template"]!["name"]!.GetValue<string>());
        string newVersion = result["template"]!["version"]!.GetValue<string>();
        Assert.NotEqual(version, newVersion);
        Assert.Equal("applied", result["reload"]!["status"]!.GetValue<string>());
        Assert.Equal("reloaded", result["reload"]!["summary"]!.GetValue<string>());

        (JsonObject after, string readBack) = await ReadAsync(kind, id);
        Assert.Equal("Renamed by an admin", after["name"]!.GetValue<string>());
        Assert.Equal(newVersion, readBack);
        (WorldId world, TemplateReloadArea area) = Assert.Single(_signal.Requests);
        Assert.Equal(Editable, world.Value);
        Assert.Equal(kind switch
        {
            "item" => TemplateReloadArea.Items,
            "ability" => TemplateReloadArea.Abilities,
            "aura" => TemplateReloadArea.Auras,
            _ => TemplateReloadArea.Creatures,
        }, area);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Refuse_a_game_master(string kind)
    {
        ulong id = FirstId(kind);
        (JsonObject json, string version) = await ReadAsync(kind, id);
        json["name"] = "By a game master";

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version), AccountAccessLevel.GameMaster);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(version, (await ReadAsync(kind, id)).Version);
        Assert.Empty(_signal.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Refuse_an_admin_on_a_world_that_is_not_editable_without_touching_the_database(string kind)
    {
        (JsonObject json, string version) = await ReadAsync(kind, FirstId(kind));
        json["name"] = "Not here";
        int opened = _worlds.OpenedFor(ReadOnly);

        HttpResponseMessage response = await PutAsync(ReadOnly, kind, 1, json, Tag(version));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("This world is not editable", (await BodyAsync(response))["title"]!.GetValue<string>());
        // The world has no database behind it, so any read or write would have thrown (a 500); the counter says the
        // same of the context factory.
        Assert.Equal(opened, _worlds.OpenedFor(ReadOnly));
        Assert.Equal(0, _worlds.OpenedFor(ReadOnly));
        Assert.Empty(_signal.Requests);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Decide_the_world_before_the_version_and_before_the_body(string kind)
    {
        // No If-Match and a body that is not JSON: still 403, not 428 or 400.
        HttpResponseMessage response = await SendAsync(HttpMethod.Put, Path(ReadOnly, kind, 1), AccountAccessLevel.Admin,
            "{ this is not json");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _worlds.OpenedFor(ReadOnly));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Answer_404_for_an_unknown_id(string kind)
    {
        (JsonObject json, string version) = await ReadAsync(kind, FirstId(kind));

        HttpResponseMessage response = await PutAsync(Editable, kind, 99999, json, Tag(version));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Answer_428_without_if_match(string kind)
    {
        ulong id = FirstId(kind);
        (JsonObject json, string version) = await ReadAsync(kind, id);
        json["name"] = "No tag";

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.Equal(version, (await ReadAsync(kind, id)).Version);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Answer_409_to_a_stale_version_and_overwrite_nothing(string kind)
    {
        ulong id = FirstId(kind);
        (JsonObject json, string version) = await ReadAsync(kind, id);

        // Two admins open the same template; the first saves.
        var first = (JsonObject)json.DeepClone();
        first["name"] = "First admin";
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(Editable, kind, id, first, Tag(version))).StatusCode);

        var second = (JsonObject)json.DeepClone();
        second["name"] = "Second admin";
        HttpResponseMessage response = await PutAsync(Editable, kind, id, second, Tag(version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Changed since you opened it", (await BodyAsync(response))["title"]!.GetValue<string>());
        Assert.Equal("First admin", (await ReadAsync(kind, id)).Json["name"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Accept_every_seeded_row_unchanged(string kind)
    {
        // The rules mirror the world's loaders, so a row the world loads must pass them as it stands.
        List<ulong> ids;
        using (WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable)))
        {
            ids = kind switch
            {
                "item" => db.ItemTemplates.AsEnumerable().Select(t => t.Id.Value).ToList(),
                "ability" => db.AbilityTemplates.AsEnumerable().Select(t => (ulong)t.Id.Value).ToList(),
                "aura" => db.AuraTemplates.AsEnumerable().Select(t => (ulong)t.Id.Value).ToList(),
                _ => db.CreatureTemplates.AsEnumerable().Select(t => t.Id.Value).ToList(),
            };
        }

        Assert.NotEmpty(ids);
        List<string> refused = [];
        foreach (ulong id in ids)
        {
            (JsonObject json, string version) = await ReadAsync(kind, id);
            HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version));
            if (response.StatusCode != HttpStatusCode.OK)
                refused.Add($"{kind} {id}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        Assert.Empty(refused);
        // Nothing changed, so nothing was audited.
        Assert.DoesNotContain(_logs.Entries, e => e.Message.StartsWith("Template saved", StringComparison.Ordinal));
    }

    public static TheoryData<string, string, object?, string> Refusals => new()
    {
        { "item", "name", "  ", "name" },
        { "item", "class", 999, "class" },
        { "item", "flags", 1 << 20, "flags" },
        { "item", "maxStackSize", 0, "maxStackSize" },
        { "item", "statType1", null, "statType1" },        // the seeded first stat keeps its value but loses its type
        { "item", "damageMax1", 0, "damageMax1" },          // a seeded weapon gets a maximum below its minimum
        { "ability", "name", "", "name" },
        { "ability", "costPowerType", "None", "costPowerType" }, // paired with a cost above 0, set below
        { "ability", "scriptName", "NoSuchScript", "scriptName" },
        { "creature", "baseAttackTime", 0.1, "baseAttackTime" },
        { "creature", "maxLevel", -5, "maxLevel" },
        { "creature", "speedRun", -1, "speedRun" },
        { "creature", "lootTableId", 987654, "lootTableId" },
        { "aura", "icon", " ", "icon" },
        { "aura", "maxStacks", 0, "maxStacks" },
        { "aura", "durationMs", 0, "durationMs" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Answer_400_with_the_field_key_and_write_nothing(string kind, string field, object? value, string key)
    {
        ulong id = await RefusalTargetAsync(kind, field);
        (JsonObject json, string version) = await ReadAsync(kind, id);
        json[field] = value is null ? null : JsonSerializer.SerializeToNode(value);
        if (kind == "ability" && field == "costPowerType") json["cost"] = 10;
        if (kind == "item" && field == "damageMax1") json["damageMin1"] = 5;

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonObject problem = await BodyAsync(response);
        Assert.Contains(key, problem["errors"]!.AsObject().Select(e => e.Key));
        Assert.Equal(version, (await ReadAsync(kind, id)).Version);
        Assert.Empty(_signal.Requests);
    }

    /// <summary>A seeded row the refusal applies to: one with a first stat, a weapon, a circle ability.</summary>
    private async Task<ulong> RefusalTargetAsync(string kind, string field)
    {
        await Task.CompletedTask;
        using WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable));
        return (kind, field) switch
        {
            ("item", "statType1") => db.ItemTemplates.AsEnumerable().First(t => t.StatType1 is not null && t.StatValue1 is not null).Id.Value,
            ("item", "damageMax1") => db.ItemTemplates.AsEnumerable().First(t => t.DamageMin1 is not null && t.DamageMax1 is not null).Id.Value,
            ("ability", "scriptName") => 200UL,
            ("ability", _) => 200UL,
            _ => FirstId(kind),
        };
    }

    // A name no world has: whatever is not in the published catalog.
    private const string Unlisted = "ScriptNotInTheCatalog";

    private static string UnknownMessage(string name) => $"Unknown script '{name}' on this world";

    private async Task<(JsonObject Json, string Version, ulong Id)> ReadFirstAsync(string kind)
    {
        ulong id = kind == "ability" ? 200UL : FirstId(kind);
        (JsonObject json, string version) = await ReadAsync(kind, id);
        return (json, version, id);
    }

    private static string[] ScriptErrors(JsonObject problem) =>
        problem["errors"]!.AsObject()["scriptName"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray() ?? [];

    [Theory]
    [InlineData("creature")]
    [InlineData("ability")]
    public async Task Refuse_a_script_the_published_catalog_does_not_list(string kind)
    {
        (JsonObject json, string version, ulong id) = await ReadFirstAsync(kind);
        string current = json["scriptName"]!.GetValue<string>();
        _catalog.Snapshot = new ScriptCatalogSnapshot([current], [current], []);
        json["scriptName"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(UnknownMessage(Unlisted), ScriptErrors(await BodyAsync(response)));
        Assert.Equal(version, (await ReadAsync(kind, id)).Version);
        Assert.Empty(_signal.Requests);
    }

    [Theory]
    [InlineData("creature")]
    [InlineData("ability")]
    public async Task Accept_a_script_the_published_catalog_lists(string kind)
    {
        (JsonObject json, string version, ulong id) = await ReadFirstAsync(kind);
        string current = json["scriptName"]!.GetValue<string>();
        _catalog.Snapshot = new ScriptCatalogSnapshot([current], [current], []);
        json["name"] = "Renamed with a listed script";

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Check_a_creature_against_the_ai_names_and_an_ability_against_the_ability_names()
    {
        (JsonObject creature, string creatureVersion, ulong creatureId) = await ReadFirstAsync("creature");
        (JsonObject ability, string abilityVersion, ulong abilityId) = await ReadFirstAsync("ability");
        string creatureScript = creature["scriptName"]!.GetValue<string>();
        string abilityScript = ability["scriptName"]!.GetValue<string>();
        // Each script is listed, but only under the other kind, and each save moves to the other's script.
        _catalog.Snapshot = new ScriptCatalogSnapshot([creatureScript], [abilityScript], []);
        creature["scriptName"] = abilityScript;
        ability["scriptName"] = creatureScript;

        HttpResponseMessage creatureResponse = await PutAsync(Editable, "creature", creatureId, creature, Tag(creatureVersion));
        HttpResponseMessage abilityResponse = await PutAsync(Editable, "ability", abilityId, ability, Tag(abilityVersion));

        Assert.Equal(HttpStatusCode.BadRequest, creatureResponse.StatusCode);
        Assert.Contains(UnknownMessage(abilityScript), ScriptErrors(await BodyAsync(creatureResponse)));
        Assert.Equal(HttpStatusCode.BadRequest, abilityResponse.StatusCode);
        Assert.Contains(UnknownMessage(creatureScript), ScriptErrors(await BodyAsync(abilityResponse)));
    }

    [Fact]
    public async Task Read_and_save_a_creature_in_a_world_that_still_has_the_dropped_columns()
    {
        // #745: a world server that has not migrated yet (Asthoria, on 0.11.0) keeps AIName and RespawnTimerSecs.
        // EF ignores columns it does not map, so the api reads and updates the row as usual.
        using (WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable)))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE \"CreatureTemplates\" ADD COLUMN \"AIName\" TEXT NOT NULL DEFAULT ''");
            db.Database.ExecuteSqlRaw("ALTER TABLE \"CreatureTemplates\" ADD COLUMN \"RespawnTimerSecs\" INTEGER NOT NULL DEFAULT 180");
        }

        (JsonObject json, string version, ulong id) = await ReadFirstAsync("creature");
        Assert.False(json.ContainsKey("aiName"));
        Assert.False(json.ContainsKey("respawnTimerSecs"));
        json["name"] = "Renamed on a world with the old columns";

        HttpResponseMessage response = await PutAsync(Editable, "creature", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using WorldDbContext check = _sqlite.CreateWorld(new WorldId(Editable));
        Assert.Equal("Renamed on a world with the old columns",
            check.CreatureTemplates.AsEnumerable().Single(t => t.Id.Value == id).Name);
    }

    [Theory]
    [InlineData("creature")]
    [InlineData("ability")]
    public async Task Let_a_row_with_an_unlisted_script_be_saved_while_its_script_name_is_unchanged(string kind)
    {
        // The catalog does not list the row's own script, but the save does not touch it.
        (JsonObject json, string version, ulong id) = await ReadFirstAsync(kind);
        _catalog.Snapshot = new ScriptCatalogSnapshot([], [], []);
        json["name"] = "Renamed, script untouched";

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Allow_a_creature_with_no_script_name_whatever_the_catalog_lists()
    {
        (JsonObject json, string version, ulong id) = await ReadFirstAsync("creature");
        _catalog.Snapshot = new ScriptCatalogSnapshot([], [], []);
        json["scriptName"] = "";

        HttpResponseMessage response = await PutAsync(Editable, "creature", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Not_call_an_empty_ability_script_name_unknown()
    {
        // An ability must name a script (a rule of its own), but the catalog does not add a second complaint.
        (JsonObject json, string version, ulong id) = await ReadFirstAsync("ability");
        _catalog.Snapshot = new ScriptCatalogSnapshot([], [], []);
        json["scriptName"] = "";

        HttpResponseMessage response = await PutAsync(Editable, "ability", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(ScriptErrors(await BodyAsync(response)), m => m.StartsWith("Unknown script", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skip_the_script_check_for_a_creature_while_no_catalog_is_published()
    {
        (JsonObject json, string version, ulong id) = await ReadFirstAsync("creature");
        _catalog.Snapshot = null;
        json["scriptName"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, "creature", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Skip_the_script_check_for_an_ability_while_no_catalog_is_published()
    {
        // The ability's own rules still run: the row only has to get past the catalog, so the message is the test.
        (JsonObject json, string version, ulong id) = await ReadFirstAsync("ability");
        _catalog.Snapshot = null;
        json["scriptName"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, "ability", id, json, Tag(version));

        Assert.DoesNotContain(ScriptErrors(await BodyAsync(response)), m => m.StartsWith("Unknown script", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("creature")]
    [InlineData("ability")]
    public async Task Save_a_changed_script_name_when_redis_cannot_be_read(string kind)
    {
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        cache.GetAsync(Arg.Any<string>())
            .Returns(Task.FromException<string?>(new RedisServerException(RedisErrorKind.Unknown, CommandFlags.CommandRetryNever, "WRONGTYPE")));
        _catalog.Real = new WorldScriptCatalog(cache, new LoggerOf<WorldScriptCatalog>(_logs.CreateLogger("test")));
        (JsonObject json, string version, ulong id) = await ReadFirstAsync(kind);
        json["scriptName"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, kind, id, json, Tag(version));

        // The catalog check is skipped, so the save reaches the template's own rules: no "Unknown script" 400.
        if (response.StatusCode != HttpStatusCode.OK)
            Assert.DoesNotContain(ScriptErrors(await BodyAsync(response)), m => m.StartsWith("Unknown script", StringComparison.Ordinal));
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Ask_the_catalog_of_the_world_being_edited()
    {
        (JsonObject json, string version, ulong id) = await ReadFirstAsync("creature");

        await PutAsync(Editable, "creature", id, json, Tag(version));

        Assert.Equal([new WorldId(Editable)], _catalog.Asked);
    }

    private static string[] UseScriptErrors(JsonObject problem) =>
        problem["errors"]!.AsObject()["useScript"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray() ?? [];

    [Fact]
    public async Task Save_an_items_use_fields()
    {
        ulong id = FirstId("item");
        (JsonObject json, string version) = await ReadAsync("item", id);
        json["useCooldownMs"] = 1000;
        json["useCooldownGroup"] = "elixir";

        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (JsonObject saved, string savedVersion) = await ReadAsync("item", id);
        Assert.Equal((1000u, "elixir"), (saved["useCooldownMs"]!.GetValue<uint>(), saved["useCooldownGroup"]!.GetValue<string>()));
        Assert.NotEqual(version, savedVersion);
    }

    /// <summary>A blank use script or cooldown group is no name: it is stored as null.</summary>
    [Fact]
    public async Task Store_a_blank_use_script_and_cooldown_group_as_null()
    {
        ulong id = FirstId("item");
        (JsonObject json, string version) = await ReadAsync("item", id);
        json["useScript"] = "   ";
        json["useCooldownGroup"] = " ";

        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject saved = (await ReadAsync("item", id)).Json;
        Assert.Null(saved["useScript"]);
        Assert.Null(saved["useCooldownGroup"]);
    }

    [Fact]
    public async Task Refuse_an_item_use_script_the_published_catalog_does_not_list()
    {
        ulong id = FirstId("item");
        _catalog.Snapshot = new ScriptCatalogSnapshot([], [], [], ["RestoreHealth"]);
        (JsonObject json, string version) = await ReadAsync("item", id);
        json["useScript"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(UnknownMessage(Unlisted), UseScriptErrors(await BodyAsync(response)));
        Assert.Empty(_signal.Requests);
    }

    [Fact]
    public async Task Skip_the_item_script_check_for_a_world_that_publishes_no_item_list()
    {
        ulong id = FirstId("item");
        // The value a world built before item use wrote: no "item" list at all.
        _catalog.Snapshot = ScriptCatalogJson.Deserialize("""{"ai":[],"ability":[],"quest":[]}""");
        Assert.NotNull(_catalog.Snapshot);
        Assert.Null(_catalog.Snapshot.Item);
        (JsonObject json, string version) = await ReadAsync("item", id);
        json["useScript"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Auras, and the aura an ability applies ----

    [Fact]
    public async Task Replace_an_auras_modifiers_and_change_its_version()
    {
        ulong id = FirstId("aura");
        (JsonObject json, string version) = await ReadAsync("aura", id);
        json["modifiers"] = new JsonArray(
            new JsonObject { ["stat"] = "Armor", ["value"] = 15f, ["kind"] = "Percent" },
            new JsonObject { ["stat"] = "DodgePct", ["value"] = 2f, ["kind"] = "Flat" });

        HttpResponseMessage response = await PutAsync(Editable, "aura", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (JsonObject saved, string newVersion) = await ReadAsync("aura", id);
        Assert.NotEqual(version, newVersion);
        Assert.Equal(["Armor:Percent:15", "DodgePct:Flat:2"], saved["modifiers"]!.AsArray()
            .Select(m => $"{m!["stat"]}:{m["kind"]}:{m["value"]}"));
    }

    [Fact]
    public async Task Remove_every_modifier_of_an_aura()
    {
        (JsonObject json, string version) = await ReadAsync("aura", TestWard);
        json["modifiers"] = new JsonArray();

        HttpResponseMessage response = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadAsync("aura", TestWard)).Json["modifiers"]!.AsArray());
        using WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable));
        Assert.DoesNotContain(db.AuraStatModifiers.AsEnumerable(), m => m.AuraId.Value == TestWard);
    }

    [Fact]
    public async Task Refuse_an_aura_the_world_would_refuse_keyed_by_its_field()
    {
        ulong id = FirstId("aura");
        (JsonObject json, string version) = await ReadAsync("aura", id);
        json["tickIntervalMs"] = json["durationMs"]!.GetValue<uint>() + 1;

        HttpResponseMessage response = await PutAsync(Editable, "aura", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await BodyAsync(response))["errors"]!["tickIntervalMs"]);
        Assert.Empty(_signal.Requests);
    }

    [Fact]
    public async Task Refuse_a_stat_modified_twice_or_below_the_tables_floor()
    {
        (JsonObject json, string version) = await ReadAsync("aura", TestWard);
        json["modifiers"] = new JsonArray(
            new JsonObject { ["stat"] = "Armor", ["value"] = 5f, ["kind"] = "Flat" },
            new JsonObject { ["stat"] = "Armor", ["value"] = 5f, ["kind"] = "Percent" });
        HttpResponseMessage twice = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        json["modifiers"] = new JsonArray(new JsonObject { ["stat"] = "Armor", ["value"] = -1_000_000f, ["kind"] = "Flat" });
        HttpResponseMessage floor = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        Assert.NotNull((await BodyAsync(twice))["errors"]!["modifiers"]);
        Assert.Equal(HttpStatusCode.BadRequest, floor.StatusCode);
        Assert.NotNull((await BodyAsync(floor))["errors"]!["modifiers"]);
        Assert.Equal(version, (await ReadAsync("aura", TestWard)).Version);
    }

    [Fact]
    public async Task Save_a_base_damage_coefficient_and_refuse_a_negative_one_by_its_field()
    {
        ulong id = FirstId("aura");
        (JsonObject json, string version) = await ReadAsync("aura", id);
        json["baseDamageCoefficient"] = -1f;

        HttpResponseMessage refused = await PutAsync(Editable, "aura", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.NotNull((await BodyAsync(refused))["errors"]!["baseDamageCoefficient"]);
        Assert.Empty(_signal.Requests);

        json["baseDamageCoefficient"] = 0.5f;
        HttpResponseMessage saved = await PutAsync(Editable, "aura", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        (JsonObject stored, string newVersion) = await ReadAsync("aura", id);
        Assert.Equal(0.5f, stored["baseDamageCoefficient"]!.GetValue<float>());
        Assert.NotEqual(version, newVersion);
    }

    [Fact]
    public async Task Refuse_an_ability_naming_an_aura_that_does_not_exist_or_does_not_fit()
    {
        (JsonObject json, string version) = await ReadAsync("ability", 200);   // Cleave, Hostile
        json["auraId"] = 999_999;
        HttpResponseMessage missing = await PutAsync(Editable, "ability", 200, json, Tag(version));

        json["auraId"] = TestWard;   // a helpful aura on a Hostile ability
        HttpResponseMessage unfit = await PutAsync(Editable, "ability", 200, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("names aura 999999, which is missing or refused",
            (await BodyAsync(missing))["errors"]!["auraId"]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unfit.StatusCode);
        Assert.Contains($"its aura {TestWard} 'Test Ward' is Helpful, which a Hostile ability cannot apply",
            (await BodyAsync(unfit))["errors"]!["auraId"]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(version, (await ReadAsync("ability", 200)).Version);
        Assert.Empty(_signal.Requests);
    }

    [Fact]
    public async Task Save_an_ally_ability_applying_a_helpful_aura_and_read_it_back()
    {
        (JsonObject json, string version) = await ReadAsync("ability", 232);   // Mending Circle, Ally
        json["auraId"] = TestWard;

        HttpResponseMessage response = await PutAsync(Editable, "ability", 232, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestWard, (await ReadAsync("ability", 232)).Json["auraId"]!.GetValue<uint>());
    }

    [Fact]
    public async Task Refuse_an_aura_kind_an_ability_applying_it_cannot_apply()
    {
        Seed(db => db.AbilityTemplates.Find(new Avalon.Common.ValueObjects.AbilityId(232))!.AuraId =
            new Avalon.Common.ValueObjects.AuraId(TestWard));
        (JsonObject json, string version) = await ReadAsync("aura", TestWard);
        json["kind"] = "Harmful";

        HttpResponseMessage response = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ability 232", (await BodyAsync(response))["errors"]!["kind"]![0]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuse_an_aura_script_the_published_catalog_does_not_list()
    {
        ulong id = FirstId("aura");
        _catalog.Snapshot = new ScriptCatalogSnapshot([], [], [], [], ["WardScript"]);
        (JsonObject json, string version) = await ReadAsync("aura", id);
        json["scriptName"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, "aura", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await BodyAsync(response))["errors"]!["scriptName"]);
    }

    [Fact]
    public async Task Accept_an_aura_script_the_published_catalog_lists()
    {
        _catalog.Snapshot = new ScriptCatalogSnapshot([], [], [], [], ["WardScript"]);
        (JsonObject json, string version) = await ReadAsync("aura", TestWard);
        json["scriptName"] = "WardScript";

        HttpResponseMessage response = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("WardScript", (await ReadAsync("aura", TestWard)).Json["scriptName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Skip_the_aura_script_check_for_a_world_that_publishes_no_aura_list()
    {
        // The value a world built before auras wrote: no "aura" list at all.
        _catalog.Snapshot = ScriptCatalogJson.Deserialize("""{"ai":[],"ability":[],"quest":[],"item":[]}""");
        Assert.NotNull(_catalog.Snapshot);
        Assert.Null(_catalog.Snapshot.Aura);
        (JsonObject json, string version) = await ReadAsync("aura", TestWard);
        json["scriptName"] = Unlisted;

        HttpResponseMessage response = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Store_a_blank_aura_script_name_as_null()
    {
        (JsonObject json, string version) = await ReadAsync("aura", TestWard);
        json["scriptName"] = "   ";

        HttpResponseMessage response = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadAsync("aura", TestWard)).Json["scriptName"]);
    }

    [Fact]
    public async Task Refuse_a_stale_aura_version_after_only_a_modifier_changed()
    {
        (JsonObject json, string version) = await ReadAsync("aura", TestWard);
        Seed(db => db.AuraStatModifiers.Single(m => m.AuraId == new Avalon.Common.ValueObjects.AuraId(TestWard)).Value = 11f);
        json["name"] = "Saved from a stale read";

        HttpResponseMessage response = await PutAsync(Editable, "aura", TestWard, json, Tag(version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_a_creature_the_world_reload_would_refuse_even_when_the_field_rules_pass()
    {
        // BaseAttackTime 0.4 is a number any field rule would let by; CreatureTemplateRules is what refuses it.
        ulong id = FirstId("creature");
        (JsonObject json, string version) = await ReadAsync("creature", id);
        json["baseAttackTime"] = 0.4;

        HttpResponseMessage response = await PutAsync(Editable, "creature", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonObject errors = (await BodyAsync(response))["errors"]!.AsObject();
        Assert.Contains("0.5", errors["baseAttackTime"]!.AsArray()[0]!.GetValue<string>());
    }

    // ---- Item edits and the vendors and quests that use the item (the world's Vendors and Quests reloads) ----

    private const int FixtureText = 90001;
    private const uint FixtureQuest = 90010;
    private const uint FixtureObjective = 90011;

    /// <summary>Runs a change against the editable world's database, straight, as a migration or hand edit would.</summary>
    private void Seed(Action<WorldDbContext> change)
    {
        using WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable));
        change(db);
        db.SaveChanges();
    }

    private ulong SeededCreature()
    {
        using WorldDbContext db = _sqlite.CreateWorld(new WorldId(Editable));
        return db.CreatureTemplates.AsEnumerable().Min(t => t.Id.Value);
    }

    private int StockOf(ulong item, uint? priceOverride = null)
    {
        int id = 0;
        Seed(db =>
        {
            var stock = new Avalon.Domain.World.VendorStock
            {
                CreatureTemplateId = new Avalon.Common.ValueObjects.CreatureTemplateId(SeededCreature()),
                Sequence = 1,
                ItemTemplateId = new Avalon.Common.ValueObjects.ItemTemplateId(item),
                PriceOverride = priceOverride,
            };
            db.VendorStocks.Add(stock);
            db.SaveChanges();
            id = stock.Id;
        });
        return id;
    }

    private void QuestOf(ulong? collects, ulong? pays)
    {
        Seed(db =>
        {
            var text = new Avalon.Common.ValueObjects.LocalizedTextId(FixtureText);
            db.LocalizedTexts.Add(new Avalon.Domain.World.LocalizedText { Id = text, Text = "Fixture" });
            db.SaveChanges();
            var creature = new Avalon.Common.ValueObjects.CreatureTemplateId(SeededCreature());
            var quest = new Avalon.Domain.World.QuestTemplate
            {
                Id = new Avalon.Common.ValueObjects.QuestTemplateId(FixtureQuest),
                TitleTextId = text,
                DescriptionTextId = text,
                CompletionTextId = text,
                GiverCreatureId = creature,
                EnderCreatureId = creature,
                LevelRequirement = 1,
            };
            quest.Stages.Add(new Avalon.Domain.World.QuestStage { QuestId = quest.Id, Sequence = 0, DescriptionTextId = text });
            quest.Objectives.Add(collects is { } item
                ? new Avalon.Domain.World.QuestObjective
                {
                    Id = FixtureObjective,
                    QuestId = quest.Id,
                    StageSequence = 0,
                    Type = Avalon.Domain.World.QuestObjectiveType.Collect,
                    ItemTemplateId = new Avalon.Common.ValueObjects.ItemTemplateId(item),
                    Count = 1,
                    DescriptionTextId = text,
                }
                : new Avalon.Domain.World.QuestObjective
                {
                    Id = FixtureObjective,
                    QuestId = quest.Id,
                    StageSequence = 0,
                    Type = Avalon.Domain.World.QuestObjectiveType.Talk,
                    CreatureTemplateId = creature,
                    Count = 1,
                    DescriptionTextId = text,
                });
            if (pays is { } reward)
            {
                quest.ItemRewards.Add(new Avalon.Domain.World.QuestItemReward
                { QuestId = quest.Id, ItemTemplateId = new Avalon.Common.ValueObjects.ItemTemplateId(reward), Count = 1 });
            }

            db.QuestTemplates.Add(quest);
        });
    }

    private void SetItem(ulong id, Action<Avalon.Domain.World.ItemTemplate> change) =>
        Seed(db => change(db.ItemTemplates.Find(new Avalon.Common.ValueObjects.ItemTemplateId(id))!));

    private async Task<JsonObject> PutItemErrorsAsync(ulong id, Action<JsonObject> edit)
    {
        (JsonObject json, string version) = await ReadAsync("item", id);
        edit(json);
        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(version, (await ReadAsync("item", id)).Version);
        Assert.Empty(_signal.Requests);
        JsonObject body = await BodyAsync(response);
        return body["errors"]?.AsObject() ?? throw new InvalidOperationException(body.ToJsonString());
    }

    [Fact]
    public async Task Refuse_making_an_item_a_quest_item_while_a_vendor_sells_it()
    {
        ulong id = FirstId("item");
        SetItem(id, i => i.Flags = Avalon.Domain.World.ItemTemplateFlags.None);
        int stock = StockOf(id);

        JsonObject errors = await PutItemErrorsAsync(id, j => j["flags"] = 2048);

        string message = errors["flags"]!.AsArray()[0]!.GetValue<string>();
        Assert.Contains($"stock row {stock} of creature template {SeededCreature()}", message, StringComparison.Ordinal);
        Assert.Contains("quest item", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuse_raising_the_sell_price_above_what_a_vendor_asks()
    {
        ulong id = FirstId("item");
        SetItem(id, i => { i.Flags = Avalon.Domain.World.ItemTemplateFlags.None; i.BuyPrice = 100; i.SellPrice = 10; });
        int stock = StockOf(id, priceOverride: 50);

        JsonObject errors = await PutItemErrorsAsync(id, j => j["sellPrice"] = 60);

        string message = errors["sellPrice"]!.AsArray()[0]!.GetValue<string>();
        Assert.Contains($"stock row {stock} of creature template {SeededCreature()}", message, StringComparison.Ordinal);
        Assert.Contains("below the item's SellPrice 60", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuse_clearing_the_quest_item_flag_of_an_item_a_quest_collects()
    {
        ulong id = FirstId("item");
        SetItem(id, i => i.Flags = Avalon.Domain.World.ItemTemplateFlags.QuestItem);
        QuestOf(collects: id, pays: null);

        JsonObject errors = await PutItemErrorsAsync(id, j => j["flags"] = 0);

        string message = errors["flags"]!.AsArray()[0]!.GetValue<string>();
        Assert.Contains($"Quest {FixtureQuest}", message, StringComparison.Ordinal);
        Assert.Contains($"objective {FixtureObjective}", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuse_making_a_quest_reward_item_unique()
    {
        ulong id = FirstId("item");
        SetItem(id, i => i.Flags = Avalon.Domain.World.ItemTemplateFlags.None);
        QuestOf(collects: null, pays: id);

        JsonObject errors = await PutItemErrorsAsync(id, j => j["flags"] = 4);

        string message = errors["flags"]!.AsArray()[0]!.GetValue<string>();
        Assert.Contains($"Quest {FixtureQuest}", message, StringComparison.Ordinal);
        Assert.Contains("Unique", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_an_item_edit_that_no_vendor_or_quest_minds()
    {
        ulong id = FirstId("item");
        SetItem(id, i => { i.Flags = Avalon.Domain.World.ItemTemplateFlags.None; i.BuyPrice = 100; i.SellPrice = 10; });
        StockOf(id);
        QuestOf(collects: null, pays: id);
        (JsonObject json, string version) = await ReadAsync("item", id);
        json["name"] = "Renamed";
        json["sellPrice"] = 20;

        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Not_refuse_an_unrelated_edit_because_a_vendor_row_was_already_invalid()
    {
        ulong id = FirstId("item");
        SetItem(id, i => { i.Flags = Avalon.Domain.World.ItemTemplateFlags.None; i.BuyPrice = 100; i.SellPrice = 10; });
        StockOf(id, priceOverride: 5); // already below the SellPrice, so the world refuses this row today
        (JsonObject json, string version) = await ReadAsync("item", id);
        json["name"] = "Renamed";

        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Audit_only_the_fields_that_changed()
    {
        ulong id = FirstId("item");
        (JsonObject json, string version) = await ReadAsync("item", id);
        string oldName = json["name"]!.GetValue<string>();
        uint oldSell = json["maxStackSize"]!.GetValue<uint>();
        json["name"] = "Audited";
        json["maxStackSize"] = oldSell + 7;

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(Editable, "item", id, json, Tag(version))).StatusCode);

        CapturedLog entry = Assert.Single(_logs.Entries, e => e.Message.StartsWith("Template saved", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, entry.Level);
        // The "@" makes Serilog destructure each change into Field, Old and New; without it they are stored as text.
        Assert.Contains("{@Changes}", (string)entry.State["{OriginalFormat}"]!, StringComparison.Ordinal);
        Assert.Equal("Item", entry.State["Kind"]);
        Assert.Equal(id, entry.State["TemplateId"]);
        Assert.Equal(Editable, entry.State["WorldId"]);
        Assert.Equal(ApiTestHost.AccountIdValue, entry.State["AccountId"]);
        IReadOnlyList<TemplateChange> changes = Assert.IsAssignableFrom<IReadOnlyList<TemplateChange>>(entry.State["@Changes"]);
        Assert.Equal(
            [new TemplateChange("maxStackSize", oldSell.ToString(), (oldSell + 7).ToString()),
             new TemplateChange("name", oldName, "Audited")],
            changes.OrderBy(c => c.Field, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Report_the_reload_outcome_the_signal_gives()
    {
        _signal.Result = new TemplateReloadResult("failed", "Creature templates: boom");
        ulong id = FirstId("creature");
        (JsonObject json, string version) = await ReadAsync("creature", id);
        json["name"] = "Wolf?";

        HttpResponseMessage response = await PutAsync(Editable, "creature", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the save stands whatever the world said
        JsonObject reload = (await BodyAsync(response))["reload"]!.AsObject();
        Assert.Equal("failed", reload["status"]!.GetValue<string>());
        Assert.Equal("Creature templates: boom", reload["summary"]!.GetValue<string>());
        Assert.Equal("Wolf?", (await ReadAsync("creature", id)).Json["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Report_failed_when_the_reload_cannot_be_requested_and_keep_the_save()
    {
        _signal.Throw = new InvalidOperationException("redis is down");
        ulong id = FirstId("item");
        (JsonObject json, string version) = await ReadAsync("item", id);
        json["name"] = "Saved anyway";

        HttpResponseMessage response = await PutAsync(Editable, "item", id, json, Tag(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("failed", (await BodyAsync(response))["reload"]!["status"]!.GetValue<string>());
        Assert.Equal("Saved anyway", (await ReadAsync("item", id)).Json["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Map_a_check_violation_that_slipped_past_validation_to_400()
    {
        // A database that refuses every save with a Postgres check violation, as if a rule were missing from here.
        TemplateEditResult<Avalon.Domain.World.ItemTemplate> result = await EditWithFailingDatabaseAsync(
            new FailingSave(PostgresErrorCodes.CheckViolation, "CK_AbilityTemplates_ThreatMultiplier_NonNegative"));

        Assert.Equal(TemplateEditOutcome.Invalid, result.Outcome);
        Assert.Equal(["threatMultiplier"], result.Errors!.Keys);
        Assert.Empty(_signal.Requests);
    }

    [Fact]
    public async Task Map_a_foreign_key_violation_that_slipped_past_validation_to_400()
    {
        // An ability's aura deleted between the check and the save: Postgres refuses its foreign key with 23503.
        TemplateEditResult<Avalon.Domain.World.ItemTemplate> result = await EditWithFailingDatabaseAsync(
            new FailingSave(PostgresErrorCodes.ForeignKeyViolation, "FK_AbilityTemplates_AuraTemplates_AuraId"));

        Assert.Equal(TemplateEditOutcome.Invalid, result.Outcome);
        Assert.Equal(["auraId"], result.Errors!.Keys);
        Assert.Empty(_signal.Requests);
    }

    [Fact]
    public async Task Map_a_serialization_failure_at_save_to_a_conflict()
    {
        // The losing side of two concurrent saves of one version: Postgres fails its UPDATE with 40001.
        TemplateEditResult<Avalon.Domain.World.ItemTemplate> result = await EditWithFailingDatabaseAsync(
            new FailingSave(PostgresErrorCodes.SerializationFailure, null!));

        Assert.Equal(TemplateEditOutcome.Conflict, result.Outcome);
        Assert.Empty(_signal.Requests);
        Assert.DoesNotContain(_logs.Entries, e => e.Message.StartsWith("Template saved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Map_a_serialization_failure_at_commit_to_a_conflict()
    {
        // The same race, caught at COMMIT: a bare PostgresException, not wrapped in a DbUpdateException.
        TemplateEditResult<Avalon.Domain.World.ItemTemplate> result = await EditWithFailingDatabaseAsync(
            new FailingCommit(PostgresErrorCodes.SerializationFailure));

        Assert.Equal(TemplateEditOutcome.Conflict, result.Outcome);
        Assert.Empty(_signal.Requests);
        Assert.DoesNotContain(_logs.Entries, e => e.Message.StartsWith("Template saved", StringComparison.Ordinal));
    }

    private async Task<TemplateEditResult<Avalon.Domain.World.ItemTemplate>> EditWithFailingDatabaseAsync(IInterceptor failure)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        DbContextOptions<WorldDbContext> options = new DbContextOptionsBuilder<WorldDbContext>().UseSqlite(connection)
            .AddInterceptors(failure).Options;
        using (var seed = new WorldDbContext(options)) seed.Database.EnsureCreated();
        var service = new TemplateEditService(new FixedFactory(options), _signal, _catalog, new LoggerOf<TemplateEditService>(_logs.CreateLogger("test")));

        Avalon.Domain.World.ItemTemplate row;
        using (var read = new WorldDbContext(options)) row = read.ItemTemplates.AsNoTracking().AsEnumerable().First();
        var request = new Avalon.Api.Contract.UpdateItemTemplateRequest
        {
            Name = "renamed",
            MaxStackSize = Math.Max(1, row.MaxStackSize),
            AllowedClasses = row.AllowedClasses.ToList(),
            Class = (Avalon.Api.Contract.ItemClass)row.Class,
            SubClass = (Avalon.Api.Contract.ItemSubClass)row.SubClass,
            Rarity = (Avalon.Api.Contract.ItemRarity)row.Rarity,
        };

        return await service.EditItemAsync(
            new TemplateEditCaller(new WorldId(Editable), new Avalon.Common.ValueObjects.AccountId(1)),
            row.Id.Value, Tag(TemplateVersion.Of(row)), request, CancellationToken.None);
    }

    [Fact]
    public void Name_the_field_a_constraint_is_about()
    {
        Assert.Equal("threatMultiplier", TemplateDbErrors.FieldOf(Pg("23514", "CK_AbilityTemplates_ThreatMultiplier_NonNegative")));
        Assert.Equal("bodyRadius", TemplateDbErrors.FieldOf(Pg("23514", "CK_CreatureTemplates_BodyRadius_Positive")));
        Assert.Equal("template", TemplateDbErrors.FieldOf(Pg("23514", "something_else")));
        Assert.Equal("template", TemplateDbErrors.FieldOf(Pg("23514", null)));
        Assert.Equal("auraId", TemplateDbErrors.FieldOf(Pg("23503", "FK_AbilityTemplates_AuraTemplates_AuraId")));
        Assert.Equal("lootTableId", TemplateDbErrors.FieldOf(Pg("23503", "FK_CreatureTemplates_LootTables_LootTableId")));
    }

    [Fact]
    public void Match_if_match_tags_exactly()
    {
        Assert.True(TemplateIfMatch.Matches("\"abc\"", "abc"));
        Assert.True(TemplateIfMatch.Matches("abc", "abc"));
        Assert.True(TemplateIfMatch.Matches("\"zzz\", \"abc\"", "abc"));
        Assert.False(TemplateIfMatch.Matches("*", "abc"));
        Assert.False(TemplateIfMatch.Matches("\"ABC\"", "abc"));
        Assert.False(TemplateIfMatch.Matches("\"ab\"", "abc"));
    }

    private static PostgresException Pg(string sqlState, string? constraint) =>
        new("refused", "ERROR", "ERROR", sqlState, constraintName: constraint);

    private sealed class FailingSave(string sqlState, string constraint) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("save failed", Pg(sqlState, constraint));
    }

    private sealed class FailingCommit(string sqlState) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw Pg(sqlState, null);
    }

    private sealed class FixedFactory(DbContextOptions<WorldDbContext> options) : IDbContextFactory<WorldDbContext>
    {
        public WorldDbContext CreateDbContext() => new(options);
    }

    private sealed class LoggerOf<T>(ILogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => inner.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <summary>The world factory, counting how many contexts each world was asked for.</summary>
    private sealed class CountingWorlds(IWorldDbContextFactory inner) : IWorldDbContextFactory
    {
        private readonly Dictionary<ushort, int> _opened = new();

        public int OpenedFor(ushort world) => _opened.GetValueOrDefault(world);

        private void Count(WorldId world) => _opened[world.Value] = _opened.GetValueOrDefault(world.Value) + 1;

        public WorldDbContext CreateWorld(WorldId world)
        {
            Count(world);
            return inner.CreateWorld(world);
        }

        public Avalon.Database.Character.CharacterDbContext CreateCharacters(WorldId world)
        {
            Count(world);
            return inner.CreateCharacters(world);
        }
    }

    private sealed class FakeCatalog : IWorldScriptCatalog
    {
        public ScriptCatalogSnapshot? Snapshot { get; set; }
        public List<WorldId> Asked { get; } = [];

        /// <summary>When set, answers in place of <see cref="Snapshot"/>: the real catalog over a cache that fails.</summary>
        public IWorldScriptCatalog? Real { get; set; }

        public Task<ScriptCatalogSnapshot?> GetAsync(WorldId world, CancellationToken ct)
        {
            Asked.Add(world);
            return Real is { } real ? real.GetAsync(world, ct) : Task.FromResult(Snapshot);
        }
    }

    private sealed class RecordingSignal : ITemplateReloadSignal
    {
        public List<(WorldId World, TemplateReloadArea Area)> Requests { get; } = [];
        public TemplateReloadResult Result { get; set; } = new("applied", "reloaded");
        public Exception? Throw { get; set; }

        public Task<TemplateReloadResult> RequestAsync(WorldId world, TemplateReloadArea area, CancellationToken ct)
        {
            if (Throw is not null) throw Throw;
            Requests.Add((world, area));
            return Task.FromResult(Result);
        }
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingLogs : ILoggerProvider
    {
        private readonly List<CapturedLog> _entries = [];

        public IReadOnlyList<CapturedLog> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Capture(CapturingLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var values = new Dictionary<string, object?>();
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                    foreach (KeyValuePair<string, object?> pair in pairs) values[pair.Key] = pair.Value;
                lock (owner._entries)
                    owner._entries.Add(new CapturedLog(category, logLevel, formatter(state, exception), values));
            }
        }
    }
}
