using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Balance.Contract;

/// <summary>The one set of JSON options the balance service and the API share: camelCase, string enums, explicit nulls.</summary>
public static class BalanceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
