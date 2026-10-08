namespace Avalon.Api.Hosting.Worlds;

/// <summary>Whether a configured world's databases could be reached (#523).</summary>
public enum WorldDatabaseStatus
{
    Available,

    /// <summary>
    /// Its databases could not be reached at startup. It answers 503 until <see cref="WorldDatabaseRecheck"/> reaches
    /// them.
    /// </summary>
    Unavailable,
}
