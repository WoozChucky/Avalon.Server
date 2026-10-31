using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
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
/// its script. Projectile travel time is outside the model: in melee every projectile lands on its first pass, a fan's
/// lanes one after another (B-R15).
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
    private static readonly double s_gcdSeconds = new CombatConfig().GcdMs / 1000d;

    private readonly CombatFormula _formula;
    private readonly IReadOnlyList<CompiledRotationEntry> _rotation;
    private readonly ICombatRandom _rng;
    private readonly int? _coneHits;
    private readonly BalanceData? _data;
    private readonly List<PendingCast> _queue = [];
    private readonly List<InFlight> _projectiles = [];

    // The creatures one projectile cast has hit, every lane of a fan's and its explosion's (B-R15), as the world's
    // ProjectileHits: one set, emptied as each cast starts to land. A cast's lanes all land in one script pass, one after
    // another, so no two casts are ever in it at once.
    private readonly HashSet<SimCreature> _castHits = [];
    private readonly List<CastEvent> _casts = [];
    private readonly Dictionary<string, long> _dealt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _taken = new(StringComparer.Ordinal);
    private long _tick;
    private long _healing;
    private double _starved;
    private double? _firstSpender;
    private double _lastCastStart = double.NegativeInfinity;
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
        if (!Player.IsDead && Player.Casting is null && Time - _lastCastStart >= s_gcdSeconds - 1e-9)
            TryCast();

        foreach (SimAbility ability in Player.Abilities)
        {
            if (ability.CooldownLeft > 0f) ability.CooldownLeft -= Dt;
        }

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

            CostCheck cost = AbilityCost.Check(Player, ability.Metadata, fromLife: Player.Talents.Totals.LifeCost);
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

    /// <summary>
    /// As CharacterEntity.Update in combat (#1181): the in-combat mana rate through PowerRegen.TakeWholePoints with the
    /// fraction carried, the carry dropped whenever the pool cannot regenerate; a cast never pauses it.
    /// </summary>
    private void Regenerate()
    {
        uint current = Player.CurrentPower ?? 0;
        uint max = Player.Power ?? 0;
        double rate = Player.ManaRegen.InCombat;
        if (Player.IsDead || current >= max || !(rate > 0d))
        {
            _regenCarry = 0d;
            return;
        }

        uint amount = PowerRegen.TakeWholePoints(rate * StepSeconds, ref _regenCarry);
        Player.CurrentPower = (uint)Math.Min(max, (ulong)current + amount);
    }

    // ---- casting, shared by both sides (InstanceAbilityCastSystem) ----

    /// <summary>
    /// A player pays when the cast is taken, as InstanceAbilityCastSystem; creatures cast free. With Blood Price (B-R14) the
    /// cost comes from life, which is no damage: no ward meets it, no total counts it, and the mana is untouched.
    /// </summary>
    private void Cast(SimUnit caster, SimAbility ability)
    {
        if (caster is SimPlayer player) AbilityCost.Pay(player, ability.Metadata, fromLife: player.Talents.Totals.LifeCost);

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
    /// As InstanceAbilityCastSystem.TickScripts, after AdvanceQueue: every projectile cast in flight lands, oldest first,
    /// with every lane it was fired with; one whose creature caster died since it was loosed is dropped
    /// (IsAbandonedByCreature). Its targets are chosen now, so a target that died meanwhile draws no roll. By index:
    /// landing fires nothing, so the list is read in place and emptied after.
    /// </summary>
    private void LandProjectiles()
    {
        for (int i = 0; i < _projectiles.Count; i++)
        {
            InFlight projectile = _projectiles[i];
            if (projectile.Caster is SimCreature { IsDead: true })
                continue;

            Resolve(projectile.Caster, projectile.Ability, projectile.Lanes);
        }

        _projectiles.Clear();
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
            _projectiles.Add(new InFlight(caster, ability, LanesOf(ability.Metadata)));
        else
            Resolve(caster, ability, lanes: 1);
    }

    /// <summary>
    /// What a fired cast does: a creature's, to the player; a character's Ally ability, to itself; a Hostile one, to the
    /// creatures its shape takes, a projectile's in each of its <paramref name="lanes" />. A creature holds no capstone, so
    /// its projectile flies one lane.
    /// </summary>
    private void Resolve(SimUnit caster, SimAbility ability, int lanes)
    {
        if (caster is not SimPlayer player)
            FireCreature((SimCreature)caster, ability);
        else if (ability.Metadata.Affects == AbilityAffects.Ally)
            Help(player, ability);
        else if (ability.Metadata.Shape == AbilityShape.Projectile)
            Fly(player, ability, lanes);
        else
            Strike(player, ability);
    }

    /// <summary>As InstanceAbilityCastSystem.LanesOf: a projectile's count (B-R15: a fan's), else 1.</summary>
    private static int LanesOf(AbilityMetadata m) =>
        m.Shape == AbilityShape.Projectile ? Math.Max(1, m.ProjectileCount) : 1;

    /// <summary>
    /// An Ally ability on the character, as AbilityEffect.Apply: its heal when Effects has one, then a second heal at the
    /// capstone's share (B-R15), each with its own crit roll, then its aura.
    /// </summary>
    private void Help(SimPlayer player, SimAbility ability)
    {
        AbilityMetadata m = ability.Metadata;
        if (AbilityRules.HasDirectEffect(m.Effects, m.Affects))
        {
            Heal(player, ability, 100f);
            if (m.SecondHitEffectiveness > 0f)
                Heal(player, ability, m.SecondHitEffectiveness);
        }

        ApplyAura(player, player, ability);
    }

    /// <summary>As CombatService.ApplyHeal: nothing, and no draw, on the dead.</summary>
    private void Heal(SimPlayer player, SimAbility ability, float amountPct)
    {
        if (player.IsDead)
            return;

        (uint heal, _) = CombatRules.Heal(player, ability, _formula, _rng, amountPct);
        _healing += CombatRules.HealPlayer(player, heal);
    }

    /// <summary>
    /// A circle or a cone, as AbilityEffect.ApplyToAll. Everyone is in melee, the pack in the nearest-first order of the
    /// world's hit queries (B-R8): a circle affects the first creatures hits do not pass over, up to its MaxTargets; a cone
    /// the same, and at most the scenario's coneHits. A MaxTargets of 0 is no cap. Area, cone reach and cone arc shape the
    /// metadata but take no other creature in melee (B-R15). Creatures walking home are not modelled: in melee nobody
    /// leashes.
    /// </summary>
    private void Strike(SimPlayer player, SimAbility ability)
    {
        AbilityMetadata m = ability.Metadata;
        int cap = m.MaxTargets > 0 ? m.MaxTargets : int.MaxValue;
        if (m.Shape == AbilityShape.Cone && _coneHits is { } coneHits)
            cap = Math.Min(cap, coneHits);

        int affected = 0;
        for (int i = 0; i < Creatures.Count && affected < cap; i++)
        {
            SimCreature target = Creatures[i];
            if (IgnoresHits(target))
                continue;

            affected++;
            Apply(player, ability, target);
        }
    }

    /// <summary>
    /// A projectile cast landing in melee, as ProjectileAbilityScript.Update lane by lane (B-R8, B-R15). Every lane shares
    /// the cast's set of creatures hit, so a creature is hit at most once per cast, and a lane with none left hits nothing
    /// and draws nothing. A lane without Pierce ends on the first creature hits do not pass over and the cast has not hit;
    /// with Pierce it hits each of them, ending on its own MaxTargets-th when that is above 0. A lane that ends on a
    /// creature explodes there when the cast has an ExplosionRadius.
    /// </summary>
    private void Fly(SimPlayer player, SimAbility ability, int lanes)
    {
        AbilityMetadata m = ability.Metadata;
        _castHits.Clear();
        for (int lane = 0; lane < lanes; lane++)
        {
            int struck = 0;   // this lane's hits, for MaxTargets: the lanes share the set, never the count
            for (int i = 0; i < Creatures.Count; i++)
            {
                SimCreature target = Creatures[i];
                if (_castHits.Contains(target) || IgnoresHits(target))
                    continue;

                _castHits.Add(target);
                struck++;
                bool last = !m.Pierce || (m.MaxTargets > 0 && struck >= m.MaxTargets);
                Apply(player, ability, target);
                if (!last)
                    continue;

                if (m.ExplosionRadius > 0f)
                    Explode(player, ability, target);
                break;
            }
        }
    }

    /// <summary>
    /// The explosion where a lane ended on <paramref name="struck" />, as ProjectileAbilityScript.Explode (B-R15): in melee
    /// its radius reaches the whole pack, so every other creature hits do not pass over and the cast has not hit is hit
    /// once, at ExplosionEffectiveness % with the Area tag, and joins the cast's set. Damage only: no aura, no second hit.
    /// </summary>
    private void Explode(SimPlayer player, SimAbility ability, SimCreature struck)
    {
        var shaping = new HitShaping(ability.Metadata.ExplosionEffectiveness, AbilityTags.Area);
        for (int i = 0; i < Creatures.Count; i++)
        {
            SimCreature target = Creatures[i];
            if (ReferenceEquals(target, struck) || _castHits.Contains(target) || IgnoresHits(target))
                continue;

            _castHits.Add(target);
            Hit(player, ability, target, shaping);
        }
    }

    /// <summary>
    /// A Hostile ability on one creature its shape affects, as AbilityEffect.Apply: the whole hit; then, with a second hit
    /// (B-R15), the same again at that share with its own rolls, unless the first was passed over or killed the creature,
    /// so a corpse draws nothing; then the aura once, placed after both hits, when the first landed (or the ability is
    /// Unavoidable) or else the second did.
    /// </summary>
    private void Apply(SimPlayer player, SimAbility ability, SimCreature target)
    {
        AbilityMetadata m = ability.Metadata;
        HitOutcome first = Hit(player, ability, target, HitShaping.Whole);
        bool landed = first == HitOutcome.Landed || (m.Flags & AbilityFlags.Unavoidable) != AbilityFlags.None;

        if (m.SecondHitEffectiveness > 0f && first != HitOutcome.Ignored && !target.IsDead)
        {
            landed |= Hit(player, ability, target, new HitShaping(m.SecondHitEffectiveness, AbilityTags.None))
                      == HitOutcome.Landed;
        }

        if (landed)
            ApplyAura(player, target, ability);
    }

    /// <summary>
    /// One hit of a character's ability, as CombatService.ApplyAbilityHit: nothing, and no draw, on a creature hits pass
    /// over; the direct amount at <paramref name="shaping" /> when Effects has one (its ward first, B-R6), a dodged hit
    /// landing nothing; else the evasion roll alone (#1164). A hit that kills ends every aura its target held.
    /// </summary>
    private HitOutcome Hit(SimPlayer player, SimAbility ability, SimCreature target, in HitShaping shaping)
    {
        if (IgnoresHits(target))
            return HitOutcome.Ignored;

        AbilityMetadata m = ability.Metadata;
        if (!AbilityRules.HasDirectEffect(m.Effects, m.Affects))
            return CombatRules.Lands(player, target, ability, _formula, _rng) ? HitOutcome.Landed : HitOutcome.Dodged;

        HitDamage hit = CombatRules.Damage(player, target, ability, _formula, _rng, shaping);
        Add(_dealt, m.Name, CombatRules.Land(target, hit, Now.UtcDateTime).Total);   // a dodged hit (0) lands nothing
        if ((hit.Result & HitResult.Dodged) != HitResult.None)
            return HitOutcome.Dodged;

        if (target.IsDead)
            ClearAuras(target);
        return HitOutcome.Landed;
    }

    /// <summary>
    /// As CombatService.IgnoresHits in melee: a corpse or an invulnerable creature, on which CombatService returns before
    /// any draw (Hostility never offers the latter), so a seeded run draws the server's sequence.
    /// </summary>
    private static bool IgnoresHits(SimCreature creature) => creature.IsDead || creature.Template.Invulnerable;

    private void FireCreature(SimCreature creature, SimAbility ability)
    {
        AbilityMetadata m = ability.Metadata;
        if (Player.IsDead || m.Affects == AbilityAffects.Ally)
            return;

        if (AbilityRules.HasDirectEffect(m.Effects, m.Affects))
        {
            HitDamage hit = CombatRules.Damage(creature, Player, ability, _formula, _rng);
            Add(_taken, $"{creature.Template.Name}: {ability.Name}", CombatRules.Land(Player, hit, Now.UtcDateTime).Total);
            if ((hit.Result & HitResult.Dodged) != HitResult.None)
                return;

            if (Player.IsDead)
            {
                ClearAuras(Player);
                return;
            }
        }
        else if (!CombatRules.Lands(creature, Player, ability, _formula, _rng))
        {
            return;
        }

        ApplyAura(creature, Player, ability);
    }

    // ---- 2a. auras (AuraSystem): applied after the hit, ticked right after the cast system ----

    /// <summary>
    /// As AuraSystem.Apply for an ability's aura: the copy held is renewed (stacked when its row allows), else a new one
    /// is added; the snapshot comes from the caster now, its base damage roll drawn from the fight's random right after
    /// the hit's draws, where the server draws it, and its ticks typed as the ability's hit is, else the caster's base
    /// type (B-R7). A dead target gets none. The server's cap on auras per unit is never reached here: no fight holds that
    /// many kinds of aura. The source ability's capstone factors apply as there (B-R15), never to crowd control: the row's
    /// duration lengthened by its AuraDurationPct, so a periodic aura holds the extra ticks that time gives while its
    /// snapshot still divides by the row's own tick count, and a Stack row's cap raised by its AuraStacksBonus.
    /// </summary>
    private void ApplyAura(SimUnit caster, SimUnit target, SimAbility ability)
    {
        if (_data is null || target.IsDead || ability.Template.AuraId is not { } id
            || !_data.Auras.TryGetValue(id, out AuraTemplate? aura))
        {
            return;
        }

        SimAura? held = target.Auras.FirstOrDefault(a => a.Template.Id.Value == aura.Id.Value
            && (!AuraRules.KeysByCaster(aura.Stacking) || ReferenceEquals(a.Caster, caster)));
        AbilityMetadata m = ability.Metadata;
        bool controls = aura.Kind == AuraKind.Harmful && AuraRules.IsCrowdControl(aura);
        uint stackBonus = controls ? 0u : m.AuraStacksBonus;
        uint durationMs = controls ? aura.DurationMs : AuraRules.Lengthen(aura.DurationMs, m.AuraDurationPct);
        AuraSnapshot snapshot = AuraRules.Snapshot(aura, caster.Attack, m, HitResolver.TypeOf(m.DamageType, caster.Attack),
            _rng);
        var schedule = AuraSchedule.Start(Now, durationMs, aura.TickIntervalMs);

        if (held is not null)
        {
            uint stacks = AuraRules.NextStacks(aura.Stacking, held.Stacks, aura.MaxStacks, stackBonus);
            bool stacked = stacks > held.Stacks;
            held.Renew(caster, stacks, snapshot, schedule);
            if (stacked) RefreshStats(target, aura);
            return;
        }

        target.Auras.Add(new SimAura(aura, caster, 1, snapshot, schedule));
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
    /// One tick, as CombatService.ApplyPeriodicDamage and ApplyPeriodicHeal, resolved from the aura's own snapshot (B-R7):
    /// one crit roll, the mitigation of the snapshot's type on damage, never a dodge or a block; only the whole points of
    /// the amount and the aura's carry are dealt or healed (AuraRules.TakeTick), a damage tick meeting the ward as its type
    /// allows (B-R6). No tick gives power (#1181).
    /// </summary>
    private void PeriodicTick(SimUnit unit, SimAura aura, bool lastTick)
    {
        float amount = aura.Snapshot.PerTickPerStack * aura.Stacks;
        double carry = aura.PeriodicCarry;
        switch (aura.Template.PeriodicKind)
        {
            case AuraPeriodicKind.Damage:
            {
                (double damage, _) = HitResolver.ResolvePeriodic(aura.Snapshot, unit.Defence, amount, _formula, _rng);
                uint points = AuraRules.TakeTick(damage, ref carry, lastTick);
                uint lost = CombatRules.LandTick(unit, points, aura.Snapshot.Type, Now.UtcDateTime).Total;
                Add(unit is SimPlayer ? _taken : _dealt, aura.Template.Name, lost);

                break;
            }
            case AuraPeriodicKind.Heal when unit is SimPlayer player:
            {
                (double heal, _) = HitResolver.ResolvePeriodicHeal(aura.Snapshot, amount, _formula, _rng);
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
            player.ApplyAuraStats(_data, Now.UtcDateTime);
        else if (unit is SimCreature creature)
            creature.ApplyAuraStats(Now.UtcDateTime);
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

    /// <summary>A projectile cast in flight, with the lanes it was fired with.</summary>
    private readonly record struct InFlight(SimUnit Caster, SimAbility Ability, int Lanes);

    /// <summary>What one hit did, as the world's HitOutcome: passed over (no draw), dodged, or landed.</summary>
    private enum HitOutcome
    {
        Ignored,
        Dodged,
        Landed,
    }
}
