using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// What a catalog modifier is (#1179): a prefix or a suffix a copy rolled, a unique's own modifier, or a base's implicit
/// stats. Only prefixes and suffixes name an item.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemModifierKind
{
    Prefix = 1,
    Suffix = 2,
    Unique = 3,
    Implicit = 4,
}
