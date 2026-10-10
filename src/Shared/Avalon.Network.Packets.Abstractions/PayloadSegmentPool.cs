using System.Runtime.InteropServices;

namespace Avalon.Network.Packets.Abstractions;

/// <summary>
/// Free payload segments by size class, powers of two from 64 B to 1 MiB (#875). It keeps what was in flight at the peak,
/// up to <see cref="MaxFreeBytesPerSizeClass" /> per size class: the send path caps what one connection holds
/// (<c>Network:MaxPendingBytes</c>, past which the connection is closed as too slow), but not what a thousand connections
/// held together during a network blip, which a pool that never shrank would pin for good. Past the cap a segment given
/// back is left to the GC. A payload above 1 MiB gets a segment of its own, never pooled.
/// </summary>
/// <remarks>
/// Each size class is a stack under a lock, which allocates only at a new peak. A <c>ConcurrentQueue</c> did not: after
/// a burst, it kept allocating new, larger internal segments with no new peak until its free segments sat in one; those
/// allocations landed in later ticks. A lock-free stack would allocate a node per push.
/// <para>
/// The tick rents and the send threads give back, so a size class's lock is shared between them. A send pass holds back
/// what it gives back (<see cref="BeginReturnBatch" />) and returns it with one lock per size class, rather than one per
/// packet, and each size class's stack sits on cache lines of its own, so the tick's rents of one size never contend
/// with returns of another (docs/benchmarks.md, the send path).
/// </para>
/// </remarks>
public sealed class PayloadSegmentPool
{
    /// <summary>
    /// The most free segment bytes one size class keeps, by segment capacity: 65,536 segments of 64 B, four of 1 MiB. The
    /// segments' own objects come on top (about 64 B each).
    /// </summary>
    public const int MaxFreeBytesPerSizeClass = 4 * 1024 * 1024;

    private const int SmallestShift = 6;
    private const int LargestShift = 20;
    private const int SizeClasses = LargestShift - SmallestShift + 1;

    // A send pass gives back at most this many segments of one size class at once: what it holds back stays bounded,
    // and a long pass does not keep the tick waiting for segments it has finished with.
    private const int ReturnBatchSize = 256;

    // [ThreadStatic] fields take the t_ prefix, which a naming rule cannot select (the static-field rule asks for s_).
#pragma warning disable IDE1006
    [ThreadStatic] private static ReturnBatch? t_batch;
#pragma warning restore IDE1006

    private readonly FreeList[] _free = new FreeList[SizeClasses];

    private readonly bool _countsOutstanding;
    private long _outstanding;

    /// <param name="countOutstanding">
    /// Counts the segments rented and not yet returned (<see cref="Outstanding" />): a test's pool. Off for the process's
    /// pool, whose counter would be one shared word written by the tick and every send thread twice per packet (#883).
    /// </param>
    public PayloadSegmentPool(bool countOutstanding = false)
    {
        _countsOutstanding = countOutstanding;
        for (int i = 0; i < _free.Length; i++)
            _free[i] = new FreeList(MaxFreeBytesPerSizeClass >> (i + SmallestShift));
    }

    /// <summary>The process's pool, which <c>PacketEncoder.Shared</c> encodes into. It counts nothing.</summary>
    public static PayloadSegmentPool Shared { get; } = new();

    /// <summary>
    /// Segments rented and not yet returned: 0 once every packet encoded has been written or released, and every return
    /// batch ended. Only a pool made to count them has the figure; any other throws rather than read a 0 it never counted.
    /// </summary>
    public long Outstanding => _countsOutstanding
        ? Interlocked.Read(ref _outstanding)
        : throw new InvalidOperationException("This payload segment pool does not count its outstanding segments.");

    /// <summary>A segment holding a copy of <paramref name="payload" />, with one reference.</summary>
    public PayloadSegment Rent(ReadOnlySpan<byte> payload)
    {
        int sizeClass = SizeClassOf(payload.Length);
        PayloadSegment? segment = sizeClass >= 0 ? _free[sizeClass].TryPop() : null;
        segment ??= new PayloadSegment(this, sizeClass,
            sizeClass >= 0 ? 1 << (sizeClass + SmallestShift) : payload.Length);
        segment.Fill(payload);
        if (_countsOutstanding)
            Interlocked.Increment(ref _outstanding);
        return segment;
    }

    /// <summary>
    /// From here to <see cref="EndReturnBatch" />, the segments of this pool the calling thread gives back are held, and
    /// given back together: one lock per size class, rather than one per segment. A send thread's pass, which gives back
    /// every packet it frames. Allocates only the first time a thread holds a segment of a size class.
    /// </summary>
    public void BeginReturnBatch()
    {
        ReturnBatch batch = t_batch ??= new ReturnBatch();
        if (batch.Pool is { } open && !ReferenceEquals(open, this))
            open.EndReturnBatch();

        batch.Pool = this;
    }

