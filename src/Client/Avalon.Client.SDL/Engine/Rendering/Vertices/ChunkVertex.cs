// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;
using Avalon.Client.SDL.Engine.Math;

namespace Avalon.Client.SDL.Engine.Vertices;

public unsafe struct ChunkVertex : IEquatable<ChunkVertex>
{
    public static readonly int SizeInBytes = sizeof(IVec3) + sizeof(Vector3) + sizeof(Vector2) + sizeof(IVec2);

    public IVec3 Position;
    public Vector3 Normal;
    public Vector2 TextureCoordinate;
    public IVec2 BlockCoordinate;

    public bool Equals(ChunkVertex other) =>
        Position.Equals(other.Position)
        && Normal.Equals(other.Normal)
        && TextureCoordinate.Equals(other.TextureCoordinate)
        && BlockCoordinate.Equals(other.BlockCoordinate);

    public override bool Equals(object? obj) => obj is ChunkVertex other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, Normal, TextureCoordinate, BlockCoordinate);

    public static bool operator ==(ChunkVertex left, ChunkVertex right) => left.Equals(right);

    public static bool operator !=(ChunkVertex left, ChunkVertex right) => !left.Equals(right);
}
