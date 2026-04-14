// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;

namespace Avalon.World.Public.Units;

public interface IUnit : IWorldObject
{
    ushort Level { get; set; }
    uint Health { get; set; }
    uint CurrentHealth { get; set; }
    PowerType PowerType { get; set; }
    uint? Power { get; set; }
    uint? CurrentPower { get; set; }
    MoveState MoveState { get; set; }

    GameEntityFields ConsumeDirtyFields();

    void OnHit(IUnit attacker, uint damage);
    void SendAttackAnimation(ISpell? spell);
    void SendFinishCastAnimation(ISpell spell);
    void SendInterruptedCastAnimation(ISpell spell);
}
