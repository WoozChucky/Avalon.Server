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
using Avalon.World.Public.Units;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// One cone or circle cast through the real cast path (#1238), as <c>AbilityCastAllocationShould</c> pins it: a god-mode
/// Wizard at the town instance's entry casts through <see cref="MapInstance.RunInstantAbility" /> at three boars 6 m
/// north, at 0° and ±10°, then the world ticks through the two view rounds after which the pool frees the script.
/// <see cref="Fire_cone" /> is Frost Fan (212, a 60° cone, 6 m), <see cref="Fire_circle" /> Flame Burst (211, a 3 m circle
/// on the aim point); each strikes all three boars. <see cref="Hold_circle_hit" /> is a boar's telegraphed Bramble Nova
/// (315, queued, 1.2 s) on a player 2 m off, whose hit the dodge wait holds for the player's one-way delay (2 ticks at the
/// scenario's 60 ms round trip) and lands after the circle's script has gone back to the pool. With circles and cones
/// pooled per instance, each allocates nothing once warm; before, each cast built its script, its cast copy, its guid
/// and its two state boxes (docs/benchmarks.md, "Pooled circles and cones").
/// </summary>
/// <remarks>
/// The boars have no script, so nothing but the casts moves in the instance, and a hit takes no health off one (a
/// creature's script does): setup and cleanup read a landed hit off each boar's encounter instead, and the held hit off
/// the player's health. Setup fails the run unless every warm-up cast was taken and struck what it aims at.
/// </remarks>
[MemoryDiagnoser]
public class AreaCastBenchmarks
{
    private const float Distance = 6f;
    private const uint BoarHealth = 100_000_000;

    private ScenarioWorld _world = null!;
    private MapInstance _instance = null!;
    private IUnit _caster = null!;
    private CharacterEntity? _player;
    private GameAbility _ability = null!;
    private AbilityAim _aim;
    private bool _queued;
    private int _ticks;
    private Creature[] _struck = [];
    private IEncounter[] _fights = [];
    private DateTime[] _struckAt = [];
    private uint _health;

    [GlobalSetup(Target = nameof(Fire_cone))]
    public void SetupCone() => SetupWizard(new AbilityId(212));

    [GlobalSetup(Target = nameof(Fire_circle))]
    public void SetupCircle() => SetupWizard(new AbilityId(211));

    [GlobalSetup(Target = nameof(Hold_circle_hit))]
    public void SetupHeld()
    {
        _world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        _instance = _world.AddInstance(MapType.Normal);
        _player = _world.NewCharacter(1238);
        Vector3 at = TownNavmesh.EntrySpawn;
        _world.Join(_instance, _player, at);
        CreatureTemplate boar = _world.Host.Data.CreatureTemplates.Single(t => t.Id == new CreatureTemplateId(4));
        _caster = Boar(boar, at, 0f, 2f);
        _ability = Ability(new AbilityId(315));
        _aim = new AbilityAim(new Vector3(0f, 0f, -1f), null);
        _queued = true;
        _ticks = (int)MathF.Ceiling(_ability.Metadata.CastTime * 60f) + 3 * BroadcastSchedule.Slots + 2;

        uint before = _player.CurrentHealth;
        int taken = WarmUp();
        if (taken != 100 || _player.CurrentHealth >= before)
        {
            throw new InvalidOperationException(
                $"Of 100 warm-up casts {taken} were taken (all must be), and the player must have been struck.");
        }

        _health = _player.CurrentHealth;
    }

    [Benchmark]
    public void Fire_cone() => Cast();

    [Benchmark]
    public void Fire_circle() => Cast();

    [Benchmark]
    public void Hold_circle_hit() => Cast();

    [GlobalCleanup]
    public void Cleanup()
    {
        // The measured casts struck what they aim at: every boar, in the encounter the warm-up began, or the player.
        bool struck = _player is null || _player.CurrentHealth < _health;
        for (int i = 0; i < _struck.Length; i++)
        {
            struck &= ReferenceEquals(_fights[i], _instance.CombatService.GetEncounterFor(_struck[i]))
                      && _fights[i].LastDamageTime > _struckAt[i];
        }

        _world.Dispose();
        if (!struck)
            throw new InvalidOperationException("The measured casts did not strike what they aim at.");
    }

    private void SetupWizard(AbilityId id)
    {
        _world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        _instance = _world.AddInstance(MapType.Normal);
        CharacterEntity wizard = _world.NewCharacter(CharacterClass.Wizard);
        wizard.GodMode = true;
        Vector3 at = TownNavmesh.EntrySpawn;
        _world.Join(_instance, wizard, at);
        _caster = wizard;
        _ability = Ability(id);

        CreatureTemplate boar = _world.Host.Data.CreatureTemplates.Single(t => t.Id == new CreatureTemplateId(4));
        Creature centre = Boar(boar, at, 0f, Distance);
        Creature left = Boar(boar, at, -10f, Distance);
        Creature right = Boar(boar, at, 10f, Distance);
        _aim = new AbilityAim(new Vector3(0f, 0f, 1f), centre.Position);

        // The two view rounds the pool waits for, and a broadcast period more.
        _ticks = 3 * BroadcastSchedule.Slots + 2;

        int taken = WarmUp();
        _struck = [left, centre, right];
        _fights = [.. _struck.Select(b => _instance.CombatService.GetEncounterFor(b)).OfType<IEncounter>()];
        if (taken != 100 || _fights.Length != _struck.Length)
        {
            throw new InvalidOperationException(
                $"Of 100 warm-up casts {taken} were taken (all must be), and every boar must have been struck.");
        }

        _struckAt = [.. _fights.Select(f => f.LastDamageTime)];
    }

    private int WarmUp()
    {
        int taken = 0;
        for (int i = 0; i < 100; i++)
        {
            if (Cast())
                taken++;
        }

        return taken;
    }

    private bool Cast()
    {
        bool taken = _queued
            ? _instance.QueueAbility(_caster, _aim, _ability)
            : _instance.RunInstantAbility(_caster, _aim, _ability);
        for (int i = 0; i < _ticks; i++)
            _world.Tick();
        return taken;
    }

    /// <summary>A seeded ability as a character's list holds it: its row through the production mapper, ready to cast.</summary>
    private GameAbility Ability(AbilityId id)
    {
        if (!_world.Host.Data.Abilities.TryGet(id, out AbilityTemplate? template))
            throw new InvalidOperationException($"The seed holds no ability {id}");

        return new GameAbility
        {
            AbilityId = id,
            Metadata = AbilityMetadataMapper.From(template),
            CastTimeTimer = template.CastTime / 1000f,
            CooldownTimer = 0f,
        };
    }

    /// <summary>A boar with no script, <paramref name="distance" /> from <paramref name="from" /> at <paramref name="degrees" />.</summary>
    private Creature Boar(CreatureTemplate template, Vector3 from, float degrees, float distance)
    {
        float radians = degrees * MathF.PI / 180f;
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, IObject.GenerateId()),
            TemplateId = template.Id,
            Metadata = template,
            Name = template.Name,
            Position = new Vector3(from.x + distance * MathF.Sin(radians), from.y, from.z + distance * MathF.Cos(radians)),
            BodyRadius = template.BodyRadius,
            Level = 1,
            Health = BoarHealth,
            CurrentHealth = BoarHealth,
        };
        _instance.AddCreature(boar);
        return boar;
    }
}
