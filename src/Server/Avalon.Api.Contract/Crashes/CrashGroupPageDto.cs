namespace Avalon.Api.Contract.Crashes;

public sealed record CrashGroupPageDto(
    IReadOnlyList<CrashGroupDto> Items, int Page, int PageSize, long Total,
    DateTimeOffset AsOf, int RetentionDays, DateTimeOffset EffectiveSince);
