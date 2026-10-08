using Avalon.Domain.Auth;

namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// One world under Database:Worlds: its id (the auth Worlds row's), its world and characters
/// connection strings, and its status. A string is null when this process does not read that database
/// (<see cref="WorldDatabaseParts"/>). A class, not a record, so no generated ToString can print
/// the strings.
/// </summary>
public sealed class ConfiguredWorld
{
    // Written by the startup check, then only by the recheck (WorldDatabaseRecheck); read by every request.
    private volatile WorldDatabaseStatus _status = WorldDatabaseStatus.Available;

    public ConfiguredWorld(WorldId id, string? worldConnectionString, string? charactersConnectionString)
    {
        Id = id;
        WorldConnectionString = worldConnectionString;
        CharactersConnectionString = charactersConnectionString;
    }

    public WorldId Id { get; }
    public string? WorldConnectionString { get; }
    public string? CharactersConnectionString { get; }

    /// <summary>
    /// Available unless the startup check could not reach its databases; such a world is available again once the
    /// recheck reaches them, and is never marked unavailable after that.
    /// </summary>
    public WorldDatabaseStatus Status => _status;

    internal void MarkUnavailable() => _status = WorldDatabaseStatus.Unavailable;

    internal void MarkAvailable() => _status = WorldDatabaseStatus.Available;

    /// <summary>The id and status only: never the connection strings.</summary>
    public override string ToString() => $"World {Id.Value} ({Status})";
}
