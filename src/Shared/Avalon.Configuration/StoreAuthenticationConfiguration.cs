namespace Avalon.Configuration;

/// <summary>Trusted host configuration. Product selection is not a field of client proof requests.</summary>
public sealed class StoreAuthenticationConfiguration
{
    public const string Product = "avalon.base";
    /// <summary>Required deployment-owned application identity; there is no built-in App ID.</summary>
    public uint SteamAppId { get; set; }
    /// <summary>Injected through backend secret configuration, never source-controlled or returned to a client.</summary>
    public string SteamPublisherKey { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";
    public string SteamIdentityPrefix { get; set; } = "avalon-auth-prod";
    public int PolicyVersion { get; set; } = 1;
    public bool DirectGrantsEnabled { get; set; }

    public void Validate(bool production)
    {
        if (SteamAppId == 0)
            throw new InvalidOperationException("Application:StoreAuthentication:SteamAppId is required and must be greater than zero.");
        if (string.IsNullOrWhiteSpace(SteamPublisherKey) ||
            SteamPublisherKey != SteamPublisherKey.Trim() || PolicyVersion < 1 || DirectGrantsEnabled ||
            (Environment != "production" && Environment != "development") ||
            (Environment == "production" && SteamIdentityPrefix != "avalon-auth-prod") ||
            (Environment == "development" && SteamIdentityPrefix != "avalon-auth-dev") ||
            (production && Environment != "production"))
        {
            throw new InvalidOperationException("Invalid Application:StoreAuthentication configuration. " +
                "Configure Avalon's Steam application, publisher secret, environment, identity prefix and policy version; direct grants are unavailable.");
        }
    }
}
