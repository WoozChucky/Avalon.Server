namespace Avalon.Api.Distribution;

/// <summary>
/// What part of the changelog to list: a product (<c>server</c>, <c>client</c>, <c>launcher</c>) or all,
/// a client channel or all the caller may use, at most <paramref name="Limit" /> entries, strictly older
/// than <paramref name="Before" /> when paging, or published at <paramref name="Before" /> with an id before
/// <paramref name="BeforeId" /> (entries of one release job can share a second).
/// </summary>
public sealed record ChangelogQuery(string? Product, Channel? Channel, int Limit, DateTimeOffset? Before, string? BeforeId = null);
