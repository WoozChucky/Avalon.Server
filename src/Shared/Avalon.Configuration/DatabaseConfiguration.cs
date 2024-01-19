namespace Avalon.Configuration;

public class DatabaseConfiguration
{
    public DatabaseConnection Auth { get; set; }
    public DatabaseConnection Characters { get; set; }
    public DatabaseConnection World { get; set; }
}

public class DatabaseConnection
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string Database { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}
