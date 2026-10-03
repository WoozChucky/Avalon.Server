using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;

namespace Avalon.Balance.Core;

public sealed record CastEvent(double StartSeconds, double FiredSeconds, string Caster, uint AbilityId);

public sealed record FightResult(
    bool Won,
    double Seconds,
    double HealthLeftPct,
    double StarvedSeconds,
    double? FirstSpenderSeconds,
    IReadOnlyDictionary<string, long> DamageDealt,
    IReadOnlyDictionary<string, long> DamageTaken,
    long Healing,
    IReadOnlyList<CastEvent> Casts);

/// <summary>
/// One player against a pack, one server tick at a time. The order inside a tick is MapInstance.Update's:
/// characters (their cast, then their own update), the cast system, the auras, then creature scripts. Circles and cones hit
/// when they fire, as their scripts do in Prepare. A projectile hits in the cast system's script pass, as
/// ProjectileAbilityScript does in Update: after every due wind-up has fired, on the tick it was loosed for one
/// loosed before that pass (a character's instant cast, any wind-up), on the next tick for one a creature looses in
/// its script. Projectile travel time is outside the model: in melee every projectile lands on its first pass.
/// </summary>
public sealed class FightSimulator
{
    /// <summary>
    /// The world server's tick: 60 Hz, so cooldowns, the global cooldown, cast times and the event order's one-tick
    /// offsets are counted as the server counts them. Power regen carries its fraction, so it does not depend on it.
    /// </summary>
    public const double StepSeconds = 1d / 60d;

    public const double MaxSeconds = 300d;

    private const float Dt = (float)StepSeconds;
    private static readonly double GcdSeconds = new CombatConfig().GcdMs / 1000d;

    private readonly RegenConfiguration _regen = new();
    private readonly CombatFormula _formula;
    private readonly IReadOnlyList<CompiledRotationEntry> _rotation;
    private readonly ICombatRandom _rng;
    private readonly int? _coneHits;
    private readonly BalanceData? _data;
    private readonly List<PendingCast> _queue = [];
    private readonly List<(SimUnit Caster, SimAbility Ability)> _projectiles = [];
    private readonly List<CastEvent> _casts = [];
    private readonly Dictionary<string, long> _dealt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _taken = new(StringComparer.Ordinal);
    private long _tick;
    private long _healing;
    private double _starved;
    private double? _firstSpender;
    private double _lastCastStart = double.NegativeInfinity;
    private double _lastCasting = double.NegativeInfinity;
    private double _regenCarry;   // PowerRegen's fraction of a point, as CharacterEntity keeps it

    /// <param name="data">The data auras are read from (an ability's aura, a character's stats refresh); none applies without it.</param>
    public FightSimulator(CombatFormula formula, SimPlayer player, IReadOnlyList<SimCreature> creatures,
        IReadOnlyList<CompiledRotationEntry> rotation, ICombatRandom rng, int? coneHits = null, BalanceData? data = null)
    {
        _formula = formula;
        Player = player;
        Creatures = creatures;
        _rotation = rotation;
        _rng = rng;
        _coneHits = coneHits;
        _data = data;
    }

    /// <summary>
    /// The instant the simulated clock starts at, which aura schedules are timed on; a tick's time is this plus its
    /// seconds in whole 100 ns units (<see cref="Now" />).
    /// </summary>
    public static readonly DateTimeOffset Epoch = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public SimPlayer Player { get; }

    public IReadOnlyList<SimCreature> Creatures { get; }

    public double Time => _tick * StepSeconds;

    /// <summary>This tick's time on the aura clock: <see cref="Epoch" /> plus the tick's seconds, rounded to 100 ns units.</summary>
    public DateTimeOffset Now =>
        Epoch + TimeSpan.FromTicks((long)Math.Round(_tick * (double)TimeSpan.TicksPerSecond * StepSeconds));

    public bool Over => Player.IsDead || Creatures.All(c => c.IsDead) || Time >= MaxSeconds - 1e-9;

    public FightResult Run()
    {
        while (!Over) Tick();
        return Result();
    }

    public void Tick()
    {
        PlayerPhase();
        CastPhase();
        AuraPhase();
        CreaturePhase();
        _tick++;
    }

    public FightResult Result() => new(
        Won: !Player.IsDead && Creatures.All(c => c.IsDead),
        Seconds: Time,
        HealthLeftPct: Player.Health == 0 ? 0 : Player.CurrentHealth * 100d / Player.Health,
        StarvedSeconds: _starved,
        FirstSpenderSeconds: _firstSpender,
        DamageDealt: new Dictionary<string, long>(_dealt, StringComparer.Ordinal),
        DamageTaken: new Dictionary<string, long>(_taken, StringComparer.Ordinal),
        Healing: _healing,
        Casts: _casts.ToList());

