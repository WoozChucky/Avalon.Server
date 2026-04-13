// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Common.Mathematics;

namespace Avalon.World.Public;

/// <summary>
///     Represents a world object in the Avalon ARPG game.
/// </summary>
public interface IWorldObject : IObject
{
    /// <summary>
    ///     Gets or sets the position of the world object.
    /// </summary>
    Vector3 Position { get; set; }

    /// <summary>
    ///     Gets or sets the velocity of the world object.
    /// </summary>
    Vector3 Velocity { get; set; }

    /// <summary>
    ///     Gets or sets the orientation of the world object.
    /// </summary>
    Vector3 Orientation { get; set; }
}
