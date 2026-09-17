// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Api.Contract;

public class PresencePaginateFilters
{
    public int Page { get; set; } = 1;

    /// <summary>Clamped to [1, 50] by <c>ObservabilityService</c>, matching the house pattern
    /// of the sibling paginate filters — never rejected at the model-binding boundary.</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Case-insensitive substring match on character name.</summary>
    public string? NameLike { get; set; }

    /// <summary>Restrict to one world.</summary>
    public ushort? WorldId { get; set; }

    /// <summary>Restrict to one map template.</summary>
    public ushort? TemplateId { get; set; }
}
