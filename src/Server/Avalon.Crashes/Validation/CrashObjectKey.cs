using System.Globalization;

namespace Avalon.Crashes.Validation;

public sealed record CrashObjectKey(string Version, DateOnly Date, Guid Id)
{
    public static bool IsVersion(string version) => version.Length is > 0 and <= 64
        && version is not "." and not ".."
        && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+');

    public static bool TryParse(string key, out CrashObjectKey? parsed)
    {
        parsed = null;
        string[] parts = key.Split('/');
        if (parts.Length != 3 || !IsVersion(parts[0])
            || !DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            || parts[2].Length != 36 || !parts[2].EndsWith(".zip", StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[2].AsSpan(0, 32), "N", out Guid id))
        {
            return false;
        }
        parsed = new CrashObjectKey(parts[0], date, id);
        return true;
    }
}
