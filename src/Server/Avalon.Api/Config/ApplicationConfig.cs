using Avalon.Configuration;
using Avalon.Infrastructure.Configuration;

namespace Avalon.Api.Config;

public class ApplicationConfig
{
    public string Name { get; set; } = string.Empty;
    public EnvironmentConfig? Environment { get; set; }
    public AuthenticationConfig? Authentication { get; set; }
    public DatabaseConfiguration? Database { get; set; }
    public NotificationConfig? Notification { get; set; }
    public CacheConfiguration? Cache { get; set; }
}
