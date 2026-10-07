using Avalon.Api.Worlds.Balance;

namespace Avalon.Api.Worlds.Config;

/// <summary>
/// The worlds service's settings directly under the <c>Application</c> section, bound by these property names as
/// Avalon.Api's <c>ApplicationConfig</c> bound them before the split (#794).
/// </summary>
public sealed class WorldsConfig
{
    public const string Section = "Application";

    /// <summary>The world public tooltip links read when they name none (the live world); falls back to the first readable one.</summary>
    public ushort? PublicWorldId { get; set; }

    /// <summary>
    /// The public website's base URL, which link previews name as a page's canonical address. Unset by
    /// default (a deployment setting); validated at startup by <see cref="Previews.PublicSiteSettings.Create"/>.
    /// </summary>
    public string? PublicSiteUrl { get; set; }

    /// <summary>The balance workbench's service (<c>Application:Balance</c>).</summary>
    public BalanceConfiguration? Balance { get; set; }

    /// <summary>The <c>Application</c> section, bound as the worlds service reads it.</summary>
    public static WorldsConfig Bind(IConfiguration configuration)
    {
        WorldsConfig config = new();
        configuration.Bind(Section, config);
        return config;
    }
}
