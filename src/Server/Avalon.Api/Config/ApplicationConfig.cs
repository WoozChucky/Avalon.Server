using Avalon.Configuration;

namespace Avalon.Api.Config;

public class ApplicationConfig
{
    public string Name { get; set; } = string.Empty;
    public EnvironmentConfig? Environment { get; set; }
    public AuthenticationConfig? Authentication { get; set; }
    public DatabaseConfiguration? Database { get; set; }
}
