using System.Globalization;

namespace Avalon.Common.Mathematics;

public class AvalonString
{
    internal static string Format(string fmt, params object[] args)
    {
        return string.Format((IFormatProvider) CultureInfo.InvariantCulture.NumberFormat, fmt, args);
    }
}
