// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

namespace Avalon.Common.ValueObjects;

public class ChunkId : ValueObject<uint>
{
    public ChunkId(uint value) : base(value)
    {
    }

    public static implicit operator ulong(ChunkId chunkId) => chunkId.Value;
    public static implicit operator ChunkId(uint value) => new(value);
}
