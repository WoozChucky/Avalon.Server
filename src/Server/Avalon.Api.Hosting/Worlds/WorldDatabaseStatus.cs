namespace Avalon.Api.Hosting.Worlds;

/// <summary>Whether a configured world's databases migrated at startup (#523).</summary>
public enum WorldDatabaseStatus
{
    Available,

    /// <summary>Its migration failed at startup. It answers 503 until the next restart.</summary>
    Unavailable,
}
