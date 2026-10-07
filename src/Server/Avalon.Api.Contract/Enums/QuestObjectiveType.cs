using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>What a quest objective counts (#433, #714): the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestObjectiveType
{
    Kill = 1,
    Collect = 2,
    Talk = 3,
    Scripted = 4,
}
