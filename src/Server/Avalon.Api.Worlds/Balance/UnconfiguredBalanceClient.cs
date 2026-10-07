using Avalon.Balance.Contract;

namespace Avalon.Api.Worlds.Balance;

/// <summary>
/// The client when <c>Application:Balance</c> is not set: every call is "not configured", which the API
/// answers with 503, so the rest of the API runs without the service.
/// </summary>
public sealed class UnconfiguredBalanceClient : IBalanceClient
{
    private static BalanceUnavailableException Unavailable() => new("balance service not configured");

    public Task<BalanceResponse<CatalogDto>> CatalogAsync(CancellationToken ct) => throw Unavailable();

    public Task<BalanceResponse<RunAcceptedDto>> StartRunAsync(RunRequestDto request, CancellationToken ct) => throw Unavailable();

    public Task<BalanceResponse<RunStatusDto>> GetRunAsync(string id, CancellationToken ct) => throw Unavailable();

    public Task<BalanceResponse> CancelRunAsync(string id, CancellationToken ct) => throw Unavailable();

    public Task<BalanceResponse<ExportResultDto>> ExportAsync(ExportRequestDto request, CancellationToken ct) => throw Unavailable();
}
