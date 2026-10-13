using System.Buffers;

namespace Avalon.Network.Packets.Serialization;

public sealed class PooledArrayBufferWriter : IBufferWriter<byte>, IDisposable
{
    /// <summary>The buffer a new writer rents, and the one <see cref="Trim" /> goes back to.</summary>
    public const int InitialCapacity = 512;

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(InitialCapacity);
    private int _written;

    public int Written => _written;

    /// <summary>The buffer's length. It doubles as it fills and is kept by <see cref="Reset" />.</summary>
    public int Capacity => _buffer.Length;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Reset() => _written = 0;

    /// <summary>
    /// Empties the writer and gives its buffer back to the array pool for one of a new writer's size. Nothing may
    /// still read what was written.
    /// </summary>
    public void Trim()
    {
        // Rented first: a rent that throws leaves the writer with the buffer it had.
        byte[] small = ArrayPool<byte>.Shared.Rent(InitialCapacity);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = small;
        _written = 0;
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
