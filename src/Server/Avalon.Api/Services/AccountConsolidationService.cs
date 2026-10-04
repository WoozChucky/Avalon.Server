using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;

namespace Avalon.Api.Services;

public sealed record AccountConsolidationReply(string State, string? Error = null)
{
    public string? OperationId { get; init; }
    public int WorldsCompleted { get; init; }
    public int WorldsTotal { get; init; }
    public long TransferredCharacters { get; init; }
}

/// <summary>Each world publishes its own receipt; Auth-DB finalization happens only after all commits.</summary>
public sealed class AccountConsolidationService(IAccountConsolidationRepository operations, IWorldRepository registry,
    IWorldDatabases configured, IWorldRepositories worlds)
{
    public async Task<AccountConsolidationReply> BeginAsync(AccountConsolidationRequest request, CancellationToken cancellationToken)
    {
        var manifest = await ManifestAsync(cancellationToken);
        if (manifest.Error is not null) return new("pending", manifest.Error);
        var result = await operations.BeginAsync(request with { Worlds = manifest.Worlds! }, cancellationToken);
        if (result.Error is not null || result.Operation is null) return new("pending", result.Error ?? "INVALID_CONSOLIDATION");
        return await ResumeAsync(result.Operation.Id, request.TargetAccountId, cancellationToken);
    }
    public async Task<AccountConsolidationReply> StatusAsync(Guid operationId, AccountId caller, CancellationToken cancellationToken)
    {
        var operation = await operations.FindAsync(operationId, cancellationToken);
        return operation is null || operation.TargetAccountId != caller ? new("pending", "INVALID_CONSOLIDATION") : Reply(operation);
    }
    public async Task<AccountConsolidationReply> ResumeAsync(Guid operationId, AccountId caller, CancellationToken cancellationToken)
    {
        var operation = await operations.FindAsync(operationId, cancellationToken);
        if (operation is null || operation.TargetAccountId != caller) return new("pending", "INVALID_CONSOLIDATION");
        if (operation.State == AccountConsolidationState.Completed) return Reply(operation);
        var manifest = await ManifestAsync(cancellationToken);
        if (manifest.Error is not null) return Reply(operation, manifest.Error);
        if (!operation.Worlds.Select(w => w.WorldId).Order().SequenceEqual(manifest.Worlds!.Select(w => w.Value).Order()))
            return Reply(operation, "WORLD_CONFIG_INCOMPLETE");
        if (operation.State == AccountConsolidationState.Transferring)
        {
            // Freeze and drain every world first, including worlds whose transfer may already have committed.
            foreach (var world in operation.Worlds.OrderBy(w => w.WorldId))
                if (!await worlds.CharacterConsolidations(new(world.WorldId)).PrepareAsync(operation.Id, operation.SourceAccountId, operation.TargetAccountId, cancellationToken))
                    return Reply(operation, "WORLD_BARRIER_PENDING");
            foreach (var world in operation.Worlds.Where(w => w.TransferredAt is null).OrderBy(w => w.WorldId))
            {
                var transfer = await worlds.CharacterConsolidations(new(world.WorldId)).TransferAsync(operation.Id, operation.SourceAccountId, operation.TargetAccountId, cancellationToken);
                if (transfer.Error == "WAITING_FOR_SESSION") return Reply(operation, state: "waiting_for_sessions");
                if (transfer.Error is not null) return Reply(operation, transfer.Error);
                if (!await operations.RecordTransferAsync(operation.Id, new(world.WorldId), transfer.TransferredCharacters, cancellationToken))
                    return Reply(operation, "SERVICE_UNAVAILABLE");
            }
            if (!await operations.FinalizeAsync(operation.Id, cancellationToken)) return Reply(operation, "FINALIZATION_PENDING");
            operation = (await operations.FindAsync(operationId, cancellationToken))!;
        }
        if (operation.State == AccountConsolidationState.Completed) return Reply(operation);
        // Finalization already moved the Steam link and retired the source. Releasing the target guard never mints authority.
        foreach (var world in operation.Worlds.Where(w => w.GuardReleasedAt is null).OrderBy(w => w.WorldId))
        {
            if (!await worlds.CharacterConsolidations(new(world.WorldId)).ReleaseTargetAsync(operation.Id, operation.SourceAccountId, operation.TargetAccountId, cancellationToken) ||
                !await operations.RecordGuardReleasedAsync(operation.Id, new(world.WorldId), cancellationToken)) return Reply(operation, "WORLD_BARRIER_PENDING");
        }
        if (!await operations.CompleteAsync(operation.Id, cancellationToken)) return Reply(operation, "FINALIZATION_PENDING");
        return Reply((await operations.FindAsync(operationId, cancellationToken))!);
    }
    private async Task<(IReadOnlyList<WorldId>? Worlds, string? Error)> ManifestAsync(CancellationToken cancellationToken)
    {
        var registered = await registry.FindAllAsync(false, cancellationToken);
        if (registered.Count == 0 || configured.All.Count == 0 || registered.Any(w => !configured.TryGet(w.Id, out _)))
            return (null, "WORLD_CONFIG_INCOMPLETE");
        // Configured historical worlds are included even when their registry listing was removed.
        if (configured.All.Any(w => !configured.IsAvailable(w.Id))) return (null, "WORLD_UNAVAILABLE");
        return (configured.All.Select(w => w.Id).OrderBy(w => w.Value).ToArray(), null);
    }
    private static AccountConsolidationReply Reply(AccountConsolidation operation, string? error = null, string? state = null) => new(
        state ?? (operation.State switch { AccountConsolidationState.Completed => "completed", AccountConsolidationState.Finalized => "finishing", _ => "transferring" }), error)
    {
        OperationId = operation.Id.ToString("N"), WorldsCompleted = operation.Worlds.Count(w => w.TransferredAt is not null),
        WorldsTotal = operation.Worlds.Count, TransferredCharacters = operation.Worlds.Sum(w => (long)w.TransferredCharacters),
    };
}
