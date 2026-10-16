using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>How busy a public realm is, in place of a head count.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PublicPopulation
{
    Low,
    Medium,
    High,

    /// <summary>The world has reached its player limit.</summary>
    Full
}
