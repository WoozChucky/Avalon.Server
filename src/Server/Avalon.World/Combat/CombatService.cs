using System.Diagnostics.CodeAnalysis;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Auras;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Pvp;
using Avalon.World.Scripts.Creatures;

namespace Avalon.World.Combat;

public sealed class CombatService : ICombatService, IHostileEncounterExit
{
    private readonly CombatConfig _config;
    private readonly EncounterRegistry _registry;
    private readonly ISimulationContext? _context;
    private readonly PvpToggle? _pvp;
    private readonly ICombatOutcomes? _outcomes;
    private readonly TimeProvider _time;
    private readonly float _furyFromDamageTaken;
    private readonly ICombatRandom _random;
    private readonly Func<CombatFormula> _formula;

    /// <summary>The seeded formula, for a service built without an accessor (tests).</summary>
    private static readonly CombatFormula s_seededFormula = CombatSeed.Formula();

    /// <param name="outcomes">Where hits and kills are reported (#546): the instance this service belongs to.</param>
    /// <param name="time">The instance's clock, the one taunts end by (#614).</param>
    /// <param name="furyFromDamageTaken">Game:FuryFromDamageTaken (#526); the setting's default when omitted.</param>
    /// <param name="random">Every combat roll (#506); <see cref="CombatRandom.Steady" /> when omitted.</param>
    /// <param name="formula">
    /// The current combat formula (#506), read once per hit at its own resolve, so a reload that lands between
    /// two hits of one cast changes the second and never half of one; the seeded formula when omitted.
    /// </param>
    public CombatService(CombatConfig config, EncounterRegistry registry, ISimulationContext? context = null,
        PvpToggle? pvp = null, ICombatOutcomes? outcomes = null, TimeProvider? time = null,
        float furyFromDamageTaken = GameConfiguration.DefaultFuryFromDamageTaken, ICombatRandom? random = null,
        Func<CombatFormula>? formula = null)
    {
        _random = random ?? CombatRandom.Steady;
        _formula = formula ?? (() => s_seededFormula);
        _time = time ?? TimeProvider.System;
        _furyFromDamageTaken = furyFromDamageTaken;
        _config = config;
        _registry = registry;
        _context = context;
        _pvp = pvp;
        _outcomes = outcomes;
    }

    /// <summary>
    /// An ability's hit (#506, #1178): its base is the ability's own (<see cref="OffenceMath.DirectBase" />: an Attack's
    /// weapon roll, a Spell's spell base) scaled by the caster's offence, then the target's defences are rolled.
    /// </summary>
    public void ApplyDamage(IUnit attacker, IUnit target, IAbility ability)
        => ApplyAbilityDamage(attacker, target, ability, unavoidable: false);

    /// <summary>
    /// A Hostile ability's hit on one unit (#1164), answering how it went: only a hit that landed applies the ability's aura.
    /// It rolls whether it lands, the target's chance to evade, unless the ability is Unavoidable; then deals its damage (as
    /// <see cref="ApplyDamage(IUnit, IUnit, IAbility)" />) when its Effects has Damage. One
    /// with no damage draws that roll alone and counts as combat either way, as a dodge does, whether or not its aura then
    /// sticks: the encounter forms, a creature's script is told it was attacked, both are tagged in combat, no threat is
    /// added beyond the encounter's and no PvP timer restarts (a dodge restarts none either: only damage dealt does).
    /// Landed, it deals nothing and sends nothing (no crit, block or armour), and the aura's own combat entry that follows
    /// finds all of that already done; dodged, it is sent as a dodge. A target every hit passes over is Ignored and draws
    /// nothing.
    /// </summary>
    public HitOutcome ApplyAbilityHit(IUnit attacker, IUnit target, IAbility ability)
    {
        AbilityMetadata metadata = ability.Metadata;
        bool unavoidable = (metadata.Flags & AbilityFlags.Unavoidable) != AbilityFlags.None;
        if (AbilityRules.HasDirectEffect(metadata.Effects, metadata.Affects))
            return ApplyAbilityDamage(attacker, target, ability, unavoidable);

        // The same targets every hit passes over, before any draw.
        if (IgnoresHits(target)) return HitOutcome.Ignored;
        if (unavoidable || !HitResolver.Evades(DefenderOf(target), attacker.Level, _formula(), _random))
        {
            EnterCombatForNothing(attacker, target);
            return HitOutcome.Landed;
        }

        // It would have dealt nothing, so it carried no type (#1178), though it is named by its own.
        Dodged(attacker, target, 0f, ability.AbilityId, HitResolver.TypeOf(metadata.DamageType, AttackerOf(attacker)),
            DamageTypeMask.None);
        return HitOutcome.Dodged;
    }

    /// <summary>A raw hit, such as a creature's swing (#506): <paramref name="damage" /> is the base the defences reduce.</summary>
    public void ApplyDamage(IUnit attacker, IUnit target, uint damage)
    {
        // Every target a hit passes over (IgnoresHits) is refused here, before anything is drawn or changed.
        if (IgnoresHits(target)) return;

        AttackerCombat attackerCombat = AttackerOf(attacker);
        // #1178: a raw hit is one part, of the attacker's base type.
        var parts = DamageParts.Of(attackerCombat.BaseType, damage);
        ResolveHit(attacker, target, attackerCombat, parts, ability: null, unavoidable: false);
    }

