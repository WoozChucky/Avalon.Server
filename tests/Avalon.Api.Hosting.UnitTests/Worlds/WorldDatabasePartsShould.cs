using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Testing;
using Avalon.Domain.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Worlds;

/// <summary>
/// A process reads only the world databases its services need (#794, design section 5.1): Database:Worlds then needs
/// only their strings, and the others are neither required nor opened.
/// </summary>
public sealed class WorldDatabasePartsShould
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void Read_only_the_characters_string_when_only_characters_are_needed()
    {
        ConfiguredWorld world = Assert.Single(WorldDatabaseSettings.Parse(
            Config(("Database:Worlds:1:Characters:ConnectionString", "Host=c1")), WorldDatabaseParts.Characters));

        Assert.Null(world.WorldConnectionString);
        Assert.Equal("Host=c1", world.CharactersConnectionString);
    }

    [Fact]
    public void Ignore_a_string_the_process_does_not_read()
    {
        ConfiguredWorld world = Assert.Single(WorldDatabaseSettings.Parse(
            Config(("Database:Worlds:1:World:ConnectionString", " "), ("Database:Worlds:1:Characters:ConnectionString", "Host=c1")),
            WorldDatabaseParts.Characters));

        Assert.Null(world.WorldConnectionString);
    }

    [Fact]
    public void Refuse_a_missing_string_the_process_reads_naming_it()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => WorldDatabaseSettings.Parse(
            Config(("Database:Worlds:1:World:ConnectionString", "Host=w1")), WorldDatabaseParts.Characters));

        Assert.Equal("Database:Worlds:1:Characters:ConnectionString is missing: this api reads each world's Characters database.",
            refused.Message);
    }

    [Fact]
    public void Name_only_the_strings_the_process_reads_when_no_world_is_listed()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            WorldDatabaseSettings.Parse(Config(), WorldDatabaseParts.World));

        Assert.Contains("set Database:Worlds:<id>:World:ConnectionString (environment: Database__Worlds__<id>__World__ConnectionString)",
            refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Characters", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_to_open_a_database_the_process_does_not_read_naming_the_setting()
    {
        var factory = new ConfiguredWorldDbContextFactory(
            new WorldDatabases([new ConfiguredWorld(new WorldId(1), null, "Host=c1")]), NullLoggerFactory.Instance);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => factory.CreateWorld(new WorldId(1)));

        Assert.Contains("Database:Worlds:1:World:ConnectionString", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_only_the_databases_the_process_reads()
    {
        using var sqlite = new SqliteWorlds(1);
        var worlds = new WorldDatabases([new ConfiguredWorld(new WorldId(1), null, "Host=c1")]);
        List<string> checkedContexts = [];
        var migrator = new ApiDatabaseMigrator(NullLogger<ApiDatabaseMigrator>.Instance,
            canConnect: (context, _) =>
            {
                checkedContexts.Add(context.GetType().Name);
                return Task.FromResult(true);
            });

        await migrator.CheckWorldsAsync(worlds, sqlite, CancellationToken.None);

        Assert.Equal(["CharacterDbContext"], checkedContexts);
        Assert.True(worlds.IsAvailable(new WorldId(1)));
    }
}
