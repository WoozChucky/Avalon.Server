using Avalon.Common;

namespace Avalon.Balance.Core;

/// <summary>
/// Which rows to run. A null set keeps everything; a filter only drops rows, never changes the rest. Classes keeps only
/// the builds of those classes (B-R18), and Builds only the builds named.
/// </summary>
public sealed record RunFilter(
    IReadOnlySet<CharacterClass>? Classes,
    IReadOnlySet<string>? Builds,
    IReadOnlySet<ushort>? Levels,
    IReadOnlySet<string>? Gear,
    IReadOnlySet<string>? Scenarios)
{
    public static readonly RunFilter None = new(null, null, null, null, null);
}
