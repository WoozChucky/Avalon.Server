using Avalon.Configuration;
using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Worlds;

/// <summary>
/// Opens a named world's contexts (#523): for startup migration and for the reads that span worlds
/// (IWorldRepositories). The caller disposes what it gets. Request code reaches the request's world
/// through the ordinary repositories instead.
/// </summary>
public interface IWorldDbContextFactory
{
    WorldDbContext CreateWorld(WorldId world);
    CharacterDbContext CreateCharacters(WorldId world);
}

/// <summary>Npgsql contexts over the strings under Database:Worlds.</summary>
public sealed class ConfiguredWorldDbContextFactory(IWorldDatabases worlds, ILoggerFactory loggerFactory)
    : IWorldDbContextFactory
{
    public WorldDbContext CreateWorld(WorldId world) =>
        new(loggerFactory, Microsoft.Extensions.Options.Options.Create(new DatabaseConfiguration
        {
            World = new DatabaseConnection { ConnectionString = Require(world).WorldConnectionString },
        }));

    public CharacterDbContext CreateCharacters(WorldId world) =>
        new(loggerFactory, Microsoft.Extensions.Options.Options.Create(new DatabaseConfiguration
        {
            Characters = new DatabaseConnection { ConnectionString = Require(world).CharactersConnectionString },
        }));

    private ConfiguredWorld Require(WorldId world) =>
        worlds.TryGet(world, out ConfiguredWorld? configured)
            ? configured
            : throw new InvalidOperationException(
                $"World {world.Value} is not configured on this api ({WorldDatabaseSettings.Section}:{world.Value}).");
}

public static class WorldDbContextFactoryExtensions
{
    /// <summary>A context factory fixed to one world, for building a repository over it.</summary>
    public static IDbContextFactory<WorldDbContext> ForWorld(this IWorldDbContextFactory factory, WorldId world) =>
        new DelegateDbContextFactory<WorldDbContext>(() => factory.CreateWorld(world));

    public static IDbContextFactory<CharacterDbContext> ForCharacters(this IWorldDbContextFactory factory, WorldId world) =>
        new DelegateDbContextFactory<CharacterDbContext>(() => factory.CreateCharacters(world));
}
