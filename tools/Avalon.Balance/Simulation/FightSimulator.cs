using Avalon.Balance.Config;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;

namespace Avalon.Balance.Simulation;

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
/// characters (their cast, then their own update), the cast system, then creature scripts.
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
    private readonly List<PendingCast> _queue = [];
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

    public FightSimulator(CombatFormula formula, SimPlayer player, IReadOnlyList<SimCreature> creatures,
        IReadOnlyList<CompiledRotationEntry> rotation, ICombatRandom rng, int? coneHits = null)
    {
        _formula = formula;
        Player = player;
        Creatures = creatures;
        _rotation = rotation;
        _rng = rng;
        _coneHits = coneHits;
    }

    public SimPlayer Player { get; }

    public IReadOnlyList<SimCreature> Creatures { get; }

    public double Time => _tick * StepSeconds;

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

        if (caster is SimPlayer player)
            FirePlayer(player, ability);
        else
            FireCreature((SimCreature)caster, ability);
    }

    private void FirePlayer(SimPlayer player, SimAbility ability)
    {
        AbilityMetadata m = ability.Metadata;
        if (m.Affects == AbilityAffects.Ally)
        {
            (uint heal, _) = CombatRules.Heal(player, ability, _formula, _rng);
            _healing += CombatRules.HealPlayer(player, heal);
            return;
        }

        foreach (SimCreature target in TargetsOf(m))
        {
            (uint damage, _) = CombatRules.Damage(player, target, ability, _formula, _rng);
            Add(_dealt, m.Name, CombatRules.HitCreature(player, target, damage, ability));
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
        if (Player.IsDead || ability.Metadata.Affects == AbilityAffects.Ally)
            return;

        (uint damage, _) = CombatRules.Damage(creature, Player, ability, _formula, _rng);
        Add(_taken, $"{creature.Template.Name}: {ability.Name}", CombatRules.HitPlayer(Player, damage));
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
