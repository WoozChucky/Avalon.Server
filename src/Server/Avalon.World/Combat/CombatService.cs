using System;
using System.Linq;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

public sealed class CombatService : ICombatService
{
    private readonly CombatConfig      _config;
    private readonly EncounterRegistry _registry;

    public CombatService(CombatConfig config, EncounterRegistry registry)
    {
        _config   = config;
        _registry = registry;
    }

    public void ApplyDamage(IUnit attacker, IUnit target, uint damage, IAbility ability)
    {
        Encounter enc = ResolveOrSpawn(attacker, target);

        // Threat is only meaningful when the target is a hostile creature with a threat list.
        if (target is ICreature)
        {
            CharacterClass attackerClass = (attacker as ICharacter)?.Class ?? CharacterClass.Hunter;
            float threat = damage
                         * ability.Metadata.ThreatMultiplier
                         * ClassThreatModifier.Get(attackerClass);
            enc.AddThreat(target, attacker, threat);
        }

        // Damage application — currently goes through IUnit.OnHit. Future Phase G: also broadcast death.
        target.OnHit(attacker, damage);

        // Combat tag — MarkCombat exists only on ICharacter (see ICharacter.cs). Apply to whichever
        // participants are characters; creature in-combat state is tracked through encounter membership.
        if (attacker is ICharacter attackerCharacter) attackerCharacter.MarkCombat();
        if (target   is ICharacter targetCharacter)   targetCharacter.MarkCombat();
    }

    private Encounter ResolveOrSpawn(IUnit attacker, IUnit target)
    {
        Encounter? existing = (_registry.FindEncounterContaining(attacker)
                            ?? _registry.FindEncounterContaining(target)) as Encounter;

        bool wouldExceedCap = existing is not null
                           && target is ICreature
                           && !existing.Hostiles.Contains(target)
                           && existing.Hostiles.Count >= _config.MergeCapHostileParticipants;

        if (wouldExceedCap)
        {
            // Overflow: spawn a new encounter to receive the new hostile.
            Encounter spillover = (Encounter)_registry.CreateEncounter();
            if (attacker is ICharacter) spillover.AddPlayer(attacker);
            if (target   is ICreature)  spillover.AddHostile(target);
            return spillover;
        }

        Encounter enc = existing ?? (Encounter)_registry.CreateEncounter();
        if (target  is ICreature)   enc.AddHostile(target);
        if (attacker is ICharacter) enc.AddPlayer(attacker);
        return enc;
    }

    // Phase F/G/H — stub implementations that throw until those phases land.
    public void ApplyHeal(IUnit healer,   IUnit target, uint amount,     IAbility ability) => throw new NotImplementedException("ApplyHeal — Phase F");
    public void ApplyTaunt(IUnit caster,  IUnit target, uint durationMs)                   => throw new NotImplementedException("ApplyTaunt — Phase F");
    public void EnterCombat(IUnit hostile, IUnit player)
    {
        // Resolve-or-spawn handles both directions; arg order matches the merge logic in ApplyDamage.
        ResolveOrSpawn(player, hostile);
    }

    public void DropPlayerFromEncounter(IUnit player)                                      => throw new NotImplementedException("DropPlayerFromEncounter — Phase H");
    public void RevivePlayer(IUnit player, Vector3 position)                               => throw new NotImplementedException("RevivePlayer — Phase G");

    public void Update(TimeSpan deltaTime)
    {
        foreach (var enc in _registry.Active.OfType<Encounter>().ToList())
            enc.Update(deltaTime);
    }
}
