namespace Avalon.Configuration;

public class DatabaseConfiguration
{
    public DatabaseConnection? Auth { get; set; }
    public DatabaseConnection? Characters { get; set; }
    public DatabaseConnection? World { get; set; }

    /// <summary>
    /// Whether EF Core logs parameter values and entity data (#558). Not a setting: the database
    /// registration sets it from the host environment, on only in Development, whatever the
    /// configuration says, so the auth database's secrets never reach a shipped log.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }
}
