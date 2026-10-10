using System.Diagnostics;

namespace Avalon.Network.Packets.Abstractions;

/// <summary>
/// One encoded payload in a pooled buffer, counted by reference (#875). The tick encodes into it; each connection that
/// sends it copies it into its frame and releases its reference; the last release returns it to its pool, which may
/// hand it to the next packet at once.
/// </summary>
public sealed class PayloadSegment
{
    private readonly PayloadSegmentPool _pool;
    private readonly byte[] _buffer;
    private int _length;
    private int _references;

    internal PayloadSegment(PayloadSegmentPool pool, int sizeClass, int capacity)
    {
        _pool = pool;
        SizeClass = sizeClass;
        _buffer = new byte[capacity];
    }

    /// <summary>The pool's size class, or -1 for a payload larger than any class, which is never pooled.</summary>
    internal int SizeClass { get; }

    public int Length => _length;

    public ReadOnlySpan<byte> Span => new(_buffer, 0, _length);

    public ReadOnlyMemory<byte> Memory => new(_buffer, 0, _length);

    internal void Fill(ReadOnlySpan<byte> payload)
    {
        payload.CopyTo(_buffer);
        _length = payload.Length;
        Volatile.Write(ref _references, 1);
    }

    internal void AddReference()
    {
        // A share after the last release would hold a segment its pool may already have handed to the next packet.
        int references = Interlocked.Increment(ref _references);
        Debug.Assert(references > 1, "A payload segment was shared after its last release");
    }

    internal void Release()
    {
        int left = Interlocked.Decrement(ref _references);
        if (left == 0)
            _pool.Return(this);

        // Only the release that reaches zero returns the segment, so a release past zero returns nothing a second time,
        // but it is still a holder's bug: one too few Share calls, or a packet released after it was given away. Never
        // thrown: a release can run on the tick, which must not throw for a send.
        Debug.Assert(left >= 0, "A payload segment was released more times than it was referenced");
    }
}
