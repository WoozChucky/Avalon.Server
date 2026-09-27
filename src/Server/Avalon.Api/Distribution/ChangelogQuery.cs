namespace Avalon.Api.Distribution;

/// <summary>
/// What part of the changelog to list: a product (<c>server</c>, <c>client</c>, <c>launcher</c>) or all,
/// a client channel or all the caller may use, at most <paramref name="Limit" /> entries, strictly older
/// than <paramref name="Before" /> when paging.
/// </summary>
public sealed record ChangelogQuery(string? Product, Channel? Channel, int Limit, DateTimeOffset? Before);
