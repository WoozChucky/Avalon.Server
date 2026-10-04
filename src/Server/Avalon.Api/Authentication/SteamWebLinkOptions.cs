namespace Avalon.Api.Authentication;

public sealed class SteamWebLinkOptions
{
    public const string Section = "Application:SteamWebLink";
    public const string Scheme = "AvalonSteamLink";
    public const string CallbackPath = "/account/links/steam/callback";
    public const string ProviderEndpoint = "https://steamcommunity.com/openid/login";
    public string CallbackUrl { get; set; } = string.Empty;
    public string SiteUrl { get; set; } = string.Empty;
    public void Validate()
    {
        if (!Valid(CallbackUrl) || !new Uri(CallbackUrl).AbsolutePath.EndsWith(CallbackPath, StringComparison.Ordinal))
            throw new InvalidOperationException(Section + ":CallbackUrl must be a trusted HTTPS URL ending in " + CallbackPath + ".");
        if (!Valid(SiteUrl)) throw new InvalidOperationException(Section + ":SiteUrl must be a trusted HTTPS site URL.");
    }
    private static bool Valid(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    public string ChallengeUrl(Guid id) { Validate(); return CallbackUrl[..^"callback".Length] + "challenge/" + id.ToString("N"); }
    public string ResultUrl(Guid? id, bool failed = false)
    {
        Validate(); return SiteUrl.TrimEnd('/') + "/account/link-store" + (id.HasValue ? "?steamLinkId=" + id.Value.ToString("N") : "?steamError=verification_failed") +
            (id.HasValue && failed ? "&steamError=verification_failed" : string.Empty);
    }
}
