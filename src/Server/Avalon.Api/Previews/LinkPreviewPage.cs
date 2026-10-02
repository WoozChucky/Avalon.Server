using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace Avalon.Api.Previews;

/// <summary>
/// The public site's base URL (<c>Application:PublicSiteUrl</c>), or none. Unset is allowed: previews then
/// leave out <c>og:url</c> and link relatively.
/// </summary>
public sealed class PublicSiteSettings
{
    private PublicSiteSettings(string? baseUrl) => Base = baseUrl;

    /// <summary>An absolute http(s) URL without a trailing slash, or null.</summary>
    public string? Base { get; }

    /// <summary>
    /// Null or blank is unset. Otherwise an absolute http or https URL with no query or fragment; a trailing
    /// slash is dropped. Anything else is refused at startup, naming the setting.
    /// </summary>
    public static PublicSiteSettings Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new PublicSiteSettings(null);
        string trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || trimmed.Contains('?', StringComparison.Ordinal)
            || trimmed.Contains('#', StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Application:PublicSiteUrl must be an absolute http or https URL without a query or fragment, e.g. https://avalon.example.");
        return new PublicSiteSettings(trimmed);
    }
}

/// <summary>
/// The tiny HTML document a link-preview bot reads: Open Graph and Twitter tags and one link, no scripts.
/// Every value comes from game data or configuration and is HTML-encoded.
/// </summary>
public static class LinkPreviewPage
{
    public const string ContentType = "text/html; charset=utf-8";

    /// <param name="path">The page's path on the public site, <c>/item/14</c> or <c>/item/14?world=2</c>.</param>
    /// <param name="publicBase">The public site's base URL, or null: no <c>og:url</c>, and the link is relative.</param>
    public static string Render(string name, string description, string path, string? publicBase,
        string? themeColour, string? siteName)
    {
        static string E(string text) => HtmlEncoder.Default.Encode(LinkPreviewText.OneLine(text));
        string n = E(name);
        string d = E(description);
        string href = E(publicBase is null ? path : publicBase + path);

        StringBuilder html = new();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append(CultureInfo.InvariantCulture, $"<title>{n}</title>\n");
        html.Append(CultureInfo.InvariantCulture, $"<meta name=\"description\" content=\"{d}\">\n");
        if (siteName is not null)
            html.Append(CultureInfo.InvariantCulture, $"<meta property=\"og:site_name\" content=\"{E(siteName)}\">\n");
        html.Append("<meta property=\"og:type\" content=\"website\">\n");
        html.Append(CultureInfo.InvariantCulture, $"<meta property=\"og:title\" content=\"{n}\">\n");
        html.Append(CultureInfo.InvariantCulture, $"<meta property=\"og:description\" content=\"{d}\">\n");
        if (publicBase is not null)
            html.Append(CultureInfo.InvariantCulture, $"<meta property=\"og:url\" content=\"{href}\">\n");
        if (themeColour is not null)
            html.Append(CultureInfo.InvariantCulture, $"<meta name=\"theme-color\" content=\"{E(themeColour)}\">\n");
        html.Append("<meta name=\"twitter:card\" content=\"summary\">\n</head>\n");
        html.Append(CultureInfo.InvariantCulture, $"<body><a href=\"{href}\">{n}</a></body>\n</html>\n");
        return html.ToString();
    }

    public static string NotFound(string? siteName) => Plain("Not found", siteName);

    public static string Unavailable(string? siteName) => Plain("Unavailable", siteName);

    private static string Plain(string what, string? siteName)
    {
        string t = HtmlEncoder.Default.Encode(siteName is null ? what : $"{what} · {siteName}");
        return $"<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>{t}</title>\n</head>\n<body>{t}</body>\n</html>\n";
    }
}
