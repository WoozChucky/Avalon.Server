// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.SDL.Engine.Vertices;

public struct PositionVertex : IEquatable<PositionVertex>
{
    public Vector3 Position;

    public bool Equals(PositionVertex other) => Position.Equals(other.Position);

    public override bool Equals(object? obj) => obj is PositionVertex other && Equals(other);

    public override int GetHashCode() => Position.GetHashCode();

    public static bool operator ==(PositionVertex left, PositionVertex right) => left.Equals(right);

    public static bool operator !=(PositionVertex left, PositionVertex right) => !left.Equals(right);
}
