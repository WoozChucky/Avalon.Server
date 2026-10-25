namespace Avalon.Crashes.Analysis;

public static class CrashVersion
{
    public static int? Compare(string left, string right)
    {
        if (!TryParse(left, out string[] leftCore, out string[] leftPre) || !TryParse(right, out string[] rightCore, out string[] rightPre))
            return null;

        for (int i = 0; i < 3; i++)
        {
            int result = CompareNumber(leftCore[i], rightCore[i]);
            if (result != 0)
                return result;
        }
        if (leftPre.Length == 0 || rightPre.Length == 0)
            return leftPre.Length == rightPre.Length ? 0 : leftPre.Length == 0 ? 1 : -1;

        for (int i = 0; i < Math.Min(leftPre.Length, rightPre.Length); i++)
        {
            bool leftNumeric = IsNumber(leftPre[i]);
            bool rightNumeric = IsNumber(rightPre[i]);
            int result = leftNumeric && rightNumeric ? CompareNumber(leftPre[i], rightPre[i])
                : leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1)
                : Math.Sign(string.Compare(leftPre[i], rightPre[i], StringComparison.Ordinal));
            if (result != 0)
                return result;
        }
        return Math.Sign(leftPre.Length.CompareTo(rightPre.Length));
    }

    private static bool TryParse(string value, out string[] core, out string[] prerelease)
    {
        core = [];
        prerelease = [];
        if (string.IsNullOrEmpty(value))
            return false;
        string[] buildParts = value.Split('+');
        if (buildParts.Length > 2 || (buildParts.Length == 2 && !ValidIdentifiers(buildParts[1], false)))
            return false;
        int separator = buildParts[0].IndexOf('-');
        string main = separator < 0 ? buildParts[0] : buildParts[0][..separator];
        core = main.Split('.');
        if (core.Length != 3 || core.Any(p => !IsNumber(p) || HasLeadingZero(p)))
            return false;
        if (separator < 0)
            return true;
        string pre = buildParts[0][(separator + 1)..];
        if (!ValidIdentifiers(pre, true))
            return false;
        prerelease = pre.Split('.');
        return true;
    }

    private static bool ValidIdentifiers(string value, bool prerelease) => value.Split('.').All(p =>
        p.Length > 0 && p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && (!prerelease || !IsNumber(p) || !HasLeadingZero(p)));

    private static bool IsNumber(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);
    private static bool HasLeadingZero(string value) => value.Length > 1 && value[0] == '0';
    private static int CompareNumber(string left, string right) => left.Length == right.Length
        ? Math.Sign(string.Compare(left, right, StringComparison.Ordinal)) : Math.Sign(left.Length.CompareTo(right.Length));
}
