// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common.Mathematics;

namespace Avalon.World.Public;

public interface IWorldObject : IObject
{
    Vector3 Position { get; set; }
    Vector3 Velocity { get; set; }
    Vector3 Orientation { get; set; }
}
