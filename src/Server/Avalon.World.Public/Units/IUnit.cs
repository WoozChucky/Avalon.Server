// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;

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
    DateTime LastCastStartTime { get; set; }

    /// <summary>
    /// Metres. The radius of this unit's body circle on X/Z, which a skill's shape must overlap to
    /// affect it (#164). Read-only.
    /// </summary>
    float BodyRadius { get; }

    GameEntityFields ConsumeDirtyFields();

    void OnHit(IUnit attacker, uint damage);
    void SendAttackAnimation(IAbility? spell);
    void SendFinishCastAnimation(IAbility spell);
    void SendInterruptedCastAnimation(IAbility spell);
}
