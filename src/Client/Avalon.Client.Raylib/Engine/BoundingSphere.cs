// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common.Mathematics;

namespace Avalon.Client.Engine;

public struct BoundingSphere
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
}
