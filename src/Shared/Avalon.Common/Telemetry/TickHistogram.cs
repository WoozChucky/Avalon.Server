// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Text;

namespace Avalon.Common.Telemetry;

// Add a simple fixed-bucket histogram — no allocations, lock-free reads.
public sealed class TickHistogram
{
    // Buckets in microseconds: 0-100, 100-250, 250-500, 500-1k, 1-2k, 2-4k,
    // 4-8k, 8-16k, 16-20k, 20-33k, 33k+
    private static readonly long[] BoundsUs =
        { 100, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000, 20_000, 33_000, long.MaxValue };
    private readonly long[] _counts = new long[BoundsUs.Length];
    public long Max;
    public long Sum;
    public long N;

    public void Record(long us)
    {
        for (int i = 0; i < BoundsUs.Length; i++)
            if (us < BoundsUs[i]) { Interlocked.Increment(ref _counts[i]); break; }
        Interlocked.Add(ref Sum, us);
        Interlocked.Increment(ref N);
        long m;
        do { m = Volatile.Read(ref Max); if (us <= m) break; }
        while (Interlocked.CompareExchange(ref Max, us, m) != m);
    }

    public string Snapshot()
    {
        var sb = new StringBuilder();
        long n = Volatile.Read(ref N);
        sb.Append($"n={n} avg={(n > 0 ? Sum / n : 0)}us max={Max}us | ");
        for (int i = 0; i < BoundsUs.Length; i++)
            sb.Append($"<{BoundsUs[i]}us:{_counts[i]} ");
        return sb.ToString();
    }
}
