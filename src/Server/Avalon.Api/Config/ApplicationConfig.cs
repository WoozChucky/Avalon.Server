using Avalon.Infrastructure.Configuration;

namespace Avalon.Api.Config;

/// <summary>
/// The "Application" section. The API's databases are not here: they are the top-level
/// <c>Database:Auth</c> and <c>Database:Worlds</c> (#582).
/// </summary>
public class ApplicationConfig
{
    public string Name { get; set; } = string.Empty;
    public EnvironmentConfig? Environment { get; set; }
    public AuthenticationConfig? Authentication { get; set; }
    public NotificationConfig? Notification { get; set; }
    public CacheConfiguration? Cache { get; set; }
    public ForwardedHeadersConfig? ForwardedHeaders { get; set; }
    public EmailConfig? Email { get; set; }
    public Distribution.DistributionConfiguration? Distribution { get; set; }
}
