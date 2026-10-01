using System.Globalization;
using System.Text.Encodings.Web;

namespace Avalon.Api.Previews;

/// <summary>The public site's base URL (Application:PublicSiteUrl), without a trailing slash.</summary>
public sealed record PublicSiteSettings(string BaseUrl)
{
    public string Base { get; } = BaseUrl.TrimEnd('/');
}

/// <summary>
/// The tiny HTML document a link-preview bot reads: Open Graph and Twitter tags and one link, no scripts.
/// Every value comes from game data or configuration and is HTML-encoded.
/// </summary>
public static class LinkPreviewPage
{
    public const string ContentType = "text/html; charset=utf-8";

    public static string Render(string name, string description, string url, string themeColour)
    {
        string n = HtmlEncoder.Default.Encode(LinkPreviewText.OneLine(name));
        string d = HtmlEncoder.Default.Encode(LinkPreviewText.OneLine(description));
        string u = HtmlEncoder.Default.Encode(url);
        string c = HtmlEncoder.Default.Encode(themeColour);
        return string.Create(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>{n}</title>
            <meta name="description" content="{d}">
            <meta property="og:site_name" content="Avalon">
            <meta property="og:type" content="website">
            <meta property="og:title" content="{n}">
            <meta property="og:description" content="{d}">
            <meta property="og:url" content="{u}">
            <meta name="theme-color" content="{c}">
            <meta name="twitter:card" content="summary">
            </head>
            <body><a href="{u}">{n}</a></body>
            </html>

            """);
    }

    public static string NotFound() => Plain("Not found · Avalon");

    public static string Unavailable() => Plain("Unavailable · Avalon");

    private static string Plain(string title)
    {
        string t = HtmlEncoder.Default.Encode(title);
        return $"<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>{t}</title>\n</head>\n<body>{t}</body>\n</html>\n";
    }
}