    /// <summary>
    /// An ability's hit (#1178): its own base, split by type (<see cref="OffenceMath.DirectBase" />), drawn only past every
    /// target a hit passes over, so nothing refused draws a roll.
    /// </summary>
    /// <param name="unavoidable">An Unavoidable ability's hit (#1164): it draws no evasion roll.</param>
    private HitOutcome ApplyAbilityDamage(IUnit attacker, IUnit target, IAbility ability, bool unavoidable)
    {
        if (IgnoresHits(target)) return HitOutcome.Ignored;

        AttackerCombat attackerCombat = AttackerOf(attacker);
        DamageParts parts = OffenceMath.DirectBase(attackerCombat, ability.Metadata, _random);
        return ResolveHit(attacker, target, attackerCombat, parts, ability, unavoidable);
    }

    /// <summary>
    /// Resolves a hit whose base is <paramref name="parts" />, on a target no early refusal passes over (IgnoresHits): an
    /// invulnerable creature (a town NPC) absorbs nothing and provokes nothing, so attacking one never creates an encounter,
    /// adds threat or tags anyone in combat; a corpse takes no hits (#588); a creature walking home ignores them (#610); a
    /// protected arrival passes over the whole hit. Every damage source funnels through the two entries above, so those
    /// guards cover them all.
    /// </summary>
    /// <param name="ability">
    /// The ability that hit, or null for a raw hit: it multiplies the threat, names the hit to the character hit, and gives
    /// its PowerGainPerHit to the caster per unit damaged (#526).
    /// </param>
    /// <param name="unavoidable">An Unavoidable ability's hit (#1164): it draws no evasion roll.</param>
    private HitOutcome ResolveHit(IUnit attacker, IUnit target, in AttackerCombat attackerCombat, in DamageParts parts,
        IAbility? ability, bool unavoidable)
    {
        // #506: the formula is read once for the whole hit.
        CombatFormula formula = _formula();
        AbilityMetadata? metadata = ability?.Metadata;
        HitDamage hit = HitResolver.ResolveDamage(attackerCombat, DefenderOf(target), parts, formula, _random, unavoidable);
        HitResult result = hit.Result;

        float threatMultiplier = metadata?.ThreatMultiplier ?? 1.0f;
        AbilityId? abilityId = ability?.AbilityId;
        uint powerGainPerHit = metadata is null ? 0u : (uint)Math.Max(0, metadata.PowerGainPerHit);

        if ((result & HitResult.Dodged) != 0)
        {
            Dodged(attacker, target, (float)parts.Total * threatMultiplier, abilityId, hit.Type, hit.Types);
            return HitOutcome.Dodged;
        }

        // Everything below reads the resolved damage (#506): threat, the hit, the gain and the PvP reset read the whole
        // hit, the ward's part included (#1178).
        uint damage = hit.Total;
        // Staff testing still resolves and reports the hit, forms an encounter, and tags combat.
        // Only its health loss and effects that require positive damage are suppressed.
        if (target is CharacterEntity { GodMode: true, IsDead: false }) damage = 0;

        // #1178: the ward takes what it may (all but blight) before life, and any damage pauses its recharge. The clock is
        // read only for a target with a ward that took damage: anything else leaves the ward as it is.
        uint absorbed = 0u;
        if (damage > 0 && HasWard(target))
        {
            DateTime now = _time.GetUtcNow().UtcDateTime;
            absorbed = AbsorbWithWard(target, Math.Min(hit.Absorbable, damage), now);
            WardDamaged(target, now);
        }

        var report = new HitReport(result, hit.Type, hit.Types, absorbed);

        // #164: a player-on-player hit that deals damage to a living player restarts both players'
        // running PvP off timers. Towns are covered because AbilityEffect checks Hostility before it
        // damages anyone, and Hostility refuses players in a town. ICombatService is on the modding
        // API, though, and this reset does not re-check the town rule itself.
        if (damage > 0 && attacker is CharacterEntity attackerEntity
                       && target is CharacterEntity { IsDead: false } targetEntity)
        {
            _pvp?.OnPlayerHitPlayer(attackerEntity, targetEntity);
        }

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

        // Past every early return above, so a creature target has health left; a character may be dead.
        bool targetWasAlive = target is ICharacter victim ? !victim.IsDead : target.CurrentHealth > 0;

        // Damage application — IUnit.OnHit mutates HP / sets death flags. A character is also told
        // which ability hit it, or none for a swing (#521 item 8).
        Hit(attacker, target, damage, damage - absorbed, abilityId, report);

        // #770: a hit that dealt something to a unit it left alive may break the crowd control it holds, by chance.
        if (damage > 0 && !AuraSystem.IsDead(target))
            _outcomes?.DamageTaken(target);

        // #526: an ability that damaged a living unit gives its caster the ability's gain, once per unit
        // damaged, capped at the caster's maximum; after the hit, before the death detection. Only a
        // character has a pool a cast spends, so a creature attacker gains nothing.
        if (damage > 0 && targetWasAlive && powerGainPerHit > 0 && attacker is CharacterEntity caster)
            caster.GainPower(powerGainPerHit);

        // Combat tag — MarkCombat exists only on ICharacter (see ICharacter.cs). Apply to whichever
        // participants are characters; creature in-combat state is tracked through encounter membership.
        if (attacker is ICharacter attackerCharacter) attackerCharacter.MarkCombat();
        if (target is ICharacter targetCharacter) targetCharacter.MarkCombat();

        // Death detection (G1): if the OnHit above pushed the target to a lethal state, notify the
        // encounter and broadcast SUnitDeathPacket. ICharacter exposes IsDead explicitly; creatures
        // signal death via CurrentHealth == 0 (their script sets it).
        NotifyDeathIfApplicable(enc, target, attacker);
        return HitOutcome.Landed;
    }

