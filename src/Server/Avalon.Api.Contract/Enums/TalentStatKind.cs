using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>How a talent node's stat line counts (#1181): the stored numbers, cast from the World's by ordinal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TalentStatKind
{
    /// <summary>Added to the gear's lines of the same stat.</summary>
    Added = 1,
    /// <summary>A multiplier of its own: "N % more".</summary>
    More = 2,
}
