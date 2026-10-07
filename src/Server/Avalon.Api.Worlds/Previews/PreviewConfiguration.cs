using System.Text.RegularExpressions;
using Avalon.Api.Contract;

namespace Avalon.Api.Worlds.Previews;

/// <summary>
/// <c>Application:Previews</c>: how a link preview looks. The defaults are in appsettings.json. The
/// rarity colours mirror Avalon.Dashboard apps/public/src/components/game/rarity.ts (Tailwind colours; Junk
/// and Common are the site's dark-theme muted-foreground and foreground), and the ability colour is the
/// site's primary accent (text-primary, the tooltip's name colour); change them together.
/// A missing or malformed colour leaves the page without a <c>theme-color</c>; it never fails a request.
/// </summary>
public sealed partial class PreviewConfiguration
{
    /// <summary>Named as <c>og:site_name</c> and in the not-found title; left out when empty.</summary>
    public string? SiteName { get; set; }

    /// <summary>A <c>#RRGGBB</c> colour for abilities, which have no rarity.</summary>
    public string? AbilityColour { get; set; }

    /// <summary>An <see cref="ItemRarity"/> name (any case) to a <c>#RRGGBB</c> colour.</summary>
    public Dictionary<string, string> RarityColours { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ColourOf(ItemRarity rarity) =>
        RarityColours.TryGetValue(rarity.ToString(), out string? colour) ? Valid(colour) : null;

    public string? AbilityTheme => Valid(AbilityColour);

    public string? Site => string.IsNullOrWhiteSpace(SiteName) ? null : SiteName.Trim();

    private static string? Valid(string? colour) =>
        colour is not null && HexColour().IsMatch(colour) ? colour : null;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColour();
}
