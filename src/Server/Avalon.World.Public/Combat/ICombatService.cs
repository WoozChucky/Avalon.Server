using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Combat;

public interface ICombatService
{
    /// <summary>
    /// An ability's hit (#1178): the ability's own base decides what it deals (an Attack its caster's weapon roll, a
    /// Spell its spell base), scaled by the caster's offence, then the target's defences are rolled.
    /// </summary>
    void ApplyDamage(IUnit attacker, IUnit target, IAbility ability);

    /// <summary>
    /// Raw-damage entry point for damage no ability deals (an item script's, for one). Behaves like
    /// the ability-bearing overload but uses a default <c>ThreatMultiplier</c> of <c>1.0</c> and
    /// skips ability-driven side effects. Funnels through the same encounter / threat / death
    /// pipeline so death broadcasts are still emitted.
    /// </summary>
    void ApplyDamage(IUnit attacker, IUnit target, uint damage);

    /// <summary>
    /// Restores the ability's heal (#1178: its spell base, scaled by the healer's increased healing) to
    /// <paramref name="target" />, capped at its maximum, and adds heal threat against the hostiles fighting it. A dead
    /// unit is never healed.
    /// </summary>
    void ApplyHeal(IUnit healer, IUnit target, IAbility ability);
    void ApplyTaunt(IUnit caster, IUnit target, uint durationMs);
    void EnterCombat(IUnit hostile, IUnit player);
    void DropPlayerFromEncounter(IUnit player);
    void RevivePlayer(IUnit player, Vector3 position);
    void Update(TimeSpan deltaTime);

    /// <summary>
    /// Returns the active encounter that contains <paramref name="unit"/> as either a hostile
    /// or a player participant, or <c>null</c> when the unit is not currently in combat.
    /// AI scripts use this to read the encounter's threat list and pick the top-threat target.
    /// </summary>
    IEncounter? GetEncounterFor(IUnit unit);
}
