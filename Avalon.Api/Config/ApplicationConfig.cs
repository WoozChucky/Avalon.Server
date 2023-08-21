namespace Avalon.Api.Config;

public class ApplicationConfig
{
    public string Name { get; set; }
    public EnvironmentConfig Environment { get; set; }
    public AuthenticationConfig Authentication { get; set; }
    public DatabaseConfig Database { get; set; }
}
