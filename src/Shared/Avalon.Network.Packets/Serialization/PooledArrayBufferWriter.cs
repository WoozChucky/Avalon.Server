using System.Buffers;

namespace Avalon.Network.Packets.Serialization;

public sealed class PooledArrayBufferWriter : IBufferWriter<byte>, IDisposable
{
    /// <summary>The buffer a new writer rents, and the one <see cref="Trim" /> goes back to.</summary>
    public const int InitialCapacity = 512;

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(InitialCapacity);
    private int _written;
    private int _peak;

    public int Written => _written;

    /// <summary>
    /// The most the writer held at a <see cref="NotePeak" /> since the last <see cref="ClearPeak" />: how its owner
    /// tells a buffer it still fills from one it has outgrown. <see cref="Reset" /> leaves it, so it does not depend
    /// on when the writer was last emptied; <see cref="Trim" /> clears it.
    /// </summary>
    public int PeakWritten => _peak;

    /// <summary>The buffer's length. It doubles as it fills and is kept by <see cref="Reset" />.</summary>
    public int Capacity => _buffer.Length;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Reset() => _written = 0;

    /// <summary>Counts what the writer holds now towards <see cref="PeakWritten" />.</summary>
    public void NotePeak()
    {
        if (_written > _peak)
            _peak = _written;
    }

    public void ClearPeak() => _peak = 0;

    /// <summary>
    /// Empties the writer and gives its buffer back to the array pool for one of a new writer's size. Nothing may
    /// still read what was written.
    /// </summary>
    /// <remarks>
    /// The buffer goes back as it is, here, when the writer grows and when it is disposed: it is not cleared. It may
    /// hold what a connection was sent, framed, and in plain text when the world does not seal
    /// (<c>Network:PacketEncryption</c> off), and the pool hands the array to whoever rents next, in this process.
    /// So the rule is the renter's: whatever rents from <see cref="ArrayPool{T}.Shared" /> writes before it reads or
    /// sends, and never sends more than it wrote. Clearing here instead would be a write of up to 2 MiB on a send
    /// thread at a moment a peer can choose, by stalling and catching up.
    /// </remarks>
    public void Trim()
    {
        // Rented first: a rent that throws leaves the writer with the buffer it had.
        byte[] small = ArrayPool<byte>.Shared.Rent(InitialCapacity);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = small;
        _written = 0;
        _peak = 0;
    }

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = Array.Empty<byte>();
        _written = 0;
    }

    private void EnsureCapacity(int sizeHint)
    {
        int needed = _written + Math.Max(sizeHint, 1);
        if (needed <= _buffer.Length) return;
        byte[] larger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, needed));
        _buffer.AsSpan(0, _written).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
