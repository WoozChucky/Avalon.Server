using Avalon.Domain.Auth;

namespace Avalon.Api.Worlds;

/// <summary>Whether a configured world's databases migrated at startup (#523).</summary>
public enum WorldDatabaseStatus
{
    Available,

    /// <summary>Its migration failed at startup. It answers 503 until the next restart.</summary>
    Unavailable,
}

/// <summary>
/// One world under Database:Worlds: its id (the auth Worlds row's), its world and characters
/// connection strings, and its status. A class, not a record, so no generated ToString can print
/// the strings.
/// </summary>
public sealed class ConfiguredWorld
{
    public ConfiguredWorld(WorldId id, string worldConnectionString, string charactersConnectionString)
    {
        Id = id;
        WorldConnectionString = worldConnectionString;
        CharactersConnectionString = charactersConnectionString;
    }

    public WorldId Id { get; }
    public string WorldConnectionString { get; }
    public string CharactersConnectionString { get; }

    /// <summary>Available until startup migration says otherwise; written only before the api serves.</summary>
    public WorldDatabaseStatus Status { get; private set; } = WorldDatabaseStatus.Available;

    internal void MarkUnavailable() => Status = WorldDatabaseStatus.Unavailable;

    /// <summary>The id and status only: never the connection strings.</summary>
    public override string ToString() => $"World {Id.Value} ({Status})";
}
