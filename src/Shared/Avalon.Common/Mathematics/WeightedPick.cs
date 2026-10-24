namespace Avalon.Common.Mathematics;

/// <summary>A pick among weighted choices from one uniform draw, so whoever draws the number decides the outcome.</summary>
public static class WeightedPick
{
    /// <summary>
    /// The index a <paramref name="draw" /> in [0, 1) lands on: the first whose running total of
    /// <paramref name="weights" /> exceeds the draw times their total, so each index is picked with its weight over the
    /// total. A weight of 0 or below is never picked. A draw that rounds past the total lands on the last weighted index;
    /// -1 when no weight is above 0.
    /// </summary>
    public static int Index(ReadOnlySpan<int> weights, double draw)
    {
        long total = 0;
        foreach (int weight in weights)
        {
            if (weight > 0)
                total += weight;
        }

        double target = draw * total;
        long running = 0;
        int last = -1;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0)
                continue;

            running += weights[i];
            last = i;
            if (running > target)
                return i;
        }

        return last;
    }
}
