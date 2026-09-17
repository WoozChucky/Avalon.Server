// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.Api.Services;

/// <summary>
/// The chunk pool a <see cref="ProceduralMapConfig"/> points at, plus the pool members
/// (chunk templates + weights) resolved against every currently known chunk template, and
/// the full template-by-id lookup those members were built from (kept around so callers that
/// need to resolve *other* template ids from the same layout — e.g. mapping a generated
/// layout back to DTOs — don't have to re-read the chunk template table).
/// </summary>
public sealed record ProceduralPoolResolution(
    IReadOnlyList<ChunkPoolMember> Members,
    IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> TemplatesById);
