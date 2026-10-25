using System.Text.Json;

namespace Avalon.Database.Crashes.Configuration;

internal static class CrashEnumStorage
{
    internal static string Write<T>(T value) where T : struct, Enum => JsonSerializer.Serialize(value).Trim('"');
    internal static T Read<T>(string value) where T : struct, Enum => JsonSerializer.Deserialize<T>('"' + value + '"');
}
