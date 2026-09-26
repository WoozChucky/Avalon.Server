// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

public class AbilityInstance
{
    public required IUnit Caster { get; init; }
    public required AbilityAim Aim { get; init; }
    public required IAbility Ability { get; init; }

    /// <summary>Built when the cast was queued, with <see cref="Aim" />; fired as it is once the cast time runs out.</summary>
    public required AbilityScript Script { get; init; }
    public required Vector3 CastStartPosition { get; init; }
}
