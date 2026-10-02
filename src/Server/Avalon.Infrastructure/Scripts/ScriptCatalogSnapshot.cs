using System.Text.Json;

namespace Avalon.Infrastructure.Scripts;

/// <summary>
/// The script names one world accepts in a template's <c>ScriptName</c>, as the world publishes them under
/// <see cref="CacheKeys.WorldScriptCatalog"/>. Written by Avalon.Server.World's <c>ScriptCatalogPublisher</c> after its
/// scripts load and after a hot reload, read by Avalon.Api's <c>WorldScriptCatalog</c>. Each list is sorted.
/// </summary>
/// <param name="Ai">Creature AI scripts, <c>[ChainedScript]</c> types excluded: the names a creature template may use.</param>
/// <param name="Ability">Ability scripts: the names an ability template may use.</param>
/// <param name="Quest">Quest scripts: the names a quest may use.</param>
/// <param name="Item">Item scripts (item use): the names an item template's UseScript may use. Null in a value written
/// by a world built before item use, whose item saves are then left unchecked.</param>
public sealed record ScriptCatalogSnapshot(
    IReadOnlyList<string> Ai,
    IReadOnlyList<string> Ability,
    IReadOnlyList<string> Quest,
    IReadOnlyList<string>? Item = null);

/// <summary>The JSON form of a <see cref="ScriptCatalogSnapshot"/>: <c>{ "ai": [], "ability": [], "quest": [], "item": [] }</c>.</summary>
public static class ScriptCatalogJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(ScriptCatalogSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    /// <summary>
    /// Null for anything that is not a whole catalog: malformed JSON, or a list missing. A value written by a
    /// different build reads as "not published", the same as an absent key.
    /// </summary>
    public static ScriptCatalogSnapshot? Deserialize(string json)
    {
        try
        {
            ScriptCatalogSnapshot? snapshot = JsonSerializer.Deserialize<ScriptCatalogSnapshot>(json, Options);
            return snapshot is { Ai: not null, Ability: not null, Quest: not null } ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
