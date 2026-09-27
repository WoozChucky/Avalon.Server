using Avalon.Configuration;
using Avalon.Database;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using DatabaseRegistration = Avalon.Database.Extensions.ServiceCollectionExtensions;
using Microsoft.Extensions.Options;

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

/// <summary>
/// Npgsql contexts over the strings under Database:Worlds. Sensitive data logging (#558) follows
/// the rule every other context follows, <see cref="DatabaseRegistration.SensitiveDataLoggingAllowed"/>:
/// on only when the host environment is Development, off with no environment, never a setting.
/// </summary>
public sealed class ConfiguredWorldDbContextFactory(
    IWorldDatabases worlds,
    ILoggerFactory loggerFactory,
    IHostEnvironment? environment = null) : IWorldDbContextFactory
{
    private readonly bool _sensitiveDataLogging = DatabaseRegistration.SensitiveDataLoggingAllowed(environment);

    public WorldDbContext CreateWorld(WorldId world) =>
        new(loggerFactory, Options.Create(new DatabaseConfiguration
        {
            World = new DatabaseConnection { ConnectionString = Require(world).WorldConnectionString },
            EnableSensitiveDataLogging = _sensitiveDataLogging,
        }));

    public CharacterDbContext CreateCharacters(WorldId world) =>
        new(loggerFactory, Options.Create(new DatabaseConfiguration
        {
            Characters = new DatabaseConnection { ConnectionString = Require(world).CharactersConnectionString },
            EnableSensitiveDataLogging = _sensitiveDataLogging,
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
