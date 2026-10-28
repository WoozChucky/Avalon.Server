using System.Reflection;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Abilities;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The hits a capstone adds (#1181 Part D), each on the real combat service of one forest instance as production builds
/// it (<see cref="ScenarioWorld.NewForestInstance" />), with a god-mode Warrior and Wizard at its entry and boars with no
/// script beside them (a hit takes no health off one, so none dies):
/// <list type="bullet">
/// <item><see cref="Hit_once" />, the baseline: <see cref="AbilityEffect.Apply" />, an unshaped Cleave (200) on a boar.</item>
/// <item><see cref="Hit_twice" />: the same Cleave shaped with <c>SecondHit</c> 50, as Aftershock shapes Ground Slam:
/// the first hit, then the second at half the amount with its own rolls.</item>
/// <item><see cref="Explode_among_five" />: a Rimeburst Frostbolt's (320, <c>ImpactExplosion</c> 30, 60) last-hit
/// branch, as <see cref="ProjectileAbilityScript" />'s flight runs it when it ends on a boar: the impact told, the
/// boar hit, then the explosion's fired circle and its hit on each of five more boars inside its 3 m.</item>
/// </list>
/// Every row is expected to allocate nothing once warm: a hit, a second hit and an explosion take nothing from the heap.
/// </summary>
/// <remarks>
/// Setup fails the run unless <see cref="Hit_twice" /> hits twice (counted through an arena that counts the hits it
/// passes on) and <see cref="Explode_among_five" /> hits the struck boar and the five, and nothing else. The explosion is
/// private to the projectile script, so the benchmark binds it, and the projectile's hit set it walks, by reflection
/// once at setup; the set is emptied before each row so every burst hits the five again, and the hit query's lists are
/// recycled as the instance recycles them at the start of each tick, which no row runs.
/// </remarks>
[MemoryDiagnoser]
public class CapstoneBenchmarks
{
    private const uint BoarHealth = 100_000_000;
    private const float AroundStruck = 1.5f;

    private ScenarioWorld _world = null!;
    private MapInstance _instance = null!;
    private CharacterEntity _warrior = null!;
    private CharacterEntity _wizard = null!;
    private GameAbility _cleave = null!;
    private GameAbility _aftershockCleave = null!;
    private GameAbility _rimeburst = null!;
    private Creature _boar = null!;
    private Creature _struck = null!;
    private ProjectileAbilityScript _projectile = null!;
    private HashSet<ObjectGuid> _projectileHits = null!;
    private UnitHitQuery _query = null!;
    private Action<Vector3, IUnit> _explode = null!;
    private Vector3 _hitPoint;

    [GlobalSetup]
    public void Setup()
    {
        _world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        _instance = _world.NewForestInstance(1);
        Vector3 entry = _instance.EntrySpawnWorldPos;
        _warrior = _world.NewCharacter(CharacterClass.Warrior);
        _wizard = _world.NewCharacter(CharacterClass.Wizard);
        _warrior.GodMode = true;
        _wizard.GodMode = true;
        _world.Join(_instance, _warrior, entry);
        _world.Join(_instance, _wizard, entry);

        _cleave = Shaped(200);
        _aftershockCleave = Shaped(200, (TalentModifierKind.SecondHit, 50, null));
        _rimeburst = Shaped(320, (TalentModifierKind.ImpactExplosion, 30, 60));

        CreatureTemplate boar = _world.Host.Data.CreatureTemplates.Single(t => t.Id == new CreatureTemplateId(4));
        _boar = Boar(boar, new Vector3(entry.x, entry.y, entry.z + 1.5f));
        _hitPoint = new Vector3(entry.x, entry.y, entry.z + 6f);
        _struck = Boar(boar, _hitPoint);
        var around = new Creature[5];
        for (int i = 0; i < around.Length; i++)
        {
            float radians = i * 2f * MathF.PI / around.Length;
            around[i] = Boar(boar, new Vector3(_hitPoint.x + AroundStruck * MathF.Sin(radians), _hitPoint.y,
                _hitPoint.z + AroundStruck * MathF.Cos(radians)));
        }

        // The boars enter the instance's hit query.
        _world.Tick();
        _query = (UnitHitQuery)_instance.Hits;

        _projectile = new ProjectileAbilityScript(_rimeburst, _wizard,
            new AbilityAim(new Vector3(0f, 0f, 1f), _hitPoint) { Origin = entry }, _instance);
        _projectile.Prepare();
        object hits = typeof(ProjectileAbilityScript).GetField("_hits", BindingFlags.Instance | BindingFlags.NonPublic)
                          ?.GetValue(_projectile)
                      ?? throw new InvalidOperationException("The projectile holds no hit set");
        _projectileHits = (HashSet<ObjectGuid>)(hits.GetType().GetProperty("Units")?.GetValue(hits)
                                                ?? throw new InvalidOperationException("The hit set holds no units"));
        MethodInfo explode = typeof(ProjectileAbilityScript).GetMethod("Explode", BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? throw new InvalidOperationException("The projectile has no explosion");
        _explode = explode.CreateDelegate<Action<Vector3, IUnit>>(_projectile);

        // Each row's hits counted once, then everything warmed outside measurement.
        var counting = new CountingArena(_instance);
        AbilityEffect.Apply(counting, _warrior, _aftershockCleave, _boar);
        int twice = counting.HitsPassedOn;
        Explode_among_five();
        int burst = _projectileHits.Count;
        bool fiveStruck = around.All(b => _instance.CombatService.GetEncounterFor(b) is not null);
        if (twice != 2 || burst != 6 || !fiveStruck)
        {
            throw new InvalidOperationException(
                $"Hit_twice hit {twice} times (2 expected); the explosion's cast hit {burst} units (the struck boar and " +
                "five more expected), and every boar around the struck one must have been hit.");
        }

        for (int i = 0; i < 1000; i++)
        {
            Hit_once();
            Hit_twice();
            Explode_among_five();
        }
    }

    [Benchmark(Baseline = true)]
    public void Hit_once() => AbilityEffect.Apply(_instance, _warrior, _cleave, _boar);

    [Benchmark]
    public void Hit_twice() => AbilityEffect.Apply(_instance, _warrior, _aftershockCleave, _boar);

    [Benchmark]
    public void Explode_among_five()
    {
        // ProjectileAbilityScript.Update's last-hit branch, the struck boar added to the cast's set as the sweep adds it.
        // The hit query's lists handed out go back as at the start of each tick, which no row runs: a burst's circle
        // query reuses one, as in a tick.
        _query.Recycle();
        _projectileHits.Clear();
        _projectileHits.Add(_struck.Guid);
        _instance.BroadcastProjectileImpact(_projectile.Guid, _wizard, _rimeburst, _projectile.CastId,
            _hitPoint + ProjectileAbilityScript.HeightOffset, ProjectileImpactReason.Hit, _struck);
        AbilityEffect.Apply(_instance, _wizard, _rimeburst, _struck);
        _explode(_hitPoint, _struck);
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>The template <paramref name="id" /> as a plain ability, shaped by <paramref name="rows" /> as held capstones shape it.</summary>
    private GameAbility Shaped(uint id, params (TalentModifierKind Kind, int Value, int? Value2)[] rows)
    {
        var abilityId = new AbilityId(id);
        if (!_world.Host.Data.Abilities.TryGet(abilityId, out AbilityTemplate? template))
            throw new InvalidOperationException($"The seed holds no ability {id}");

        AbilityShaping shaping = default;
        foreach ((TalentModifierKind kind, int value, int? value2) in rows)
            shaping.Add(kind, value, value2);

        return new GameAbility
        {
            AbilityId = abilityId,
            Metadata = shaping.Apply(AbilityMetadataMapper.From(template)),
            CastTimeTimer = template.CastTime / 1000f,
            CooldownTimer = 0f,
        };
    }

    /// <summary>A boar with no script at <paramref name="at" />.</summary>
    private Creature Boar(CreatureTemplate template, Vector3 at)
    {
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, IObject.GenerateId()),
            TemplateId = template.Id,
            Metadata = template,
            Name = template.Name,
            Position = at,
            BodyRadius = template.BodyRadius,
            Level = 1,
            Health = BoarHealth,
            CurrentHealth = BoarHealth,
        };
        _instance.AddCreature(boar);
        return boar;
    }

    /// <summary>The instance as an arena, counting the hits it passes on: setup's check only, never measured.</summary>
    private sealed class CountingArena(IAbilityArena inner) : IAbilityArena
    {
        public int HitsPassedOn { get; private set; }

        public MapType MapType => inner.MapType;
        public ICombatService CombatService => inner.CombatService;
        public IHitQuery Hits => inner.Hits;

        public int HitRewindTicks { get => inner.HitRewindTicks; set => inner.HitRewindTicks = value; }
        public IUnit? HitRewindExempt { get => inner.HitRewindExempt; set => inner.HitRewindExempt = value; }
        public AbilityFootprint? DodgeFootprint { get => inner.DodgeFootprint; set => inner.DodgeFootprint = value; }
        public uint CastInFlight { get => inner.CastInFlight; set => inner.CastInFlight = value; }
        public AbilityFootprint? FootprintInFlight { get => inner.FootprintInFlight; set => inner.FootprintInFlight = value; }
        public uint ViewRound => inner.ViewRound;

        public HitOutcome HitForAbility(IUnit caster, IUnit target, IAbility ability, in HitShaping shaping)
        {
            HitsPassedOn++;
            return inner.HitForAbility(caster, target, ability, shaping);
        }

        public void HealForAbility(IUnit caster, IUnit target, IAbility ability, float amountPct) =>
            inner.HealForAbility(caster, target, ability, amountPct);

        public void ApplyAbilityAura(IUnit caster, IUnit target, IAbility ability) =>
            inner.ApplyAbilityAura(caster, target, ability);

        public bool TryDeferHit(IUnit caster, IAbility ability, IUnit target) => inner.TryDeferHit(caster, ability, target);

        public IMapNavigator GetNavigatorForPosition(Vector3 position) => inner.GetNavigatorForPosition(position);

        public void BroadcastUnitStartCast(IUnit caster, IAbility ability, uint castId, AbilityFootprint? footprint) =>
            inner.BroadcastUnitStartCast(caster, ability, castId, footprint);

        public void BroadcastAbilityFired(IUnit caster, IAbility ability, AbilityFootprint footprint) =>
            inner.BroadcastAbilityFired(caster, ability, footprint);

        public void BroadcastProjectileImpact(ObjectGuid projectile, IUnit caster, IAbility ability, uint castId,
            Vector3 position, ProjectileImpactReason reason, IUnit? target) =>
            inner.BroadcastProjectileImpact(projectile, caster, ability, castId, position, reason, target);

        public void BroadcastFinishCast(IUnit caster, IAbility ability, uint castId) =>
            inner.BroadcastFinishCast(caster, ability, castId);

        public void BroadcastInterruptedCast(IUnit caster, IAbility ability, uint castId) =>
            inner.BroadcastInterruptedCast(caster, ability, castId);
    }
}
