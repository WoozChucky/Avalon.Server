namespace Avalon.Configuration;

/// <summary>Trusted host configuration. Product selection is not a field of client proof requests.</summary>
public sealed class StoreAuthenticationConfiguration
{
    public const string Product = "avalon.base";
    /// <summary>Required deployment-owned application identity; there is no built-in App ID.</summary>
    public uint SteamAppId { get; set; }
    public SteamPlaytestConfiguration SteamPlaytest { get; set; } = new();
    /// <summary>Injected through backend secret configuration, never source-controlled or returned to a client.</summary>
    public string SteamPublisherKey { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";
    public string SteamIdentityPrefix { get; set; } = "avalon-auth-prod";
    public int PolicyVersion { get; set; } = 1;
    public bool DirectGrantsEnabled { get; set; }

    public SteamApplicationSelection? ResolveSteamApplication(uint? appId)
    {
        var selected = appId ?? SteamAppId;
        if (selected == 0) return null;
        if (selected == SteamAppId)
            return new(selected, false, Array.Empty<ushort>());
        if (SteamPlaytest is { Enabled: true } playtest && selected == playtest.AppId &&
            playtest.AllowedWorldIds is { Length: > 0 } worlds && !worlds.Contains((ushort)0) &&
            worlds.Distinct().Count() == worlds.Length)
            return new(selected, true, Array.AsReadOnly((ushort[])worlds.Clone()));
        return null;
    }

    public void Validate(bool production)
    {
        if (SteamAppId == 0)
            throw new InvalidOperationException("Application:StoreAuthentication:SteamAppId is required and must be greater than zero.");
        if (SteamPlaytest is null || SteamPlaytest.AllowedWorldIds is null ||
            (SteamPlaytest.AppId != 0 && SteamPlaytest.AppId == SteamAppId) ||
            SteamPlaytest.AllowedWorldIds.Contains((ushort)0) ||
            SteamPlaytest.AllowedWorldIds.Distinct().Count() != SteamPlaytest.AllowedWorldIds.Length ||
            (SteamPlaytest.Enabled && (SteamPlaytest.AppId == 0 || SteamPlaytest.AllowedWorldIds.Length == 0)))
            throw new InvalidOperationException("Invalid Application:StoreAuthentication:SteamPlaytest configuration. Use a distinct positive application ID and nonempty distinct positive allowed world IDs when enabled.");
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
