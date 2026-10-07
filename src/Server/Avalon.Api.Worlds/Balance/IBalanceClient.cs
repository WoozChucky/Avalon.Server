using Avalon.Balance.Contract;

namespace Avalon.Api.Worlds.Balance;

/// <summary>
/// What the balance service answered: its status code and, when the body was JSON, that body exactly
/// as received, so a 404, 422, 429, 502 or 503 reaches the caller unchanged.
/// </summary>
public record BalanceResponse(int Status, string? Json)
{
    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>A response whose success body is <typeparamref name="T" />; <see cref="Value" /> is null otherwise.</summary>
public sealed record BalanceResponse<T>(int Status, T? Value, string? Json) : BalanceResponse(Status, Json);

public interface IBalanceClient
{
    Task<BalanceResponse<CatalogDto>> CatalogAsync(CancellationToken ct);

    Task<BalanceResponse<RunAcceptedDto>> StartRunAsync(RunRequestDto request, CancellationToken ct);

    Task<BalanceResponse<RunStatusDto>> GetRunAsync(string id, CancellationToken ct);

    Task<BalanceResponse> CancelRunAsync(string id, CancellationToken ct);

    Task<BalanceResponse<ExportResultDto>> ExportAsync(ExportRequestDto request, CancellationToken ct);
}
