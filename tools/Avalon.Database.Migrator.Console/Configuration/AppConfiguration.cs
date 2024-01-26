using Avalon.Configuration;
using Avalon.Database.Migrator.Configuration;

namespace Avalon.Database.Migrator.Console.Configuration;

public class AppConfiguration
{
    public DatabaseConfiguration? Database { get; set; }
    public MigratorConfiguration? Migrator { get; set; }
}