    /// <summary>
    /// Applies the hit, then reports what it led to, to this service's own instance (#546). A living
    /// character's damage is sent (a dead one's OnHit ignores the hit, and nothing is sent). A creature
    /// its script brought from above 0 to 0 health is killed: once, here, before the caller's encounter
    /// death and death broadcast.
    /// </summary>
    /// <param name="damage">The whole hit, the ward's part included: what the character is told it took.</param>
    /// <param name="life">What is left for health once the ward took its part (#1178).</param>
    private void Hit(IUnit attacker, IUnit target, uint damage, uint life, AbilityId? abilityId, in HitReport report)
    {
        if (target is CharacterEntity character)
        {
            bool wasDead = character.IsDead;
            uint characterHealthBefore = character.CurrentHealth;
            character.OnHit(attacker, life);
            if (!wasDead)
            {
                GainFuryFromDamage(character, characterHealthBefore);
                _outcomes?.CharacterDamaged(character, attacker, damage, abilityId, report);
            }
            return;
        }

        // A creature's script is handed at most the health it has left, so a script that subtracts
        // the damage it is given cannot wrap its uint health past 0 (#588), and only the part its ward left
        // (#1178): a hit the ward took whole hands it 0. Threat above still counts the whole hit.
        uint healthBefore = target.CurrentHealth;

        // The creature script broadcasts the hit itself; for exactly the length of its OnHit the instance
        // marks that broadcast with how the hit went (#506) and adds the ward's part back (#1178).
        if (_outcomes is not null) _outcomes.HitInFlight = report;
        try
        {
            target.OnHit(attacker, target is ICreature ? Math.Min(life, healthBefore) : life);
        }
        finally
        {
            if (_outcomes is not null) _outcomes.HitInFlight = HitReport.None;
        }

        if (target is ICreature creature && healthBefore > 0 && creature.CurrentHealth == 0)
            _outcomes?.CreatureKilled(creature, attacker);
    }

    /// <summary>
    /// #526: a Fury character gains a share of the health it lost, from any source: what it actually lost, not the
    /// damage it was handed, so it stays right once armour reduces a hit (#506). A hit that kills it gains nothing:
    /// GainPower refuses the dead, and death empties Fury anyway.
    /// </summary>
    private void GainFuryFromDamage(CharacterEntity character, uint healthBefore)
    {
        if (character.PowerType != PowerType.Fury)
            return;

        uint lost = healthBefore > character.CurrentHealth ? healthBefore - character.CurrentHealth : 0u;
        character.GainPower(Fury.FromDamageTaken(lost, healthBefore, character.Health, _furyFromDamageTaken));
    }

    /// <summary>
    /// A dodged hit (#506) is still combat: the encounter forms, the creature is given threat as if the hit
    /// had landed for its base, both sides are tagged in combat, and a creature's script is told it was
    /// attacked (<c>AiScript.OnAttacked</c>) so it engages. It deals nothing, so the script is not hit,
    /// nobody gains power, and nobody dies.
    /// </summary>
    private void Dodged(IUnit attacker, IUnit target, float baseThreat, AbilityId? abilityId, DamageType type,
        DamageTypeMask types)
    {
        Encounter enc = ResolveOrSpawn(attacker, target);

        // Sent like a hit, with 0, to whoever would have seen it land; a dead character is sent nothing,
        // as a hit on it would not be.
        if (target is not ICharacter { IsDead: true })
            _outcomes?.HitDodged(attacker, target, abilityId, type, types);

        if (target is ICreature creature)
        {
            CharacterClass attackerClass = (attacker as ICharacter)?.Class ?? CharacterClass.Hunter;
            enc.AddThreat(target, attacker, baseThreat * ClassThreatModifier.Get(attackerClass));

            // The creature fights back as if hit, though it took nothing (#506 review).
            Attacked(creature, attacker);
        }

        if (attacker is ICharacter attackerCharacter) attackerCharacter.MarkCombat();
        if (target is ICharacter targetCharacter) targetCharacter.MarkCombat();
    }

    /// <summary>
    /// Tells the creature's script it was attacked for nothing, waking a World-side creature first so it acts on it
    /// (#979). Any other creature (a test's) is told directly.
    /// </summary>
    private static void Attacked(ICreature creature, IUnit attacker)
    {
        if (creature is Creature scheduled)
            scheduled.OnAttacked(attacker);
        else
            creature.Script?.OnAttacked(attacker);
    }

