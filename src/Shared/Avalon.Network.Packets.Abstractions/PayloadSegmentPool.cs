namespace Avalon.Network.Packets.Abstractions;

/// <summary>
/// Free payload segments by size class, powers of two from 64 B to 1 MiB (#875). Unbounded on purpose: it holds what
/// was in flight at the peak, which the send path caps per connection (the outbox's packet capacity, the oldest dropped
/// first when it is full), and a pool that
/// dropped segments would make the tick allocate new ones. A payload above 1 MiB gets a segment of its own, never pooled.
/// </summary>
/// <remarks>
/// Each size class is a stack under a lock, which allocates only when the class holds more free segments than ever
/// before. A <c>ConcurrentQueue</c> did not: once the free segments of a class outnumbered its tail segment, every
/// segment taken and given back moved the queue through new internal segments, so a burst that left many free
/// (a fight's loot and damage packets, a test run before) had every later tick allocate there, in proportion to its sends.
/// </remarks>
public sealed class PayloadSegmentPool
{
    private const int SmallestShift = 6;
    private const int LargestShift = 20;

    private readonly Stack<PayloadSegment>[] _free = new Stack<PayloadSegment>[LargestShift - SmallestShift + 1];

    private long _outstanding;

    public PayloadSegmentPool()
    {
        for (int i = 0; i < _free.Length; i++)
            _free[i] = new Stack<PayloadSegment>();
    }

    /// <summary>The process's pool, which <c>PacketEncoder.Shared</c> encodes into.</summary>
    public static PayloadSegmentPool Shared { get; } = new();

    /// <summary>Segments rented and not yet returned: 0 once every packet encoded has been written or released.</summary>
    public long Outstanding => Interlocked.Read(ref _outstanding);

    /// <summary>A segment holding a copy of <paramref name="payload" />, with one reference.</summary>
    public PayloadSegment Rent(ReadOnlySpan<byte> payload)
    {
        int sizeClass = SizeClassOf(payload.Length);
        PayloadSegment? segment = null;
        if (sizeClass >= 0)
        {
            Stack<PayloadSegment> free = _free[sizeClass];
            lock (free)
                free.TryPop(out segment);
        }

        segment ??= new PayloadSegment(this, sizeClass,
            sizeClass >= 0 ? 1 << (sizeClass + SmallestShift) : payload.Length);
        segment.Fill(payload);
        Interlocked.Increment(ref _outstanding);
        return segment;
    }

    internal void Return(PayloadSegment segment)
    {
        Interlocked.Decrement(ref _outstanding);
        if (segment.SizeClass >= 0)
        {
            Stack<PayloadSegment> free = _free[segment.SizeClass];
            lock (free)
                free.Push(segment);
        }
    }

    private static int SizeClassOf(int length)
    {
        int shift = SmallestShift;
        while ((1 << shift) < length)
        {
            if (++shift > LargestShift)
                return -1;
        }

        return shift - SmallestShift;
    }
}
