// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

public class AbilityInstance
{
    public required IUnit Caster { get; init; }
    public IUnit? Target { get; set; }
    public required IAbility Ability { get; init; }
    public required Vector3 CastStartPosition { get; init; }
}