    /// <summary>What a unit attacks with (#506); a unit that is neither a character nor a creature brings its level alone.</summary>
    private static AttackerCombat AttackerOf(IUnit unit) => unit switch
    {
        CharacterEntity character => character.Combat,
        Creature creature => creature.Combat,
        _ => new AttackerCombat(unit.Level, 0f),
    };

    /// <summary>What a unit defends with (#506); a unit that is neither a character nor a creature has no defences.</summary>
    private static DefenderCombat DefenderOf(IUnit unit) => unit switch
    {
        CharacterEntity character => character.Defence,
        Creature creature => creature.Defence,
        _ => default,
    };

    /// <summary>Whether a unit has a ward (#1178): a maximum above 0. Without one, absorbing and pausing do nothing.</summary>
    private static bool HasWard(IUnit unit) => unit switch
    {
        CharacterEntity character => character.MaxWard > 0,
        Creature creature => creature.MaxWard > 0,
        _ => false,
    };

    /// <summary>
    /// What a unit's ward takes of <paramref name="absorbable" /> (#1178); a unit that is neither a character nor a creature
    /// has none.
    /// </summary>
    private static uint AbsorbWithWard(IUnit unit, uint absorbable, DateTime now) => unit switch
    {
        CharacterEntity character => character.AbsorbWithWard(absorbable, now),
        Creature creature => creature.AbsorbWithWard(absorbable, now),
        _ => 0u,
    };

    /// <summary>Damage a unit took, whether its ward met it or not (#1178): its ward's recharge pauses.</summary>
    private static void WardDamaged(IUnit unit, DateTime now)
    {
        if (unit is CharacterEntity character)
            character.WardDamaged(now);
        else if (unit is Creature creature)
            creature.WardDamaged(now);
    }

    /// <summary>This service's instance is a town, where players are never hostile to one another.</summary>
    private bool InTown => _context is IMapInstance { MapType: MapType.Town };

    /// <summary>A creature whose combat script is walking it home (#610); see <see cref="IReturningHome" />.</summary>
    private static bool IsReturningHome(IUnit unit) =>
        unit is ICreature { Script: IReturningHome { IsReturningHome: true } };

    /// <summary>
    /// A target every hit passes over, as ApplyDamage does before rolling anything: an invulnerable creature, a corpse,
    /// a creature walking home, or a protected arrival.
    /// </summary>
    internal static bool IgnoresHits(IUnit target) =>
        target is ICreature { Invulnerable: true } or ICreature { CurrentHealth: 0 }
        || IsReturningHome(target) || ArrivalProtection.IsProtected(target);

    private void NotifyDeathIfApplicable(Encounter? enc, IUnit target, IUnit? attacker)
    {
        bool dead = (target is ICharacter c && c.IsDead) || target.CurrentHealth == 0;
        if (!dead) return;

        // What a hit set off before this (a crowd control it broke, a kill, through their scripts) may have dealt a
        // further hit that merged its encounter into another (#1168), so the death goes to the fight it now is.
        enc?.Current.OnParticipantDied(target);
        _context?.BroadcastUnitDeath(target, attacker);
        _outcomes?.UnitDied(target);
    }

    private Encounter ResolveOrSpawn(IUnit attacker, IUnit target)
    {
        var existing = (_registry.FindEncounterContaining(attacker)
                            ?? _registry.FindEncounterContaining(target)) as Encounter;
        if (existing is null)
        {
            var enc = (Encounter)_registry.CreateEncounter();
            ClassifyAndAdd(enc, attacker);
            ClassifyAndAdd(enc, target);
            return enc;
        }

        // Classify each participant by TYPE, not by ROLE — so creature-attacks-player and player-attacks-creature
        // both resolve alike. The attacker's first encounter holding both, each on its own side, is the common case,
        // and costs no walk.
        if (EncounterRegistry.Holds(existing, attacker) && EncounterRegistry.Holds(existing, target))
            return existing;

        return ResolveApart(existing, attacker, target);
    }

    /// <summary>
    /// A pair that <paramref name="existing" />, the first encounter found for either, does not hold both of. A pair that
    /// already shares an encounter fights on in it, on every path (#1165): it may share a later one (a spillover, or a
    /// creature's fight the character joined at the cap), and adding a unit to the first would put it in two. Two fights a
    /// character and a creature bring together become one (#1168), within the merge cap; past it, the character joins the
    /// creature's own fight. A creature new to combat goes into the character's encounter, whichever struck, or past the
    /// cap into a spillover of the character's.
    /// </summary>
    private Encounter ResolveApart(Encounter existing, IUnit attacker, IUnit target)
    {
        if (_registry.FindEncounterContainingBoth(attacker, target) is { } shared)
            return shared;

        int cap = _config.MergeCapHostileParticipants;
        if (TryMerge(attacker, target, cap, out Encounter? merged, out Encounter? creatureFight))
            return merged;

        // A creature already fighting keeps its one encounter: the character joins that fight, which adds no hostile
        // to it.
        Encounter into = creatureFight ?? existing;

        // Anything else adds a creature to the encounter found (the character's first, whichever struck), except past
        // the cap: then a spillover of the character's with room takes it before a new one is opened, so creatures
        // pulled past the cap fill spillovers of up to the cap each rather than one encounter apiece. A creature
        // striking a creature, which none does, takes a spillover of the attacker's.
        if (creatureFight is null && (attacker is ICreature || target is ICreature) && existing.Hostiles.Count >= cap)
        {
            IUnit holder = target is ICharacter ? target : attacker;
            into = _registry.FindEncounterWithRoom(holder, cap) ?? (Encounter)_registry.CreateEncounter();
        }

        ClassifyAndAdd(into, attacker);
        ClassifyAndAdd(into, target);
        return into;
    }

