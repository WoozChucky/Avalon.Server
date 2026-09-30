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
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static ScenarioFile ParseScenarios(string json) => Parse<ScenarioFile>(json, "scenarios");

    public static TargetFile ParseTargets(string json) => Parse<TargetFile>(json, "targets");

    public static RotationFile ParseRotations(string json) => Parse<RotationFile>(json, "rotations");

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
