using System.Globalization;
using System.Text;

namespace Avalon.World.Threading;

/// <summary>
/// The Linux CPU list format (#1002), as <c>/sys</c> and <c>/proc</c> write it and as <c>taskset -c</c> and
/// <c>DOTNET_GCHeapAffinitizeRanges</c> read it: CPU numbers and inclusive ranges, comma-separated (<c>0-7,9-19,21-23</c>).
/// </summary>
public static class CpuList
{
    /// <summary>The CPUs as a list, runs of consecutive numbers as ranges; empty for none.</summary>
    public static string Format(IEnumerable<int> cpus)
    {
        var text = new StringBuilder();
        int[] sorted = cpus.Distinct().Order().ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            int first = sorted[i];
            while (i + 1 < sorted.Length && sorted[i + 1] == sorted[i] + 1)
                i++;

            if (text.Length > 0)
                text.Append(',');

            text.Append(first.ToString(CultureInfo.InvariantCulture));
            if (sorted[i] != first)
                text.Append('-').Append(sorted[i].ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>Reads a list (<c>8,20</c>, <c>0-5,12-17</c>); false when any part is not a number or a range.</summary>
    public static bool TryParse(string text, out IReadOnlySet<int> cpus)
    {
        var parsed = new SortedSet<int>();
        cpus = parsed;
        foreach (string part in text.Trim().Split(',', StringSplitOptions.TrimEntries))
        {
            int dash = part.IndexOf('-', StringComparison.Ordinal);
            string low = dash < 0 ? part : part[..dash];
            string high = dash < 0 ? part : part[(dash + 1)..];
            if (!int.TryParse(low, NumberStyles.None, CultureInfo.InvariantCulture, out int first)
                || !int.TryParse(high, NumberStyles.None, CultureInfo.InvariantCulture, out int last)
                || last < first)
            {
                return false;
            }

            for (int cpu = first; cpu <= last; cpu++)
                parsed.Add(cpu);
        }

        return true;
    }
}