    /// <summary>
    /// Merges the fight a creature is in with a fight of the character it meets, whichever struck (#1168), when the two
    /// together hold no more than <paramref name="cap" /> hostiles: the creature is in one fight, the character may be in
    /// several, and the first of the character's that fits is merged. <paramref name="creatureFight" /> is the creature's
    /// fight, if it has one, merged or not.
    /// </summary>
    private bool TryMerge(IUnit attacker, IUnit target, int cap, [NotNullWhen(true)] out Encounter? merged,
        out Encounter? creatureFight)
    {
        merged = null;
        creatureFight = null;
        (IUnit creature, IUnit character) = target is ICreature ? (target, attacker) : (attacker, target);
        if (creature is not ICreature || character is not ICharacter)
            return false;

        creatureFight = _registry.FindEncounterHolding(creature);
        if (creatureFight is null)
            return false;

        // Never the creature's own fight: it would hold the pair, which shares no encounter.
        Encounter? characterFight = _registry.FindEncounterWithRoom(character, cap - creatureFight.Hostiles.Count + 1);
        if (characterFight is null)
            return false;

        merged = _registry.Merge(creatureFight, characterFight);
        return true;
    }

    private static void ClassifyAndAdd(Encounter enc, IUnit unit)
    {
        if (unit is ICreature) enc.AddHostile(unit);
        else if (unit is ICharacter) enc.AddPlayer(unit);
    }

    /// <summary>
    /// Restores the resolved heal, capped at the target's maximum (#164): the ability's heal base (#1178,
    /// <see cref="OffenceMath.HealBase" />: its spell base grown by the healer's level, scaled by its increased healing
    /// alone), then the one crit roll (#506). A heal is never dodged, blocked or reduced by armour. A heal never
    /// lowers health: a unit already at or above its maximum keeps what it has, and the heal restores 0
    /// (#548). A dead unit is never healed. The health change reaches clients through entity replication, as regeneration's
    /// does; a heal that restored more than 0 is also reported to this service's instance, which sends it as
    /// SMSG_UNIT_HEALED with the restored amount and its crit (#506). Heal threat counts only the health actually restored (#531):
    /// <c>restored × HealThreatPerHp × class modifier</c>, split across the hostiles in the target's
    /// encounter. Overheal adds none, so a heal that restores nothing leaves the encounter untouched and
    /// does not even add the healer to it.
    /// </summary>
    public void ApplyHeal(IUnit healer, IUnit target, IAbility ability)
    {
        bool dead = target is ICharacter { IsDead: true } || target.CurrentHealth == 0;
        if (dead) return;

        // #506: resolved past the early return, so a dead target draws no roll; the formula is read once.
        CombatFormula formula = _formula();
        AttackerCombat healerCombat = AttackerOf(healer);
        double baseHeal = OffenceMath.HealBase(healerCombat, ability.Metadata, _random);
        (uint amount, HitResult result) = HitResolver.ResolveHeal(healerCombat, baseHeal, formula, _random);

        uint before = target.CurrentHealth;
        uint after = HealRules.After(before, target.Health, amount); // never lowers health above the maximum (#548)
        target.CurrentHealth = after;

        uint restored = after > before ? after - before : 0;
        if (restored == 0) return;

        // #506: only a heal that restored something is reported, with what it restored, overheal left out.
        _outcomes?.UnitHealed(healer, target, restored, ability.AbilityId, result);

        AddHealThreat(healer, target, restored, ability.Metadata.HealThreatPerHp);
    }

    /// <summary>
    /// Heal threat for health actually restored (#531): <c>restored x perHp x class modifier</c>, split across the
    /// hostiles still fighting in the target's encounter; the healer joins it. A creature walking home takes none
    /// (#610), or it would carry it past its reset; the creatures still fighting share the whole of it.
    /// </summary>
    /// <remarks>
    /// The hostiles are walked in place twice, to count those fighting and then to give each its share, where a list of
    /// them was made for every heal (#883). Nothing in between changes the answer: joining the encounter and adding
    /// threat write the threat lists, never the set of hostiles or whether one is walking home.
    /// </remarks>
    private void AddHealThreat(IUnit healer, IUnit target, uint restored, float healThreatPerHp)
    {
        if (healThreatPerHp <= 0 || restored == 0)
            return;

        var enc = _registry.FindEncounterContaining(target) as Encounter;
        if (enc is null) return;

        int fighting = 0;
        foreach (IUnit hostile in enc.WalkHostiles())
        {
            if (!IsReturningHome(hostile)) fighting++;
        }

        if (fighting == 0) return;

        CharacterClass healerClass = (healer as ICharacter)?.Class ?? CharacterClass.Cleric;
        float perHostile = restored * healThreatPerHp * ClassThreatModifier.Get(healerClass) / fighting;

        if (!enc.Players.Contains(healer)) enc.AddPlayer(healer);
        foreach (IUnit hostile in enc.WalkHostiles())
        {
            if (!IsReturningHome(hostile)) enc.AddThreat(hostile, healer, perHostile);
        }
    }

