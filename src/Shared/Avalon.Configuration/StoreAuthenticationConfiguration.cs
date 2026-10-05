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
    public Dictionary<string, GameProviderApplicationConfiguration> AdditionalApplications { get; set; } = new(StringComparer.Ordinal);

    public GameApplicationSelection? ResolveApplication(string? key)
    {
        if (key == "avalon.base") return new(key, "avalon", "base", Product, Environment, Array.Empty<ushort>(), false);
        if (key is "steam.main" or "steam.playtest")
        {
            var app = ResolveSteamApplication(key == "steam.main" ? SteamAppId : SteamPlaytest.AppId);
            if (app is null || (key == "steam.playtest" && !app.Restricted)) return null;
            return new(key, "steam", app.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture), Product,
                Environment, app.AllowedWorldIds, app.Restricted);
        }
        if (key is null || !AdditionalApplications.TryGetValue(key, out var configured) || configured is not { Enabled: true } ||
            !SourceText(key, 128) || !SourceText(configured.Provider, 32) || !SourceText(configured.ProviderProductId, 128) ||
            configured.Provider is "steam" or "avalon" || configured.AllowedWorldIds is null ||
            configured.AllowedWorldIds.Contains((ushort)0) || configured.AllowedWorldIds.Distinct().Count() != configured.AllowedWorldIds.Length ||
            (configured.Restricted && configured.AllowedWorldIds.Length == 0)) return null;
        return new(key, configured.Provider, configured.ProviderProductId, Product, Environment, configured.AllowedWorldIds, configured.Restricted);
    }

    private static bool SourceText(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;

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
