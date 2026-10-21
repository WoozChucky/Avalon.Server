using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// How often a repeatable quest can be done again (#710): the stored numbers, so values are only ever appended. There is
/// no None: null stands for none.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestRepeatFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
}
