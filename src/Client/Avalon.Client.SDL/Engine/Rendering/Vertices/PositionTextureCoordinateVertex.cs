// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.SDL.Engine.Vertices;

public struct PositionTextureCoordinateVertex : IEquatable<PositionTextureCoordinateVertex>
{
    public Vector3 Position;
    public Vector2 TextureCoordinate;

    public bool Equals(PositionTextureCoordinateVertex other) =>
        Position.Equals(other.Position) && TextureCoordinate.Equals(other.TextureCoordinate);

    public override bool Equals(object? obj) => obj is PositionTextureCoordinateVertex other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, TextureCoordinate);

    public static bool operator ==(PositionTextureCoordinateVertex left, PositionTextureCoordinateVertex right) =>
        left.Equals(right);

    public static bool operator !=(PositionTextureCoordinateVertex left, PositionTextureCoordinateVertex right) =>
        !left.Equals(right);
}
