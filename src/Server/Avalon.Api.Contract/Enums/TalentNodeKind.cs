using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>What a talent node is (#1181): the stored numbers, cast from the World's by ordinal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TalentNodeKind
{
    /// <summary>A class's start: free, never allocated, and linked to the class's starter ability.</summary>
    Start = 1,
    Small = 2,
    Notable = 3,
    /// <summary>Unlocks the ability its link names.</summary>
    Ability = 4,
    Capstone = 5,
    Keystone = 6,
    /// <summary>A hub junction, where a class's spoke meets the hub ring.</summary>
    Junction = 7,
}
