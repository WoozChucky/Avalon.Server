using System;
using System.Buffers;

namespace Avalon.Network.Packets.Serialization;

public sealed class PooledArrayBufferWriter : IBufferWriter<byte>
{
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(512);
    private int _written;

    public int Written => _written;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Reset() => _written = 0;

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

    private void EnsureCapacity(int sizeHint)
    {
        int needed = _written + Math.Max(sizeHint, 1);
        if (needed <= _buffer.Length) return;
        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, needed));
        _buffer.AsSpan(0, _written).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
