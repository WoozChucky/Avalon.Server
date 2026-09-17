// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.Api.Services;

public class ProceduralLayoutInputsResolver : IProceduralLayoutInputsResolver
{
    private readonly IChunkPoolRepository _pools;
    private readonly IChunkTemplateRepository _chunks;

    public ProceduralLayoutInputsResolver(IChunkPoolRepository pools, IChunkTemplateRepository chunks)
    {
        _pools = pools;
        _chunks = chunks;
    }

    public async Task<ChunkPool?> FindPoolAsync(ChunkPoolId poolId, CancellationToken ct)
    {
        IReadOnlyList<ChunkPool> pools = await _pools.FindAllWithMembershipsAsync(ct);
        return pools.FirstOrDefault(p => p.Id == poolId);
    }

    public async Task<ProceduralPoolResolution> ResolveMembersAsync(ChunkPool pool, CancellationToken ct)
    {
        IReadOnlyList<ChunkTemplate> templates = await _chunks.FindAllWithSlotsAsync(ct);
        Dictionary<ChunkTemplateId, ChunkTemplate> byId = templates.ToDictionary(t => t.Id);

        List<ChunkPoolMember> members = pool.Memberships
            .Where(m => byId.ContainsKey(m.ChunkTemplateId))
            .Select(m => new ChunkPoolMember(byId[m.ChunkTemplateId], m.Weight))
            .ToList();

        return new ProceduralPoolResolution(members, byId);
    }
}