    // ---- 1. the character: its cast (CastAbilityHandler's checks), then CharacterEntity.Update ----

    private void PlayerPhase()
    {
        if (!Player.IsDead && Player.Casting is null && Time - _lastCastStart >= GcdSeconds - 1e-9)
            TryCast();

        foreach (SimAbility ability in Player.Abilities)
        {
            if (ability.CooldownLeft > 0f) ability.CooldownLeft -= Dt;
        }

        if (Player.Casting is not null) _lastCasting = Time;
        Regenerate();
    }

    private void TryCast()
    {
        bool first = true;
        foreach (CompiledRotationEntry entry in _rotation)
        {
            SimAbility ability = Player.Ability(entry.AbilityId);
            if (!ability.Ready || !entry.When.All(c => c.Holds(Read(c.Stat))))
                continue;

            CostCheck cost = AbilityCost.Check(Player, ability.Metadata);
            if (first && cost == CostCheck.NotEnoughPower) _starved += StepSeconds;
            first = false;
            if (cost != CostCheck.Payable)
                continue;

            if (ability.Metadata.Cost > 0) _firstSpender ??= Time;
            _lastCastStart = Time;
            Cast(Player, ability);
            return;
        }
    }

    private double Read(string stat) => stat switch
    {
        "targetsAlive" => Creatures.Count(c => !c.IsDead),
        "healthPct" => Player.Health == 0 ? 0 : Player.CurrentHealth * 100d / Player.Health,
        "power" => Player.CurrentPower ?? 0,
        _ => (Player.Power ?? 0) == 0 ? 0 : (Player.CurrentPower ?? 0) * 100d / Player.Power!.Value,
    };

    /// <summary>As CharacterEntity.Update: PowerRegen with the fraction carried, the carry dropped whenever the pool cannot regenerate.</summary>
    private void Regenerate()
    {
        uint current = Player.CurrentPower ?? 0;
        uint max = Player.Power ?? 0;
        if (Player.IsDead || Player.PowerType is not (PowerType.Mana or PowerType.Energy) || current >= max
            || Player.RegenStat == 0)
        {
            _regenCarry = 0d;
            return;
        }

        bool suppressed = Time - _lastCasting < _regen.PowerRegenCastSuppressSeconds;
        uint amount = PowerRegen.Amount(_regen, Player.RegenStat, inCombat: true, suppressed, StepSeconds, ref _regenCarry);
        Player.CurrentPower = Math.Min(max, current + amount);
    }

    // ---- casting, shared by both sides (InstanceAbilityCastSystem) ----

    private void Cast(SimUnit caster, SimAbility ability)
    {
        if (caster is SimPlayer) AbilityCost.Pay(caster, ability.Metadata);   // creatures cast free

        if (ability.Metadata.CastTime > 0f)
        {
            var cast = new PendingCast(caster, ability, CombatRules.CastTime(caster, ability), Time);
            caster.Casting = cast;
            _queue.Add(cast);
            return;
        }

        Fire(caster, ability, Time);
    }

    private void CastPhase()
    {
        foreach (PendingCast cast in _queue.ToList())
        {
            cast.TimeLeft -= Dt;
            if (cast.Caster is SimCreature { IsDead: true })
            {
                Drop(cast);
                continue;
            }

            if (cast.TimeLeft > 0f)
                continue;

            Drop(cast);
            if (!cast.Caster.IsDead)
                Fire(cast.Caster, cast.Ability, cast.Started);
        }

        LandProjectiles();
    }

    /// <summary>
    /// As InstanceAbilityCastSystem.TickScripts, after AdvanceQueue: every projectile in flight lands, oldest first;
    /// one whose creature caster died since it was loosed is dropped (IsAbandonedByCreature). Its targets are chosen
    /// now, so a target that died meanwhile draws no roll.
    /// </summary>
    private void LandProjectiles()
    {
        if (_projectiles.Count == 0)
            return;

        (SimUnit Caster, SimAbility Ability)[] inFlight = [.. _projectiles];
        _projectiles.Clear();
        foreach ((SimUnit caster, SimAbility ability) in inFlight)
        {
            if (caster is SimCreature { IsDead: true })
                continue;

            Resolve(caster, ability);
        }
    }

    private void Drop(PendingCast cast)
    {
        _queue.Remove(cast);
        cast.Caster.Casting = null;
    }

    private void Fire(SimUnit caster, SimAbility ability, double started)
    {
        ability.CooldownLeft = CombatRules.CooldownAfterFire(caster, ability);
        _casts.Add(new CastEvent(started, Time, caster.Name, ability.Id));

        if (ability.Metadata.Shape == AbilityShape.Projectile)
            _projectiles.Add((caster, ability));
        else
            Resolve(caster, ability);
    }

