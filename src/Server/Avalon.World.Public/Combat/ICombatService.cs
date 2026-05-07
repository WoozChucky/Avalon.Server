using System;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Combat;

public interface ICombatService
{
    void ApplyDamage(IUnit attacker, IUnit target, uint damage, IAbility ability);
    void ApplyHeal  (IUnit healer,   IUnit target, uint amount, IAbility ability);
    void ApplyTaunt (IUnit caster,   IUnit target, uint durationMs);
    void EnterCombat(IUnit hostile,  IUnit player);
    void DropPlayerFromEncounter(IUnit player);
    void RevivePlayer(IUnit player, Vector3 position);
    void Update(TimeSpan deltaTime);
}
