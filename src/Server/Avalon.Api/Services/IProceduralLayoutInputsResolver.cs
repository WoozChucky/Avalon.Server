// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.Api.Services;

/// <summary>
/// Resolves the chunk pool and pool-member list a <see cref="ProceduralMapConfig"/> points at.
///
/// Shared by <see cref="MapService.PreviewLayoutAsync"/> (an admin-triggered layout preview)
/// and <see cref="ObservabilityService"/>'s layout-staleness check (a comparison run on a path
/// the admin dashboard polls every 1.5s) so that a future change to how pool members resolve —
/// e.g. how a missing chunk template is excluded, or how weight is read — lands in exactly one
/// place instead of drifting between two call sites that both feed
/// <see cref="LayoutConfigVersion.Compute"/>. That drift is exactly the failure mode
/// LayoutStale exists to catch, so the resolution logic itself must not risk it.
///
/// Returns null on a missing pool rather than throwing: each caller decides how to surface
/// that. <see cref="MapService"/> throws a <c>BusinessException</c> with an actionable message;
/// <see cref="ObservabilityService"/> treats it as "cannot determine — report not stale."
/// </summary>
public interface IProceduralLayoutInputsResolver
{
    Task<ChunkPool?> FindPoolAsync(ChunkPoolId poolId, CancellationToken ct);
    Task<ProceduralPoolResolution> ResolveMembersAsync(ChunkPool pool, CancellationToken ct);
}
