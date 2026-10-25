using System.Globalization;
using System.Text.Json;

namespace Avalon.Crashes.Validation;

internal static class CrashMetadataReader
{
    internal static CrashMetadata Read(byte[] bytes, string expectedVersion)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid();
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!fields.TryAdd(property.Name, property.Value))
                    throw Invalid();
            }
            int schema = Required(fields, "schema").GetInt32();
            if (schema != 1)
                throw new CrashInputException(CrashFailureCode.UnsupportedSchema);
            string exe = Text(fields, "exe");
            string version = Text(fields, "version");
            string gitHash = Text(fields, "gitHash");
            if (!string.Equals(exe, "runtime", StringComparison.Ordinal)
                || version.Length is 0 or > 64
                || !version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')
                || !string.Equals(version, expectedVersion, StringComparison.Ordinal)
                || gitHash.Length is < 7 or > 64
                || !gitHash.All(char.IsAsciiHexDigit))
            {
                throw Invalid();
            }
            uint code = Required(fields, "exceptionCode").GetUInt32();
            uint thread = Required(fields, "threadId").GetUInt32();
            string address = Text(fields, "exceptionAddress");
            ReadOnlySpan<char> hex = address.AsSpan();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];
            if (hex.Length is 0 or > 16 || !ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong numeric))
                throw Invalid();
            string time = Text(fields, "time");
            if (!DateTimeOffset.TryParseExact(time, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset timestamp))
                throw Invalid();
            return new CrashMetadata(schema, exe, version, gitHash.ToLowerInvariant(), code, "0x" + numeric.ToString("x16", CultureInfo.InvariantCulture), thread, timestamp);
        }
        catch (JsonException)
        {
            throw Invalid();
        }
        catch (InvalidOperationException)
        {
            throw Invalid();
        }
        catch (FormatException)
        {
            throw Invalid();
        }
    }

    private static JsonElement Required(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out JsonElement value))
            throw Invalid();
        return value;
    }

    private static string Text(Dictionary<string, JsonElement> fields, string name) => Required(fields, name).GetString() ?? throw Invalid();
    private static CrashInputException Invalid() => new(CrashFailureCode.InvalidMetadata);
}
