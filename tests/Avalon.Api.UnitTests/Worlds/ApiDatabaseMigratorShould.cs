using System.Net;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// Startup (#523): auth first, and a failure there stops the api; then each world, whose failure
/// marks it unavailable (503) while the others serve. Logs name the world and the exception type,
/// never a connection string.
/// </summary>
public class ApiDatabaseMigratorShould
{
    private const string WorldTwoString = "Host=world-two.internal;Password=w2-secret";
    private const string Auth = "DataSource=:memory:";

    private readonly List<string?> _migrated = [];
    private readonly ListLogger _log = new();

    private static WorldDatabases Worlds() => new(
    [
        new ConfiguredWorld(new WorldId(1), "Host=w1", "Host=c1"),
        new ConfiguredWorld(new WorldId(2), WorldTwoString, "Host=c2"),
        new ConfiguredWorld(new WorldId(3), "Host=w3", "Host=c3"),
    ]);

    private static IDbContextFactory<AuthDbContext> AuthContexts() =>
        new DelegateDbContextFactory<AuthDbContext>(() =>
            new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(Auth).Options));

    /// <summary>Contexts that are never opened: each names its world in its connection string.</summary>
    private sealed class UnopenedWorlds : IWorldDbContextFactory
    {
        public WorldDbContext CreateWorld(WorldId world) =>
            new(new DbContextOptionsBuilder<WorldDbContext>().UseSqlite($"DataSource=world-{world.Value}").Options);

        public CharacterDbContext CreateCharacters(WorldId world) =>
            new(new DbContextOptionsBuilder<CharacterDbContext>().UseSqlite($"DataSource=characters-{world.Value}").Options);
    }

    private ApiDatabaseMigrator Migrator(string failing) => new(_log, (context, _) =>
    {
        string? name = context.Database.GetConnectionString();
        _migrated.Add(name);
        // The exception's message carries a connection string, as a driver's can: it must not reach the log.
        if (name == failing) throw new NpgsqlException($"could not connect: {WorldTwoString}");
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Mark_a_world_whose_migration_fails_unavailable_and_carry_on()
    {
        WorldDatabases worlds = Worlds();

        await Migrator("DataSource=world-2").MigrateAsync(AuthContexts(), worlds, new UnopenedWorlds(), CancellationToken.None);

        Assert.Equal([WorldDatabaseStatus.Available, WorldDatabaseStatus.Unavailable, WorldDatabaseStatus.Available],
            worlds.All.Select(w => w.Status));
        Assert.Equal([Auth, "DataSource=world-1", "DataSource=characters-1", "DataSource=world-2", "DataSource=world-3",
            "DataSource=characters-3"], _migrated);
    }

    [Fact]
    public async Task Mark_a_world_unavailable_when_only_its_characters_database_fails()
    {
        WorldDatabases worlds = Worlds();

        await Migrator("DataSource=characters-1").MigrateAsync(AuthContexts(), worlds, new UnopenedWorlds(), CancellationToken.None);

        Assert.Equal(WorldDatabaseStatus.Unavailable, worlds.All[0].Status);
        Assert.Equal(WorldDatabaseStatus.Available, worlds.All[1].Status);
    }

    [Fact]
    public async Task Refuse_startup_when_the_auth_migration_fails()
    {
        await Assert.ThrowsAsync<NpgsqlException>(() =>
            Migrator(Auth).MigrateAsync(AuthContexts(), Worlds(), new UnopenedWorlds(), CancellationToken.None));

        Assert.Equal([Auth], _migrated);
    }

    [Fact]
    public async Task Log_the_world_and_the_exception_type_but_never_a_connection_string()
    {
        await Migrator("DataSource=world-2").MigrateAsync(AuthContexts(), Worlds(), new UnopenedWorlds(), CancellationToken.None);

        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Text.StartsWith("World 2 is unavailable", StringComparison.Ordinal)
                                           && e.Text.Contains("NpgsqlException", StringComparison.Ordinal));
        Assert.DoesNotContain(_log.Entries, e => e.Text.Contains("w2-secret", StringComparison.Ordinal)
                                                 || e.Text.Contains("world-two.internal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_every_world_with_its_status()
    {
        await Migrator("DataSource=world-2").MigrateAsync(AuthContexts(), Worlds(), new UnopenedWorlds(), CancellationToken.None);

        List<string> summary = _log.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Text).ToList();
        Assert.Equal(["World 1: Available", "World 2: Unavailable", "World 3: Available"], summary);
    }

    [Fact]
    public async Task Answer_503_for_the_failed_world_and_serve_the_others()
    {
        WorldDatabases worlds = Worlds();
        await Migrator("DataSource=world-2").MigrateAsync(AuthContexts(), worlds, new UnopenedWorlds(), CancellationToken.None);

        IWorldRepository authWorlds = Substitute.For<IWorldRepository>();
        foreach (ushort id in new ushort[] { 1, 2, 3 })
            authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(new WorldEntity
                {
                    Id = new WorldId(id), Name = $"World{id}", AccessLevelRequired = AccountAccessLevel.Player,
                    Host = "h", MinVersion = "0.0.1", Version = "0.0.1",
                });
        IItemTemplateRepository items = Substitute.For<IItemTemplateRepository>();
        items.FindByIdAsync(Arg.Any<ItemTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ItemTemplate { Id = new ItemTemplateId(1), Name = "Lantern" });

        await using ApiAuthHost host = await ApiAuthHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(worlds);
            services.AddSingleton(authWorlds);
            services.AddSingleton(items);
        });
        Account account = ApiAuthHost.MakeAccount();
        host.AccountNowIs(account);
        string token = ApiAuthHost.Mint(account);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.GetAsync("/world/2/item-template/1", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/world/1/item-template/1", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync("/world/3/item-template/1", token)).StatusCode);
    }

    /// <summary>Keeps every formatted message, and the exception's text if one is passed, so a leak through either shows.</summary>
    private sealed class ListLogger : ILogger<ApiDatabaseMigrator>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " | " + exception)));
    }
}
