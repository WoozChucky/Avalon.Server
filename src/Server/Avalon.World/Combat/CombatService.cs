using System;
using System.Linq;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

public sealed class CombatService : ICombatService
{
    private readonly CombatConfig        _config;
    private readonly EncounterRegistry   _registry;
    private readonly ISimulationContext? _context;

    public CombatService(CombatConfig config, EncounterRegistry registry, ISimulationContext? context = null)
    {
        _config   = config;
        _registry = registry;
        _context  = context;
    }

    public void ApplyDamage(IUnit attacker, IUnit target, uint damage, IAbility ability)
        => ApplyDamageCore(attacker, target, damage, ability.Metadata.ThreatMultiplier);

    public void ApplyDamage(IUnit attacker, IUnit target, uint damage)
        => ApplyDamageCore(attacker, target, damage, 1.0f);

    private void ApplyDamageCore(IUnit attacker, IUnit target, uint damage, float threatMultiplier)
    {
        Encounter enc = ResolveOrSpawn(attacker, target);

        // Threat is only meaningful when the target is a hostile creature with a threat list.
        if (target is ICreature)
        {
            CharacterClass attackerClass = (attacker as ICharacter)?.Class ?? CharacterClass.Hunter;
            float threat = damage
                         * threatMultiplier
                         * ClassThreatModifier.Get(attackerClass);
            enc.AddThreat(target, attacker, threat);
        }

        // Damage application — IUnit.OnHit mutates HP / sets death flags.
        target.OnHit(attacker, damage);

        // Combat tag — MarkCombat exists only on ICharacter (see ICharacter.cs). Apply to whichever
        // participants are characters; creature in-combat state is tracked through encounter membership.
        if (attacker is ICharacter attackerCharacter) attackerCharacter.MarkCombat();
        if (target   is ICharacter targetCharacter)   targetCharacter.MarkCombat();

        // Death detection (G1): if the OnHit above pushed the target to a lethal state, notify the
        // encounter and broadcast SUnitDeathPacket. ICharacter exposes IsDead explicitly; creatures
        // signal death via CurrentHealth == 0 (their script sets it before raising Died()).
        NotifyDeathIfApplicable(enc, target, attacker);
    }

    private void NotifyDeathIfApplicable(Encounter enc, IUnit target, IUnit attacker)
    {
        bool dead = (target is ICharacter c && c.IsDead) || target.CurrentHealth == 0;
        if (!dead) return;

        enc.OnParticipantDied(target);
        _context?.BroadcastUnitDeath(target, attacker);
    }

    private Encounter ResolveOrSpawn(IUnit attacker, IUnit target)
    {
        Encounter? existing = (_registry.FindEncounterContaining(attacker)
                            ?? _registry.FindEncounterContaining(target)) as Encounter;

        // Classify each participant by TYPE, not by ROLE — so creature-attacks-player and
        // player-attacks-creature both populate the encounter correctly.
        bool wouldExceedCap = existing is not null
                           && target is ICreature
                           && !existing.Hostiles.Contains(target)
                           && existing.Hostiles.Count >= _config.MergeCapHostileParticipants;

        if (wouldExceedCap)
        {
            Encounter spillover = (Encounter)_registry.CreateEncounter();
            ClassifyAndAdd(spillover, attacker);
            ClassifyAndAdd(spillover, target);
            return spillover;
        }

        Encounter enc = existing ?? (Encounter)_registry.CreateEncounter();
        ClassifyAndAdd(enc, attacker);
        ClassifyAndAdd(enc, target);
        return enc;
    }

    private static void ClassifyAndAdd(Encounter enc, IUnit unit)
    {
        if (unit is ICreature)        enc.AddHostile(unit);
        else if (unit is ICharacter)  enc.AddPlayer(unit);
    }

    public void ApplyHeal(IUnit healer, IUnit target, uint amount, IAbility ability)
    {
        if (ability.Metadata.HealThreatPerHp <= 0) return;

        var enc = _registry.FindEncounterContaining(target) as Encounter;
        if (enc is null || enc.Hostiles.Count == 0) return;

        var healerClass = (healer as ICharacter)?.Class ?? CharacterClass.Healer;
        float threatTotal = amount * ability.Metadata.HealThreatPerHp * ClassThreatModifier.Get(healerClass);
        float perHostile  = threatTotal / enc.Hostiles.Count;

        if (!enc.Players.Contains(healer)) enc.AddPlayer(healer);
        foreach (var h in enc.Hostiles)
            enc.AddThreat(h, healer, perHostile);
    }

    public void ApplyTaunt(IUnit caster, IUnit target, uint durationMs)
    {
        if (target is not ICreature creature) return;

        var enc = _registry.FindEncounterContaining(target) as Encounter;
        if (enc is null) return;

        var threats = enc.GetThreatList(creature);
        float top = 0;
        foreach (var t in threats.Values)
            if (t > top) top = t;

        if (!enc.Players.Contains(caster)) enc.AddPlayer(caster);

        threats.TryGetValue(caster, out var current);
        float deltaToBecomeTop = (top + 1.0f) - current;
        enc.AddThreat(creature, caster, deltaToBecomeTop);

        creature.TauntedBy      = caster;
        creature.TauntExpiresAt = DateTime.UtcNow.AddMilliseconds(durationMs);
    }

    public void EnterCombat(IUnit hostile, IUnit player)
    {
        // Resolve-or-spawn handles both directions; arg order matches the merge logic in ApplyDamage.
        ResolveOrSpawn(player, hostile);
    }

    // Phase H stub — throws until that phase lands.
    public void DropPlayerFromEncounter(IUnit player)                                      => throw new NotImplementedException("DropPlayerFromEncounter — Phase H");

    public void RevivePlayer(IUnit player, Vector3 position)
    {
        // Revive is a character-only operation. Creatures use the respawner pipeline
        // (CreatureRespawner/ICreatureMetadata), not RevivePlayer.
        if (player is not ICharacter character) return;

        // Note: ICharacter has a Revive() helper but it forces CurrentHealth to Health
        // (full restore). We need a partial revive driven by ReviveHealthFraction, so we
        // clear IsDead directly via the setter and assign the partial HP value ourselves.
        uint maxHealth = character.Health;
        uint reviveHp  = (uint)(maxHealth * _config.ReviveHealthFraction);

        // Defensive: a tiny ReviveHealthFraction × small max can truncate to 0. Reviving
        // alive-but-at-0HP would be a degenerate state (death detection treats HP==0 as
        // dead for creatures), so floor to 1 HP whenever the unit has any max health.
        if (reviveHp == 0 && maxHealth > 0) reviveHp = 1;

        // Clear dead-flag first so observers don't see "alive but at 0 HP" mid-revive
        // (mirrors the ordering inside CharacterEntity.Revive()).
        character.IsDead        = false;
        character.CurrentHealth = reviveHp;
        character.Position      = position;

        _context?.BroadcastUnitRevive(character, position, reviveHp);
    }

    public IEncounter? GetEncounterFor(IUnit unit) => _registry.FindEncounterContaining(unit);

    public void Update(TimeSpan deltaTime)
    {
        foreach (var enc in _registry.Active.OfType<Encounter>().ToList())
        {
            enc.Update(deltaTime);
            if (enc.ShouldEnd) _registry.Dispose(enc);
        }
    }
}
