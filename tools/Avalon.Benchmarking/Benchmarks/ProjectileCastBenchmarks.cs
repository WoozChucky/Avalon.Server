using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// One projectile cast flown to its end on a creature, through the real cast path (#1181 Part D): a god-mode Wizard at the
/// town instance's entry casts its Spark Bolt (210) through <see cref="MapInstance.RunInstantAbility" /> at a boar 6 m
/// north, between two more at ±10° it passes, then the world ticks through the flight, the final state's broadcast and
/// two view rounds, as <c>ProjectileCastAllocationShould</c> pins it. With projectiles pooled per instance it allocates
/// nothing once warm; before the pool, each cast allocated its projectile, its cast copy, its hit set, its state boxes,
/// its guid and its running-list entry (docs/benchmarks.md, "Ability script construction").
/// </summary>
/// <remarks>
/// <para>
/// The boars have no script, so nothing but the cast moves in the instance, and a hit takes no health off one (a
/// creature's script does): setup reads a landed hit off the centre boar's encounter instead. It fails the run unless
/// every warm-up cast was taken, the centre boar was struck, and neither side boar was.
/// </para>
/// <para>
/// <see cref="Fire_fan_of_three" /> is <see cref="Fire_one" /> with the Spark Bolt shaped as a fan of three
/// (<c>AddedProjectiles</c> 2, as a held capstone shapes it), each lane at its own boar, every lane taken from the pool.
/// Its setup fails the run unless a fan cast puts three projectiles in the Wizard's view and every boar was struck.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ProjectileCastBenchmarks
{
    private const float Distance = 6f;
    private const uint BoarHealth = 100_000_000;

    private ScenarioWorld _world = null!;
    private MapInstance _instance = null!;
    private CharacterEntity _wizard = null!;
    private IAbility _sparkBolt = null!;
    private AbilityAim _aim;
    private int _ticks;
    private Creature[] _struck = [];
    private IEncounter[] _fights = [];
    private DateTime[] _struckAt = [];

    [GlobalSetup(Target = nameof(Fire_one))]
    public void SetupOne() => Setup(fan: false);

    [GlobalSetup(Target = nameof(Fire_fan_of_three))]
    public void SetupFan() => Setup(fan: true);

    private void Setup(bool fan)
    {
        _world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        _instance = _world.AddInstance(MapType.Normal);
        _wizard = _world.NewCharacter(CharacterClass.Wizard);
        _wizard.GodMode = true;
        Vector3 at = TownNavmesh.EntrySpawn;
        _world.Join(_instance, _wizard, at);
        if (fan)
            ShapeAFan();
        _sparkBolt = _wizard.Spells[new AbilityId(210)]
                     ?? throw new InvalidOperationException("The Wizard holds no Spark Bolt");

        CreatureTemplate boar = _world.Host.Data.CreatureTemplates.Single(t => t.Id == new CreatureTemplateId(4));
        Creature centre = Boar(boar, at, 0f);
        Creature left = Boar(boar, at, -10f);
        Creature right = Boar(boar, at, 10f);
        _aim = new AbilityAim(new Vector3(0f, 0f, 1f), centre.Position);

        // The cast time, the flight, the final state's broadcast and the two view rounds the pool waits for.
        AbilityMetadata meta = _sparkBolt.Metadata;
        _ticks = (int)MathF.Ceiling(meta.CastTime * 60f) + (int)MathF.Ceiling(Distance / meta.ProjectileSpeed * 60f)
                 + 3 * BroadcastSchedule.Slots + 2;

        int taken = 0;
        for (int i = 0; i < 100; i++)
        {
            if (Cast())
                taken++;
        }

        // One projectile strikes the centre boar alone; a fan strikes all three, a lane each.
        _struck = fan ? [left, centre, right] : [centre];
        _fights = [.. _struck.Select(b => _instance.CombatService.GetEncounterFor(b)).OfType<IEncounter>()];
        bool sidesSpared = fan || (_instance.CombatService.GetEncounterFor(left) is null
                                   && _instance.CombatService.GetEncounterFor(right) is null);
        if (taken != 100 || _fights.Length != _struck.Length || !sidesSpared)
        {
            throw new InvalidOperationException(
                $"Of 100 warm-up casts {taken} were taken (all must be), and {(fan ? "every boar" : "only the centre boar")} must have been struck.");
        }

        int lanes = fan ? LanesInView() : 1;
        if (lanes != (fan ? 3 : 1))
            throw new InvalidOperationException($"A fan cast put {lanes} projectiles in the Wizard's view, not 3.");

        _struckAt = [.. _fights.Select(f => f.LastDamageTime)];
    }

    [Benchmark]
    public void Fire_one() => Cast();

    [Benchmark]
    public void Fire_fan_of_three() => Cast();

    [GlobalCleanup]
    public void Cleanup()
    {
        // The measured casts struck every boar they aim at, each in the encounter the warm-up began.
        bool struck = true;
        for (int i = 0; i < _struck.Length; i++)
        {
            struck &= ReferenceEquals(_fights[i], _instance.CombatService.GetEncounterFor(_struck[i]))
                      && _fights[i].LastDamageTime > _struckAt[i];
        }

        _world.Dispose();
        if (!struck)
            throw new InvalidOperationException("The measured casts did not strike every boar they aim at.");
    }

    /// <summary>
    /// The Wizard's Spark Bolt replaced in its list by a plain ability shaped as a fan of three (<c>AddedProjectiles</c> 2),
    /// as a held capstone shapes it (<see cref="AbilityShaping.Apply" />).
    /// </summary>
    private void ShapeAFan()
    {
        var id = new AbilityId(210);
        if (!_world.Host.Data.Abilities.TryGet(id, out AbilityTemplate? template))
            throw new InvalidOperationException("The seed holds no Spark Bolt");

        AbilityShaping shaping = default;
        shaping.Add(TalentModifierKind.AddedProjectiles, 2, null);
        var shaped = new GameAbility
        {
            AbilityId = id,
            Metadata = shaping.Apply(AbilityMetadataMapper.From(template)),
            CastTimeTimer = template.CastTime / 1000f,
            CooldownTimer = 0f,
        };
        _wizard.Spells.Load([.. _wizard.Spells.All.Select(a => a.AbilityId == id ? shaped : a)]);
    }

    /// <summary>
    /// The projectiles one more cast puts in the Wizard's view: every new spell projectile its view decided to add, tick
    /// by tick through the cast's rounds.
    /// </summary>
    private int LanesInView()
    {
        HashSet<ulong> seen = [];
        if (!_instance.RunInstantAbility(_wizard, _aim, _sparkBolt))
            return 0;

        for (int i = 0; i < _ticks; i++)
        {
            _world.Tick();
            foreach (ObjectGuid guid in _wizard.CharacterGameState.NewObjects)
            {
                if (guid.Type == ObjectType.SpellProjectile)
                    seen.Add(guid.RawValue);
            }
        }

        return seen.Count;
    }

    private bool Cast()
    {
        bool taken = _instance.RunInstantAbility(_wizard, _aim, _sparkBolt);
        for (int i = 0; i < _ticks; i++)
            _world.Tick();
        return taken;
    }

    /// <summary>A boar with no script, <see cref="Distance" /> from <paramref name="from" /> at <paramref name="degrees" />.</summary>
    private Creature Boar(CreatureTemplate template, Vector3 from, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, IObject.GenerateId()),
            TemplateId = template.Id,
            Metadata = template,
            Name = template.Name,
            Position = new Vector3(from.x + Distance * MathF.Sin(radians), from.y, from.z + Distance * MathF.Cos(radians)),
            BodyRadius = template.BodyRadius,
            Level = 1,
            Health = BoarHealth,
            CurrentHealth = BoarHealth,
        };
        _instance.AddCreature(boar);
        return boar;
    }
}
