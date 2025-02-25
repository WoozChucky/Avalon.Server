// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.SDL.Engine.Math;

public struct IVec3 : IEquatable<IVec3>
{
    public int X;
    public int Y;
    public int Z;

    public IVec3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public bool Equals(IVec3 other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is IVec3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(IVec3 left, IVec3 right) => left.Equals(right);

    public static bool operator !=(IVec3 left, IVec3 right) => !left.Equals(right);

    public static IVec3 operator +(IVec3 left, IVec3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static IVec3 operator -(IVec3 left, IVec3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static IVec3 operator *(IVec3 left, int right) => new(left.X * right, left.Y * right, left.Z * right);

    public static IVec3 operator *(int left, IVec3 right) => new(left * right.X, left * right.Y, left * right.Z);

    public static IVec3 operator /(IVec3 left, int right) => new(left.X / right, left.Y / right, left.Z / right);

    public static IVec3 operator /(int left, IVec3 right) => new(left / right.X, left / right.Y, left / right.Z);

    public static implicit operator Vector3(IVec3 v) => new(v.X, v.Y, v.Z);

    public static implicit operator IVec3(Vector3 v) => new((int)v.X, (int)v.Y, (int)v.Z);

    public static IVec3 Zero => new(0, 0, 0);
    public static IVec3 One => new(1, 1, 1);
    public static IVec3 UnitX => new(1, 0, 0);
    public static IVec3 UnitY => new(0, 1, 0);
    public static IVec3 UnitZ => new(0, 0, 1);
}
