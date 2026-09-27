using Avalon.Api.Worlds;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Domain.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// Real world and characters databases for several worlds, one SQLite in-memory connection each,
/// held open for the fixture's life (the connection is the database). Stands in for the api's
/// Npgsql <see cref="ConfiguredWorldDbContextFactory"/>.
/// </summary>
public sealed class SqliteWorlds : IWorldDbContextFactory, IDisposable
{
    private readonly Dictionary<ushort, SqliteConnection> _world = new();
    private readonly Dictionary<ushort, SqliteConnection> _characters = new();

    public SqliteWorlds(params ushort[] ids)
    {
        foreach (ushort id in ids)
        {
            _world[id] = Open();
            _characters[id] = Open();
            using (WorldDbContext world = CreateWorld(new WorldId(id))) world.Database.EnsureCreated();
            using (CharacterDbContext characters = CreateCharacters(new WorldId(id))) characters.Database.EnsureCreated();
        }
    }

    public WorldDbContext CreateWorld(WorldId world) =>
        new(new DbContextOptionsBuilder<WorldDbContext>().UseSqlite(Connection(_world, world)).Options);

    public CharacterDbContext CreateCharacters(WorldId world) =>
        new(new DbContextOptionsBuilder<CharacterDbContext>().UseSqlite(Connection(_characters, world)).Options);

    private static SqliteConnection Open()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static SqliteConnection Connection(Dictionary<ushort, SqliteConnection> all, WorldId world) =>
        all.TryGetValue(world.Value, out SqliteConnection? connection)
            ? connection
            : throw new InvalidOperationException($"World {world.Value} is not configured.");

    public void Dispose()
    {
        foreach (SqliteConnection connection in _world.Values.Concat(_characters.Values)) connection.Dispose();
    }
}