    /// <summary>Gives back what the calling thread held since <see cref="BeginReturnBatch" />, and stops holding.</summary>
    public void EndReturnBatch()
    {
        ReturnBatch? batch = t_batch;
        if (batch is null || !ReferenceEquals(batch.Pool, this))
            return;

        for (int sizeClass = 0; sizeClass < SizeClasses; sizeClass++)
        {
            if (batch.Holds(sizeClass))
                GiveBack(batch, sizeClass);
        }

        batch.Pool = null;
    }

    internal void Return(PayloadSegment segment)
    {
        int sizeClass = segment.SizeClass;
        if (sizeClass >= 0)
        {
            ReturnBatch? batch = t_batch;
            if (batch is not null && ReferenceEquals(batch.Pool, this))
            {
                if (batch.Hold(sizeClass, segment) == ReturnBatchSize)
                    GiveBack(batch, sizeClass);
                return;
            }

            _free[sizeClass].Push(segment);
        }

        if (_countsOutstanding)
            Interlocked.Decrement(ref _outstanding);
    }

    private void GiveBack(ReturnBatch batch, int sizeClass)
    {
        Span<PayloadSegment?> held = batch.Take(sizeClass);
        _free[sizeClass].Push(held);
        held.Clear();
        if (_countsOutstanding)
            Interlocked.Add(ref _outstanding, -held.Length);
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

    /// <summary>
    /// One size class's free segments, a stack under its own lock (the object's header). Its fields sit at the front and
    /// the object runs on past them, so the next size class's object, and its lock word, start more than the 128 B of
    /// adjacent cache lines a processor may fetch together further on: a rent of one size never waits on a line a return
    /// of another size holds.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private sealed class FreeList
    {
        [FieldOffset(0)] private PayloadSegment?[] _items = new PayloadSegment?[16];
        [FieldOffset(8)] private int _count;
        [FieldOffset(12)] private readonly int _limit;
#pragma warning disable CS0169 // never read: it only takes up room
        [FieldOffset(144)] private readonly long _end;
#pragma warning restore CS0169

        public FreeList(int limit) => _limit = limit;

        public PayloadSegment? TryPop()
        {
            lock (this)
            {
                if (_count == 0)
                    return null;

                PayloadSegment? segment = _items[--_count];
                _items[_count] = null; // a segment rented here and later dropped past the cap is not kept alive by its slot
                return segment;
            }
        }

        /// <summary>Keeps it if the size class is under its cap; past it, it is left to the GC.</summary>
        public void Push(PayloadSegment segment)
        {
            lock (this)
            {
                if (_count == _limit)
                    return;

                if (_count == _items.Length)
                    Array.Resize(ref _items, Math.Min(_items.Length * 2, _limit));
                _items[_count++] = segment;
            }
        }

        /// <summary>Keeps what fits under the cap; the rest is left to the GC.</summary>
        public void Push(ReadOnlySpan<PayloadSegment?> segments)
        {
            lock (this)
            {
                int keep = Math.Min(segments.Length, _limit - _count);
                if (keep <= 0)
                    return;

                if (_count + keep > _items.Length)
                    Array.Resize(ref _items, Math.Min(Math.Max(_items.Length * 2, _count + keep), _limit));

                segments[..keep].CopyTo(_items.AsSpan(_count));
                _count += keep;
            }
        }
    }

    /// <summary>A thread's held returns, by size class, for the pool whose batch it has open.</summary>
    private sealed class ReturnBatch
    {
        private readonly PayloadSegment?[]?[] _held = new PayloadSegment?[SizeClasses][];
        private readonly int[] _counts = new int[SizeClasses];

        public PayloadSegmentPool? Pool { get; set; }

        public bool Holds(int sizeClass) => _counts[sizeClass] != 0;

        /// <returns>How many segments of the size class are now held.</returns>
        public int Hold(int sizeClass, PayloadSegment segment)
        {
            PayloadSegment?[] held = _held[sizeClass] ??= new PayloadSegment?[ReturnBatchSize];
            held[_counts[sizeClass]] = segment;
            return ++_counts[sizeClass];
        }

        /// <summary>The segments held of a size class, which stop being held: the caller gives them back and clears them.</summary>
        public Span<PayloadSegment?> Take(int sizeClass)
        {
            int count = _counts[sizeClass];
            _counts[sizeClass] = 0;
            return _held[sizeClass].AsSpan(0, count);
        }
    }
}
