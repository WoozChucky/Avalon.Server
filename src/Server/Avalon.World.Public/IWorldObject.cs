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
    ///     Gets or sets the velocity of the world object, in world units (metres) per second.
    /// </summary>
    /// <remarks>
    ///     Replicated as <c>ObjectState.Velocity</c>, and the client extrapolates an object between
    ///     state broadcasts as <c>position + Velocity * secondsSinceUpdate</c>, so the magnitude is
    ///     the speed and matters as much as the direction. Zero when the object is at rest. A
    ///     character's comes from <c>PlayerInputHandler</c> (direction times movement speed); a
    ///     creature's is written only by its <see cref="Creatures.ICreatureLocomotion" />. Ability projectiles
    ///     carry their own <c>Velocity</c> and do not follow this yet: <c>FireballAbilityScript</c>
    ///     publishes a unit direction.
    /// </remarks>
    Vector3 Velocity { get; set; }

    /// <summary>
    ///     Gets or sets the orientation of the world object.
    /// </summary>
    Vector3 Orientation { get; set; }
}