    private void Resolve(SimUnit caster, SimAbility ability)
    {
        if (caster is SimPlayer player)
            FirePlayer(player, ability);
        else
            FireCreature((SimCreature)caster, ability);
    }

    /// <summary>
    /// As AbilityEffect.Apply for each unit the shape affects: the direct amount when Effects has one, then the aura. A
    /// dodged hit applies no aura, and a hit that kills ends every aura its target held.
    /// </summary>
    private void FirePlayer(SimPlayer player, SimAbility ability)
    {
        AbilityMetadata m = ability.Metadata;
        bool direct = AbilityRules.HasDirectEffect(m.Effects, m.Affects);
        if (m.Affects == AbilityAffects.Ally)
        {
            if (direct)
            {
                (uint heal, _) = CombatRules.Heal(player, ability, _formula, _rng);
                _healing += CombatRules.HealPlayer(player, heal);
            }

            ApplyAura(player, player, ability);
            return;
        }

        foreach (SimCreature target in TargetsOf(m))
        {
            if (direct)
            {
                (uint damage, HitResult result) = CombatRules.Damage(player, target, ability, _formula, _rng);
                Add(_dealt, m.Name, CombatRules.HitCreature(player, target, damage, ability));
                if ((result & HitResult.Dodged) != HitResult.None)
                    continue;

                if (target.IsDead)
                {
                    ClearAuras(target);
                    continue;
                }
            }

            ApplyAura(player, target, ability);
        }
    }

    /// <summary>
    /// Everyone is in melee: circles and cones reach every living creature (a cone up to coneHits), a projectile the
    /// first, or all when it pierces. Only a target the server would roll against is returned: CombatService returns
    /// before any draw for a corpse or an invulnerable creature (Hostility never offers the latter), so a seeded run
    /// draws the server's sequence. Creatures walking home are not modelled: in melee nobody leashes.
    /// </summary>
    private List<SimCreature> TargetsOf(AbilityMetadata m)
    {
        IEnumerable<SimCreature> living = Creatures.Where(c => !c.IsDead && !c.Template.Invulnerable);
        return m.Shape switch
        {
            AbilityShape.Cone when _coneHits is { } cap => living.Take(cap).ToList(),
            AbilityShape.Projectile when !m.Pierce => living.Take(1).ToList(),
            _ => living.ToList(),
        };
    }

    private void FireCreature(SimCreature creature, SimAbility ability)
    {
        AbilityMetadata m = ability.Metadata;
        if (Player.IsDead || m.Affects == AbilityAffects.Ally)
            return;

        if (AbilityRules.HasDirectEffect(m.Effects, m.Affects))
        {
            (uint damage, HitResult result) = CombatRules.Damage(creature, Player, ability, _formula, _rng);
            Add(_taken, $"{creature.Template.Name}: {ability.Name}", CombatRules.HitPlayer(Player, damage));
            if ((result & HitResult.Dodged) != HitResult.None)
                return;

            if (Player.IsDead)
            {
                ClearAuras(Player);
                return;
            }
        }

        ApplyAura(creature, Player, ability);
    }

    // ---- 2a. auras (AuraSystem): applied after the hit, ticked right after the cast system ----

    /// <summary>
    /// As AuraSystem.Apply for an ability's aura: the copy held is renewed (stacked when its row allows), else a new one
    /// is added; the snapshot comes from the caster now, its base damage roll drawn from the fight's random right after
    /// the hit's draws, where the server draws it. A dead target gets none. The server's cap on auras per unit is never
    /// reached here: no fight holds that many kinds of aura.
    /// </summary>
    private void ApplyAura(SimUnit caster, SimUnit target, SimAbility ability)
    {
        if (_data is null || target.IsDead || ability.Template.AuraId is not { } id
            || !_data.Auras.TryGetValue(id, out AuraTemplate? aura))
            return;

        SimAura? held = target.Auras.FirstOrDefault(a => a.Template.Id.Value == aura.Id.Value
            && (!AuraRules.KeysByCaster(aura.Stacking) || ReferenceEquals(a.Caster, caster)));
        AuraSnapshot snapshot = AuraRules.Snapshot(aura, caster.Attack, _rng);
        AuraSchedule schedule = AuraSchedule.Start(Now, aura.DurationMs, aura.TickIntervalMs);
        uint gain = (uint)Math.Max(0, ability.Metadata.PowerGainPerHit);

        if (held is not null)
        {
            uint stacks = AuraRules.NextStacks(aura.Stacking, held.Stacks, aura.MaxStacks);
            bool stacked = stacks > held.Stacks;
            held.Renew(caster, stacks, snapshot, schedule, gain);
            if (stacked) RefreshStats(target, aura);
            return;
        }

        target.Auras.Add(new SimAura(aura, caster, 1, snapshot, schedule, gain));
        RefreshStats(target, aura);
    }

