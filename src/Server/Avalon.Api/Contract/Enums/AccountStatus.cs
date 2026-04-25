using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountStatus : byte
{
    Active = 0,
    Banned = 1,
    Deactivated = 2
}
