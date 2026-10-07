using Avalon.Api.Hosting.Config;
using Avalon.Infrastructure.Configuration;

namespace Avalon.Api.Config;

public class ApplicationConfig
{
    public const string Section = "Application";

    public string Name { get; set; } = string.Empty;
    public EnvironmentConfig? Environment { get; set; }
    public AuthenticationConfig? Authentication { get; set; }
    public NotificationConfig? Notification { get; set; }
    public CacheConfiguration? Cache { get; set; }
    public ForwardedHeadersConfig? ForwardedHeaders { get; set; }
    public EmailConfig? Email { get; set; }

    /// <summary>The "Application" section, bound as the services still in Avalon.Api read it.</summary>
    public static ApplicationConfig Bind(IConfiguration configuration)
    {
        ApplicationConfig applicationConfig = new();
        configuration.Bind(Section, applicationConfig);
        return applicationConfig;
    }
}
