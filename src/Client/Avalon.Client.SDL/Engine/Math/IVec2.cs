// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.SDL.Engine.Math;

public struct IVec2 : IEquatable<IVec2>
{
    public int X;
    public int Y;

    public IVec2(int x, int y)
    {
        X = x;
        Y = y;
    }

    public bool Equals(IVec2 other) => X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is IVec2 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(IVec2 left, IVec2 right) => left.Equals(right);

    public static bool operator !=(IVec2 left, IVec2 right) => !left.Equals(right);

    public static IVec2 operator +(IVec2 left, IVec2 right) => new(left.X + right.X, left.Y + right.Y);

    public static IVec2 operator -(IVec2 left, IVec2 right) => new(left.X - right.X, left.Y - right.Y);

    public static IVec2 operator *(IVec2 left, int right) => new(left.X * right, left.Y * right);

    public static IVec2 operator *(int left, IVec2 right) => new(left * right.X, left * right.Y);

    public static IVec2 operator /(IVec2 left, int right) => new(left.X / right, left.Y / right);

    public static IVec2 operator /(int left, IVec2 right) => new(left / right.X, left / right.Y);

    public static implicit operator Vector2(IVec2 v) => new(v.X, v.Y);

    public static implicit operator IVec2(Vector2 v) => new((int)v.X, (int)v.Y);

    public static IVec2 Zero => new(0, 0);
    public static IVec2 One => new(1, 1);
    public static IVec2 UnitX => new(1, 0);
    public static IVec2 UnitY => new(0, 1);
}
