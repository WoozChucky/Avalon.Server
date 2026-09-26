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
    ///     <para>
    ///     Replicated as <c>ObjectState.Velocity</c>, and the client extrapolates an object between
    ///     state broadcasts as <c>position + Velocity * secondsSinceUpdate</c>. So the magnitude is
    ///     the speed the object is actually moving at, never a bare unit direction, and the value is
    ///     exactly zero whenever the object is at rest. Every writer follows that rule (#424):
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             Characters: <c>PlayerInputHandler</c> writes the horizontal step it actually took this
    ///             input, divided by the input step length, so a character held against a wall or given
    ///             no input publishes zero. A character that dies is set to zero, since its input is
    ///             dropped from then on. Spawned at zero by character select.
    ///         </item>
    ///         <item>
    ///             Creatures: written only by the instance's <see cref="Creatures.ICreatureLocomotion" />,
    ///             direction times speed while walking and zero once at rest, stopped, teleported or killed.
    ///         </item>
    ///         <item>
    ///             Ability objects: a projectile such as <c>FireballAbilityScript</c> publishes its
    ///             direction times its flight speed and zero once it hits or fizzles; a non-projectile
    ///             ability such as <c>StrikeAbilityScript</c> never moves and leaves it at zero.
    ///         </item>
    ///         <item>Portals never move and are zero.</item>
    ///     </list>
    /// </remarks>
    Vector3 Velocity { get; set; }

    /// <summary>
    ///     Gets or sets the orientation of the world object.
    /// </summary>
    Vector3 Orientation { get; set; }
}