    /// <summary>
    /// An item's heal (item use): a fixed <paramref name="amount" />, with no stat scaling and no crit,
    /// never raising health past the maximum nor lowering health above it (#548), and never healing the dead. It adds
    /// no heal threat and touches no encounter. A heal that restored more than 0 is reported to this service's
    /// instance like an ability's, with no ability id. Answers the health restored. World-side: not on ICombatService.
    /// </summary>
    public uint RestoreHealth(IUnit healer, IUnit target, uint amount)
    {
        bool dead = target is ICharacter { IsDead: true } || target.CurrentHealth == 0;
        if (dead) return 0;

        uint before = target.CurrentHealth;
        uint after = HealRules.After(before, target.Health, amount);
        target.CurrentHealth = after;

        uint restored = after > before ? after - before : 0;
        if (restored > 0)
            _outcomes?.UnitHealed(healer, target, restored, abilityId: null, HitResult.None);

        return restored;
    }

    /// <summary>What a unit attacks with (#506), for an aura's snapshot. World-side.</summary>
    internal static AttackerCombat AttackerFor(IUnit unit) => AttackerOf(unit);

    /// <summary>
    /// The random every roll of this service draws from, so an aura's base damage roll at apply time comes from the
    /// same source as a hit's. World-side.
    /// </summary>
    internal ICombatRandom Random => _random;

    /// <summary>
    /// One hit an aura deals on its own, outside its run of ticks (auras), such as one its script deals: a single tick,
    /// so its amount is rounded to the nearest point rather than carried. As
    /// <see cref="ApplyPeriodicDamage(in PeriodicHit, ref double, bool)" /> otherwise.
    /// </summary>
    public uint ApplyPeriodicDamage(in PeriodicHit hit)
    {
        double carry = 0d;
        return ApplyPeriodicDamage(hit, ref carry, lastTick: true);
    }

    /// <summary>
    /// One damage tick of a harmful aura (auras), World-side, not on ICombatService: the snapshot's crit is rolled (the
    /// one draw) and the target's mitigation of the snapshot's type taken off (#1178), never evaded or blocked. The
    /// resolved amount is added to <paramref name="carry" /> (the aura's own) and only its whole points are dealt, the fraction kept for the next
    /// tick; the <paramref name="lastTick" /> rounds what is left (AuraRules.TakeTick). A tick worth less than a point
    /// deals nothing and is not reported, though it still counts as combat. A target every hit passes over
    /// (invulnerable, a corpse, a creature walking home) takes nothing, draws nothing and carries nothing. With a
    /// caster present: the encounter forms, the creature gains the caster's threat (the source ability's multiplier,
    /// the class modifier) and is told it was attacked, the caster gains the source ability's power per unit damaged,
    /// both are tagged in combat, and a killing tick credits the caster. With none: the target is hurt, a killing tick
    /// is a kill nobody is credited with, and the target's encounter, if it has one, hears of the death. A creature's
    /// health changes here, not through its script, so its script broadcasts nothing; the instance reports the tick
    /// with its aura. Answers the damage dealt.
    /// </summary>
    public uint ApplyPeriodicDamage(in PeriodicHit hit, ref double carry, bool lastTick)
    {
        IUnit target = hit.Target;
        IUnit? caster = hit.Caster;
        if (IgnoresHits(target) || target is ICharacter { IsDead: true })
            return 0;

        DamageType type = hit.Snapshot.Type;
        (double amount, HitResult result) =
            HitResolver.ResolvePeriodic(hit.Snapshot.Attacker, DefenderOf(target), type, hit.BaseAmount,
                _formula(), _random);
        uint points = AuraRules.TakeTick(amount, ref carry, lastTick);

        // Staff testing still resolves and reports the tick, forms an encounter, and tags combat; only its health loss
        // and what needs positive damage are suppressed.
        uint dealt = target is CharacterEntity { GodMode: true } ? 0u : points;

        // #1178: the ward takes a tick it may (never a bleed's or blight's) before life; any tick pauses its recharge.
        // Everything else reads the whole tick, the ward's part included.
        // The clock is read only for a target with a ward that took damage, as for a hit.
        uint absorbed = 0u;
        if (dealt > 0 && HasWard(target))
        {
            DateTime now = _time.GetUtcNow().UtcDateTime;
            absorbed = HitResolver.WardAbsorbs(type, periodic: true) ? AbsorbWithWard(target, dealt, now) : 0u;
            WardDamaged(target, now);
        }

        uint life = dealt - absorbed;

        // A town never lets players be hostile, so a tick there (an aura brought in from elsewhere) restarts no PvP timer.
        if (dealt > 0 && !InTown && caster is CharacterEntity attackerEntity
            && target is CharacterEntity { IsDead: false } targetEntity)
        {
            _pvp?.OnPlayerHitPlayer(attackerEntity, targetEntity);
        }

        Encounter? enc = PeriodicEncounter(hit, dealt);

        uint healthBefore = target.CurrentHealth;
        if (target is CharacterEntity character)
        {
            character.OnPeriodicHit(life);
            GainFuryFromDamage(character, healthBefore);
        }
        else if (life > 0)
        {
            // Written only when it changes, so a tick that deals nothing does not mark the health for a broadcast.
            target.CurrentHealth = healthBefore - Math.Min(life, healthBefore);
        }

        if (points > 0)
        {
            _outcomes?.PeriodicTick(caster, target, dealt, hit.Aura,
                new HitReport(result, type, DamageTypes.MaskOf(type), absorbed), heal: false);
        }

        if (target is ICreature creature)
        {
            if (healthBefore > 0 && creature.CurrentHealth == 0)
                _outcomes?.CreatureKilled(creature, caster);
            else if (caster is not null)
                Attacked(creature, caster);
        }

        // #770: a tick that dealt something to a unit it left alive may break the crowd control it holds, by chance.
        if (dealt > 0 && !AuraSystem.IsDead(target))
            _outcomes?.DamageTaken(target);

        if (dealt > 0 && healthBefore > 0 && hit.Source.PowerGainPerHit > 0 && caster is CharacterEntity gainer)
            gainer.GainPower(hit.Source.PowerGainPerHit);

        if (caster is ICharacter casterCharacter) casterCharacter.MarkCombat();
        if (target is ICharacter targetCharacter) targetCharacter.MarkCombat();

        NotifyDeathIfApplicable(enc, target, caster);
        return dealt;
    }

