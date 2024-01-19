using Avalon.Configuration;
using Avalon.Database.Migrator.Configuration;

namespace Avalon.Api.Config;

public class ApplicationConfig
{
    public string Name { get; set; }
    public EnvironmentConfig Environment { get; set; }
    public AuthenticationConfig Authentication { get; set; }
    public DatabaseConfiguration Database { get; set; }
    public MigratorConfiguration Migrator { get; set; }
}
