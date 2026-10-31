using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Balance.Core;

public static class ConfigFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // A null where the file's type has no null ("builds": null, "pack": [null]) is a bad file, not a later crash.
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private static readonly JsonSerializerOptions s_canonical = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        // The output is files and JSON, never embedded in HTML: keep ">=2" readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The three files as canonical JSON: 2-space indent, camelCase, string enums, a trailing newline.</summary>
    public static (string Scenarios, string Targets, string Builds) Save(BalanceConfig config) =>
        (Write(config.Scenarios), Write(config.Targets), Write(config.Builds));

    private static string Write<T>(T file) => JsonSerializer.Serialize(file, s_canonical) + "\n";

    public static ScenarioFile ParseScenarios(string json) => Parse<ScenarioFile>(json, "scenarios");

    public static TargetFile ParseTargets(string json) => Parse<TargetFile>(json, "targets");

    public static BuildFile ParseBuilds(string json) => Parse<BuildFile>(json, "builds");

    private static T Parse<T>(string json, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException($"{name}: the file is empty");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{name}: {e.Message}", e);
        }
    }
}