    /// <summary>
    /// The encounter a damage tick counts in, and the threat it adds to a creature target. A creature caster never forms
    /// or rejoins an encounter with its ticks: the aura formed one when it landed, and a creature that has since given up
    /// the fight and reset at home must stay out of it. A character caster's tick forms or joins one as a hit does, and a
    /// tick from nobody counts in whatever encounter its target is in.
    /// </summary>
    private Encounter? PeriodicEncounter(in PeriodicHit hit, uint dealt)
    {
        IUnit target = hit.Target;
        IUnit? caster = hit.Caster;
        Encounter? enc = caster is null or ICreature
            ? _registry.FindEncounterContaining(target) as Encounter
            : ResolveOrSpawn(caster, target);
        if (caster is not null && enc is not null && target is ICreature)
        {
            CharacterClass casterClass = (caster as ICharacter)?.Class ?? CharacterClass.Hunter;
            enc.AddThreat(target, caster, dealt * hit.Source.ThreatMultiplier * ClassThreatModifier.Get(casterClass));
        }

        return enc;
    }

    /// <summary>
    /// One heal an aura gives on its own, outside its run of ticks (auras): a single tick, rounded to the nearest point.
    /// As <see cref="ApplyPeriodicHeal(in PeriodicHit, ref double, bool)" /> otherwise.
    /// </summary>
    public uint ApplyPeriodicHeal(in PeriodicHit hit)
    {
        double carry = 0d;
        return ApplyPeriodicHeal(hit, ref carry, lastTick: true);
    }

    /// <summary>
    /// One heal tick of a helpful aura (auras), World-side: the snapshot's crit is rolled (the one draw), the whole
    /// points are taken from the amount and <paramref name="carry" /> as a damage tick takes them, the heal never raises
    /// health past the maximum nor lowers it (#548), and the dead are never healed (nothing drawn or carried). A tick
    /// that restored something is reported with its aura, and gives a present caster heal threat by its source
    /// ability. Answers what it restored.
    /// </summary>
    public uint ApplyPeriodicHeal(in PeriodicHit hit, ref double carry, bool lastTick)
    {
        IUnit target = hit.Target;
        if (target is ICharacter { IsDead: true } || target.CurrentHealth == 0)
            return 0;

        (double amount, HitResult result) =
            HitResolver.ResolvePeriodicHeal(hit.Snapshot.Attacker, hit.BaseAmount, _formula(), _random);
        uint points = AuraRules.TakeTick(amount, ref carry, lastTick);

        uint before = target.CurrentHealth;
        uint after = HealRules.After(before, target.Health, points);
        target.CurrentHealth = after;

        uint restored = after > before ? after - before : 0;
        if (restored == 0)
            return 0;

        _outcomes?.PeriodicTick(hit.Caster, target, restored, hit.Aura, new HitReport(result, default, DamageTypeMask.None, 0u),
            heal: true);
        if (hit.Caster is { } healer)
            AddHealThreat(healer, target, restored, hit.Source.HealThreatPerHp);
        return restored;
    }

