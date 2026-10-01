using System.Text.Json;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Scripts;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World.Configuration;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// The names a world offers the admin app: what <see cref="ScriptManager"/> registered, and the JSON the
/// <see cref="ScriptCatalogPublisher"/> writes under the world's catalog key. The expectations come from the types
/// themselves (<c>typeof(X).Name</c>), so no script name is spelled out here.
/// </summary>
public class ScriptCatalogShould
{
    private const ushort World = 4;

    private readonly TestLog _log = new();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    private ScriptManager Manager() => new(_log);

    private ScriptCatalogPublisher Publisher(IScriptManager scripts) => new(scripts, _cache,
        Options.Create(new GameConfiguration { WorldId = World }), new Logger(_log));

    private string[] Writes() => _cache.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(IReplicatedCache.SetAsync))
        .Select(c =>
        {
            object?[] args = c.GetArguments();
            Assert.Equal(CacheKeys.WorldScriptCatalog(World), args[0]);
            Assert.Null(args[2]);
            return (string)args[1]!;
        }).ToArray();

    private static ScriptCatalogSnapshot Read(string json) =>
        JsonSerializer.Deserialize<ScriptCatalogSnapshot>(json, ScriptCatalogJson.Options)!;

    [Fact]
    public void Key_the_catalog_by_world()
    {
        Assert.Equal("world:4:scripts", CacheKeys.WorldScriptCatalog(4));
    }

    [Fact]
    public void Expose_sorted_names_of_every_loaded_script_kind()
    {
        ScriptManager manager = Manager();

        manager.Load();

        Assert.Contains(typeof(ThrowOnLeaveScript).Name, manager.AiScriptNames);
        Assert.Contains(typeof(RecordingAbilityScript).Name, manager.AbilityScriptNames);
        Assert.Contains(typeof(SampleQuestScript).Name, manager.QuestScriptNames);
        Assert.Equal(manager.AiScriptNames.OrderBy(n => n, StringComparer.Ordinal), manager.AiScriptNames);
        Assert.Equal(manager.AbilityScriptNames.OrderBy(n => n, StringComparer.Ordinal), manager.AbilityScriptNames);
        Assert.Equal(manager.QuestScriptNames.OrderBy(n => n, StringComparer.Ordinal), manager.QuestScriptNames);
    }

    [Fact]
    public void Leave_chained_scripts_out_of_the_ai_names()
    {
        ScriptManager manager = Manager();

        manager.Load();

        Assert.DoesNotContain(nameof(KitCombatScript), manager.AiScriptNames);
        Assert.DoesNotContain(nameof(KitPatrolScript), manager.AiScriptNames);
        Assert.DoesNotContain(nameof(KitAggroDefendScript), manager.AiScriptNames);
    }

    [Fact]
    public void Name_nothing_before_it_has_loaded()
    {
        ScriptManager manager = Manager();

        Assert.Empty(manager.AiScriptNames);
        Assert.Empty(manager.AbilityScriptNames);
        Assert.Empty(manager.QuestScriptNames);
    }

    [Fact]
    public void Register_hot_reloaded_ai_scripts_by_name_and_skip_chained_ones()
    {
        ScriptManager manager = Manager();

        manager.RegisterHotReloaded([typeof(ThrowOnLeaveScript), typeof(KitCombatScript)]);

        Assert.Equal([typeof(ThrowOnLeaveScript).Name], manager.AiScriptNames);
        Assert.Equal(typeof(ThrowOnLeaveScript), manager.GetAiScript(typeof(ThrowOnLeaveScript).Name));
        Assert.Null(manager.GetAiScript(nameof(KitCombatScript)));
    }

    [Fact]
    public async Task Publish_the_loaded_names_under_the_worlds_key_with_no_expiry()
    {
        ScriptManager manager = Manager();
        manager.Load();

        await Publisher(manager).PublishAsync();

        ScriptCatalogSnapshot snapshot = Read(Assert.Single(Writes()));
        Assert.Equal(manager.AiScriptNames, snapshot.Ai);
        Assert.Equal(manager.AbilityScriptNames, snapshot.Ability);
        Assert.Equal(manager.QuestScriptNames, snapshot.Quest);
        Assert.DoesNotContain(nameof(KitCombatScript), snapshot.Ai);
    }

    [Fact]
    public async Task Publish_again_with_the_new_names_after_a_hot_reload()
    {
        ScriptManager manager = Manager();
        ScriptCatalogPublisher publisher = Publisher(manager);
        await publisher.PublishAsync();

        manager.RegisterHotReloaded([typeof(ThrowOnLeaveScript)]);
        await publisher.PublishAsync();

        string[] writes = Writes();
        Assert.Equal(2, writes.Length);
        Assert.Empty(Read(writes[0]).Ai);
        Assert.Equal([typeof(ThrowOnLeaveScript).Name], Read(writes[1]).Ai);
    }

    [Fact]
    public async Task Log_and_carry_on_when_the_cache_refuses_the_write()
    {
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("redis down")));

        await Publisher(Manager()).PublishAsync();

        Assert.Single(_log.Errors);
    }

    private sealed class Logger(TestLog log) : ILogger<ScriptCatalogPublisher>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => log.Log(logLevel, eventId, state, exception, formatter);
    }
}
