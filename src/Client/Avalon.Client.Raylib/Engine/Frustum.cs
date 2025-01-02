// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.Engine;

public struct Frustum
{
    public Vector4[]
        Planes; // Each plane is defined by a 4D vector (A, B, C, D) representing the plane equation Ax + By + Cz + D = 0.

    public Frustum() => Planes = new Vector4[6]; // Left, Right, Top, Bottom, Near, Far
}
