using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>Where a character's held quest stands (#433, #714): the stored numbers, so values are only ever appended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CharacterQuestState
{
    Active = 1,
    ReadyToTurnIn = 2,
}
