namespace Avalon.Api.Distribution;

/// <summary>
/// One change in a changelog entry: its kind (<c>new</c>, <c>improved</c>, <c>fixed</c>, <c>changed</c>,
/// <c>internal</c> or <c>dependencies</c>), the pull request's player note, and the pull request itself
/// for the public server repository only.
/// </summary>
public sealed record ChangelogItemDto(string Kind, string Text, bool Breaking, int? Pr, Uri? PrUrl);

/// <summary>
/// A release's changelog entry (homelab spec 2026-09-27-avalon-changelog-design §4), as the website shows
/// it: the released commit is left out.
/// </summary>
public sealed record ChangelogEntryDto(
    string Product,
    string? Channel,
    string Version,
    string? Build,
    DateTimeOffset PublishedAt,
    Uri? ReleaseUrl,
    IReadOnlyList<ChangelogItemDto> Items)
{
    /// <summary>
    /// The entry's key under <c>changelog/</c> without <c>.json</c> (e.g. <c>client/ptr/0.1.0+8.9078bc8</c>):
    /// stable and unique, the tie-break when paging entries published at the same time.
    /// </summary>
    public string Id { get; init; } = "";
}