    /// <summary>
    /// A harmful aura landed (auras): combat as a dodge is, with nothing dealt. The encounter forms (a fight newly formed
    /// or joined holds the caster at the initial seed on the creature's list; one merged with the caster's seeds nothing,
    /// #1168), a creature's script is told it was attacked so it engages, and both are tagged in combat. Nothing for a
    /// target every hit passes over.
    /// </summary>
    public void EnterAuraCombat(IUnit caster, IUnit target)
    {
        if (IgnoresHits(target))
            return;

        EnterCombatForNothing(caster, target);
    }

    /// <summary>
    /// Combat from an attack that dealt nothing and added no threat (a harmful aura, or an ability with no damage that
    /// landed, #1164): the encounter forms, a creature's script is told it was attacked, both are tagged in combat. Done
    /// twice for one attack (a landed hit, then its aura) it changes nothing more: the encounter already holds both, the
    /// creature already fights the attacker, and the tags are only set again.
    /// </summary>
    private void EnterCombatForNothing(IUnit attacker, IUnit target)
    {
        ResolveOrSpawn(attacker, target);
        if (target is ICreature creature)
            Attacked(creature, attacker);
        if (attacker is ICharacter attackerCharacter) attackerCharacter.MarkCombat();
        if (target is ICharacter targetCharacter) targetCharacter.MarkCombat();
    }

    public void ApplyTaunt(IUnit caster, IUnit target, uint durationMs)
    {
        if (target is not ICreature creature) return;

        // A creature walking home cannot be taunted (#614), as it cannot be hit (#610).
        if (IsReturningHome(creature)) return;

        var enc = _registry.FindEncounterContaining(target) as Encounter;
        if (enc is null) return;

        IReadOnlyDictionary<IUnit, float> threats = enc.GetThreatList(creature);
        float top = 0;
        foreach (float t in threats.Values)
            if (t > top) top = t;

        if (!enc.Players.Contains(caster)) enc.AddPlayer(caster);

        threats.TryGetValue(caster, out float current);
        float deltaToBecomeTop = (top + 1.0f) - current;
        enc.AddThreat(creature, caster, deltaToBecomeTop);

        creature.TauntedBy = caster;
        creature.TauntExpiresAt = _time.GetUtcNow().UtcDateTime.AddMilliseconds(durationMs);
    }

    public void EnterCombat(IUnit hostile, IUnit player)
    {
        if (ArrivalProtection.IsProtected(player)) return;

        // A creature walking home is pulled into no fight until it is home and reset (#614).
        if (IsReturningHome(hostile)) return;

        // Resolve-or-spawn handles both directions; arg order matches the merge logic in ApplyDamage.
        ResolveOrSpawn(player, hostile);
    }

    void IHostileEncounterExit.DropHostileFromEncounter(IUnit hostile)
    {
        var enc = _registry.FindEncounterContaining(hostile) as Encounter;
        enc?.RemoveHostile(hostile);
    }

    public void DropPlayerFromEncounter(IUnit player)
    {
        // A player can be in several encounters (one it joined past the merge cap, #1168): prune it from the
        // participants and every hostile's threat list of each. Idempotent: if the player isn't in any encounter,
        // this is a no-op (called from disconnect/transition exit-paths that fire whether or not the player was
        // ever in combat).
        _registry.RemovePlayerEverywhere(player);
    }

    public void RevivePlayer(IUnit player, Vector3 position)
    {
        // Revive is a character-only operation. A dead creature is not revived at all today — its
        // corpse is removed by ICorpseRemover after ICreatureMetadata.BodyRemoveTimer and that is the
        // end of it.
        if (player is not ICharacter character) return;

        // Note: ICharacter has a Revive() helper but it forces CurrentHealth to Health
        // (full restore). We need a partial revive driven by ReviveHealthFraction, so we
        // clear IsDead directly via the setter and assign the partial HP value ourselves.
        uint maxHealth = character.Health;
        uint reviveHp = (uint)(maxHealth * _config.ReviveHealthFraction);

        // Defensive: a tiny ReviveHealthFraction × small max can truncate to 0. Reviving
        // alive-but-at-0HP would be a degenerate state (death detection treats HP==0 as
        // dead for creatures), so floor to 1 HP whenever the unit has any max health.
        if (reviveHp == 0 && maxHealth > 0) reviveHp = 1;

        // Clear dead-flag first so observers don't see "alive but at 0 HP" mid-revive
        // (mirrors the ordering inside CharacterEntity.Revive()).
        character.IsDead = false;
        character.CurrentHealth = reviveHp;

        // Revived where it is put, a jump from where it died: a query rewound to before it finds no position (#1082).
        (character as CharacterEntity)?.Jump();
        character.Position = position;

        _context?.BroadcastUnitRevive(character, position, reviveHp);
    }

    public IEncounter? GetEncounterFor(IUnit unit) => _registry.FindEncounterContaining(unit);

    // Reused every tick: the encounters to update, copied first because an ended one is disposed
    // mid-loop. The tick path allocates nothing.
    private readonly List<Encounter> _updating = new();

    public void Update(TimeSpan deltaTime)
    {
        _registry.CopyActiveTo(_updating);
        for (int i = 0; i < _updating.Count; i++)
        {
            Encounter enc = _updating[i];
            enc.Update(deltaTime);
            if (enc.ShouldEnd) _registry.Dispose(enc);
        }
        _updating.Clear();
    }
}
