using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

/// <summary>Which rows to run. A null set keeps everything; a filter only drops rows, never changes the rest.</summary>
public sealed record RunFilter(
    IReadOnlySet<CharacterClass>? Classes,
    IReadOnlySet<ushort>? Levels,
    IReadOnlySet<string>? Gear,
    IReadOnlySet<string>? Scenarios)
{
    public static readonly RunFilter None = new(null, null, null, null);
}
