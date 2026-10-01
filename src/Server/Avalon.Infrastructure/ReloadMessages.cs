using System.Text.Json;

namespace Avalon.Infrastructure;

/// <summary>Asks a world to reload areas of its static data. Areas are <c>ReloadArea</c> names, e.g. "Items".</summary>
public sealed record ReloadRequestMessage(Guid RequestId, string[] Areas);

/// <summary>One area's outcome in a world's answer.</summary>
public sealed record ReloadOutcomeMessage(string Area, bool Succeeded, string Summary);

/// <summary>A world's answer to the <see cref="ReloadRequestMessage"/> with the same <see cref="RequestId"/>.</summary>
public sealed record ReloadResultMessage(Guid RequestId, ReloadOutcomeMessage[] Outcomes);

/// <summary>The one serializer setting both ends of the reload channels use: camelCase JSON.</summary>
public static class ReloadMessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);
}