    /// <summary>
    /// As AuraSystem.Update: the player, then each creature; a dead unit loses every aura; each aura, in the order
    /// applied, takes the ticks it is owed now (its last tick rounding what its carry holds), then ends once its time is
    /// up; a tick that kills ends every aura on the unit. The simulator steps every server tick and is never late, so it
    /// does not model the server's catch-up after a stall (each aura's owed ticks paid together, aura by aura).
    /// </summary>
    private void AuraPhase()
    {
        DateTimeOffset now = Now;
        TickAuras(Player, now);
        foreach (SimCreature creature in Creatures)
            TickAuras(creature, now);
    }

    private void TickAuras(SimUnit unit, DateTimeOffset now)
    {
        if (unit.Auras.Count == 0)
            return;

        if (unit.IsDead)
        {
            ClearAuras(unit);
            return;
        }

        foreach (SimAura aura in unit.Auras.ToList())
        {
            int due = aura.Schedule.Due(now);
            for (int k = 0; k < due && !unit.IsDead; k++)
            {
                aura.Schedule = aura.Schedule.AfterTicks(1);
                PeriodicTick(unit, aura, lastTick: aura.Schedule.TicksLeft == 0);
            }

            if (unit.IsDead)
            {
                ClearAuras(unit);
                return;
            }

            if (aura.Schedule.Expired(now))
            {
                unit.Auras.Remove(aura);
                RefreshStats(unit, aura.Template);
            }
        }
    }

    /// <summary>
    /// One tick, as CombatService.ApplyPeriodicDamage and ApplyPeriodicHeal: one crit roll, armour on damage, never a
    /// dodge or a block; only the whole points of the amount and the aura's carry are dealt or healed
    /// (AuraRules.TakeTick). A damage tick on the player gains Fury from the health it lost, tick by tick; one on a
    /// creature gives its caster, while standing, the source ability's power.
    /// </summary>
    private void PeriodicTick(SimUnit unit, SimAura aura, bool lastTick)
    {
        float amount = aura.Snapshot.PerTickPerStack * aura.Stacks;
        double carry = aura.PeriodicCarry;
        switch (aura.Template.PeriodicKind)
        {
            case AuraPeriodicKind.Damage:
            {
                (double damage, _) = HitResolver.ResolvePeriodic(aura.Snapshot.Attacker, unit.Defence, amount, _formula, _rng);
                uint points = AuraRules.TakeTick(damage, ref carry, lastTick);
                if (unit is SimPlayer player)
                    Add(_taken, aura.Template.Name, CombatRules.HitPlayer(player, points));
                else
                    Add(_dealt, aura.Template.Name, CombatRules.PeriodicHitCreature((SimCreature)unit, points,
                        aura.Caster is SimPlayer { IsDead: false } caster ? caster : null, aura.PowerGainPerHit));
                break;
            }
            case AuraPeriodicKind.Heal when unit is SimPlayer player:
            {
                (double heal, _) = HitResolver.ResolvePeriodicHeal(aura.Snapshot.Attacker, amount, _formula, _rng);
                _healing += CombatRules.HealPlayer(player, AuraRules.TakeTick(heal, ref carry, lastTick));
                break;
            }
        }

        aura.PeriodicCarry = carry;
    }

    /// <summary>Death ends every aura the unit holds, its stats refreshed when any of them modified them.</summary>
    private void ClearAuras(SimUnit unit)
    {
        if (unit.Auras.Count == 0)
            return;

        bool modified = unit.Auras.Any(a => a.Template.Modifiers.Count > 0);
        unit.Auras.Clear();
        if (modified) RefreshStats(unit, null);
    }

    /// <summary>
    /// As AuraStatsRefresh after an aura with stat modifiers changed (<paramref name="changed" /> null for any): a
    /// character's stats refreshed, a creature's auras folded.
    /// </summary>
    private void RefreshStats(SimUnit unit, AuraTemplate? changed)
    {
        if (changed is not null && changed.Modifiers.Count == 0)
            return;

        if (unit is SimPlayer player && _data is not null)
            player.ApplyAuraStats(_data);
        else if (unit is SimCreature creature)
            creature.ApplyAuraStats();
    }

    // ---- 3. creature scripts (CreatureCombatScript: cooldowns first, then a choice) ----

    private void CreaturePhase()
    {
        foreach (SimCreature creature in Creatures)
        {
            if (creature.IsDead) continue;

            foreach (SimAbility ability in creature.Abilities)
            {
                if (ability.CooldownLeft > 0f) ability.CooldownLeft -= Dt;
            }

            if (creature.Casting is not null || Player.IsDead) continue;
            if (creature.Choose() is { } choice) Cast(creature, choice);
        }
    }

    private static void Add(Dictionary<string, long> totals, string key, uint amount) =>
        totals[key] = totals.GetValueOrDefault(key) + amount;
}
