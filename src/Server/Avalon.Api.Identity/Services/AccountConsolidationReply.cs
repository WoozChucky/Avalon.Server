// The published OpenAPI document names this schema by the type's full name, so the type keeps the namespace it had
// before the split (#794).
namespace Avalon.Api.Services;

public sealed record AccountConsolidationReply(string State, string? Error = null)
{
    public string? OperationId { get; init; }
    public int WorldsCompleted { get; init; }
    public int WorldsTotal { get; init; }
    public long TransferredCharacters { get; init; }
}
