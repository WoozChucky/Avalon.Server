namespace Avalon.Configuration;

public class DatabaseConfiguration
{
    public DatabaseConnection? Auth { get; set; }
    public DatabaseConnection? Characters { get; set; }
    public DatabaseConnection? World { get; set; }
}
