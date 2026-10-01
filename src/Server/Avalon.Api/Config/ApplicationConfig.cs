using Avalon.Infrastructure.Configuration;

namespace Avalon.Api.Config;

public class ApplicationConfig
{
    public string Name { get; set; } = string.Empty;
    /// <summary>The world public tooltip links read when they name none (the live world); falls back to the first readable one.</summary>
    public ushort? PublicWorldId { get; set; }
    /// <summary>
    /// The public website's base URL, which link previews name as a page's canonical address. Unset by
    /// default (a deployment setting); validated at startup by <see cref="Previews.PublicSiteSettings.Create"/>.
    /// </summary>
    public string? PublicSiteUrl { get; set; }
    public EnvironmentConfig? Environment { get; set; }
    public AuthenticationConfig? Authentication { get; set; }
    public NotificationConfig? Notification { get; set; }
    public CacheConfiguration? Cache { get; set; }
    public ForwardedHeadersConfig? ForwardedHeaders { get; set; }
    public EmailConfig? Email { get; set; }
    public Distribution.DistributionConfiguration? Distribution { get; set; }
    public Balance.BalanceConfiguration? Balance { get; set; }
}
