// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.SDL.Engine.Vertices;

public struct PositionNormalTexCoordVertex : IEquatable<PositionNormalTexCoordVertex>
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TextureCoordinate;

    public bool Equals(PositionNormalTexCoordVertex other) => Position.Equals(other.Position) &&
                                                              Normal.Equals(other.Normal) &&
                                                              TextureCoordinate.Equals(other.TextureCoordinate);

    public override bool Equals(object? obj) => obj is PositionNormalTexCoordVertex other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, Normal, TextureCoordinate);

    public static bool operator ==(PositionNormalTexCoordVertex left, PositionNormalTexCoordVertex right) =>
        left.Equals(right);

    public static bool operator !=(PositionNormalTexCoordVertex left, PositionNormalTexCoordVertex right) =>
        !left.Equals(right);
}
