using System.Globalization;

namespace Avalon.Common.Mathematics;

public static class AvalonString
{
    internal static string Format(string fmt, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture.NumberFormat, fmt, args);
}
