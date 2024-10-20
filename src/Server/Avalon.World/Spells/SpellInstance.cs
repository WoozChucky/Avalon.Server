// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common.Mathematics;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;

namespace Avalon.World.Spells;

public class SpellInstance
{
    public required IUnit Caster { get; init; }
    public IUnit? Target { get; set; }
    public required ISpell SpellInfo { get; init; }
    public required Vector3 CastStartPosition { get; init; }
}
