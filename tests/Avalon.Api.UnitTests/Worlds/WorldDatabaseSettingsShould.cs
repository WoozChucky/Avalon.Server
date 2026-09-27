using System.Text;
using Avalon.Api.Worlds;
using Avalon.Domain.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>Database:Worlds (#523): parsed once at startup, refused with the setting named.</summary>
public class WorldDatabaseSettingsShould
{
    private const string WorldOne = "Host=world-one;Password=w1-secret";
    private const string CharactersOne = "Host=characters-one;Password=c1-secret";

    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    private static (string, string?)[] Pair(string id, string world, string characters) =>
    [
        ($"Database:Worlds:{id}:World:ConnectionString", world),
        ($"Database:Worlds:{id}:Characters:ConnectionString", characters),
    ];

    [Fact]
    public void Parse_one_world()
    {
        ConfiguredWorld world = Assert.Single(WorldDatabaseSettings.Parse(Config(Pair("1", WorldOne, CharactersOne))));

        Assert.Equal((ushort)1, world.Id.Value);
        Assert.Equal(WorldOne, world.WorldConnectionString);
        Assert.Equal(CharactersOne, world.CharactersConnectionString);
        Assert.Equal(WorldDatabaseStatus.Available, world.Status);
    }

    [Fact]
    public void Parse_several_worlds_in_id_order()
    {
        IReadOnlyList<ConfiguredWorld> worlds = WorldDatabaseSettings.Parse(
            Config([.. Pair("3", "Host=w3", "Host=c3"), .. Pair("1", "Host=w1", "Host=c1"), .. Pair("2", "Host=w2", "Host=c2")]));

        Assert.Equal(new ushort[] { 1, 2, 3 }, worlds.Select(w => w.Id.Value));
        Assert.Equal("Host=c2", worlds[1].CharactersConnectionString);
    }

    [Fact]
    public void Refuse_a_configuration_with_no_world()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorldDatabaseSettings.Parse(Config(("Database:Auth:ConnectionString", "Host=auth"))));

        Assert.StartsWith("Database:Worlds lists no world", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("65536")]
    [InlineData("abc")]
    public void Refuse_a_world_id_that_is_not_a_positive_integer(string id)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorldDatabaseSettings.Parse(Config(Pair(id, "Host=w", "Host=c"))));

        Assert.StartsWith($"Database:Worlds:{id}: a world id is a positive integer", ex.Message);
    }

    [Theory]
    [InlineData("1", (ushort)1)]
    [InlineData("42", (ushort)42)]
    [InlineData("65535", (ushort)65535)]
    public void Read_a_canonical_world_id(string text, ushort expected)
    {
        Assert.True(WorldDatabaseSettings.TryParseWorldId(text, out WorldId? id));
        Assert.Equal(expected, id.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("1e1")]
    [InlineData("0x1")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("١")]
    public void Refuse_a_world_id_that_is_not_canonical(string? text)
    {
        Assert.False(WorldDatabaseSettings.TryParseWorldId(text, out WorldId? id));
        Assert.Null(id);
    }

    [Fact]
    public void Refuse_a_world_with_only_its_world_string()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorldDatabaseSettings.Parse(Config(("Database:Worlds:1:World:ConnectionString", WorldOne))));

        Assert.StartsWith("Database:Worlds:1:Characters:ConnectionString is missing", ex.Message);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_world_with_only_its_characters_string()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorldDatabaseSettings.Parse(Config(("Database:Worlds:1:Characters:ConnectionString", CharactersOne))));

        Assert.StartsWith("Database:Worlds:1:World:ConnectionString is missing", ex.Message);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuse_a_blank_string(string blank)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorldDatabaseSettings.Parse(Config(Pair("1", blank, CharactersOne))));

        Assert.Equal("Database:Worlds:1:World:ConnectionString is blank.", ex.Message);
    }

    [Fact]
    public void Keep_its_connection_strings_out_of_its_text() =>
        Assert.Equal("World 4 (Available)", new ConfiguredWorld(new WorldId(4), WorldOne, CharactersOne).ToString());

    [Fact]
    public void Mark_a_world_unavailable()
    {
        WorldDatabases databases = new([new ConfiguredWorld(new WorldId(4), WorldOne, CharactersOne)]);

        databases.MarkUnavailable(new WorldId(4));

        Assert.True(databases.TryGet(new WorldId(4), out ConfiguredWorld? world));
        Assert.Equal(WorldDatabaseStatus.Unavailable, world.Status);
    }

    [Fact]
    public void Not_find_a_world_it_was_not_given()
    {
        WorldDatabases databases = new([new ConfiguredWorld(new WorldId(4), WorldOne, CharactersOne)]);

        Assert.False(databases.TryGet(new WorldId(5), out _));
    }

    [Fact]
    public void Call_a_configured_world_available_until_it_is_marked()
    {
        WorldDatabases databases = new([
            new ConfiguredWorld(new WorldId(4), WorldOne, CharactersOne),
            new ConfiguredWorld(new WorldId(7), WorldOne, CharactersOne),
        ]);

        Assert.True(databases.IsAvailable(new WorldId(4)));

        databases.MarkUnavailable(new WorldId(4));

        Assert.False(databases.IsAvailable(new WorldId(4)));
        Assert.True(databases.IsAvailable(new WorldId(7)));
    }

    [Fact]
    public void Not_call_a_world_it_was_not_given_available() =>
        Assert.False(new WorldDatabases([new ConfiguredWorld(new WorldId(4), WorldOne, CharactersOne)])
            .IsAvailable(new WorldId(5)));

    [Fact]
    public void Refuse_to_mark_a_world_it_was_not_given()
    {
        WorldDatabases databases = new([new ConfiguredWorld(new WorldId(4), WorldOne, CharactersOne)]);

        var ex = Assert.Throws<InvalidOperationException>(() => databases.MarkUnavailable(new WorldId(5)));

        Assert.Contains("World 5", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_the_json_shape()
    {
        const string json = """
            {
              "Database": {
                "Auth": { "ConnectionString": "Host=auth" },
                "Worlds": {
                  "1": { "World": { "ConnectionString": "Host=w1" }, "Characters": { "ConnectionString": "Host=c1" } },
                  "2": { "World": { "ConnectionString": "Host=w2" }, "Characters": { "ConnectionString": "Host=c2" } }
                }
              }
            }
            """;
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
        IConfiguration configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        IReadOnlyList<ConfiguredWorld> worlds = WorldDatabaseSettings.Parse(configuration);

        Assert.Equal(new ushort[] { 1, 2 }, worlds.Select(w => w.Id.Value));
        Assert.Equal("Host=w2", worlds[1].WorldConnectionString);
        Assert.Equal("Host=c2", worlds[1].CharactersConnectionString);
    }

    [Fact]
    public void Parse_the_environment_variable_form()
    {
        // A prefix no other test or host variable uses, so this can run beside the other tests.
        const string prefix = "AV523T_";
        const string world = prefix + "Database__Worlds__2__World__ConnectionString";
        const string characters = prefix + "Database__Worlds__2__Characters__ConnectionString";
        try
        {
            Environment.SetEnvironmentVariable(world, "Host=w2");
            Environment.SetEnvironmentVariable(characters, "Host=c2");
            IConfiguration configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();

            ConfiguredWorld parsed = Assert.Single(WorldDatabaseSettings.Parse(configuration));

            Assert.Equal((ushort)2, parsed.Id.Value);
            Assert.Equal("Host=w2", parsed.WorldConnectionString);
            Assert.Equal("Host=c2", parsed.CharactersConnectionString);
        }
        finally
        {
            Environment.SetEnvironmentVariable(world, null);
            Environment.SetEnvironmentVariable(characters, null);
        }
    }
}
