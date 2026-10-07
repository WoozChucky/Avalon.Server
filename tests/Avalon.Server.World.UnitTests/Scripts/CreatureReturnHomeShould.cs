using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Combat;
using Avalon.World.Creatures;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Scripts.Creatures.Forest;
using DotRecast.Detour;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// #715: a creature kited past its leash walks home, ignoring hits, and must always end that walk: reset at home,
/// at full health and hittable again. Driven over a real navmesh, a real locomotion (both kinds), a real
/// World-side creature and a real <see cref="CombatService" />, so "immune" is what the combat service does with
/// a hit, not what a substitute was told to answer. Time is the tick's delta only.
/// </summary>
public class CreatureReturnHomeShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(0.05);

    /// <summary>A flat 120 x 120 ground at height 0, from -60 to 60 on X and Z.</summary>
    private static readonly Lazy<DtNavMesh> s_ground = new(BakeGround, isThreadSafe: true);

    private static DtNavMesh BakeGround()
    {
        float[] vertices =
        [
            -60f, 0f, -60f,
            -60f, 0f, 60f,
            60f, 0f, 60f,
            60f, 0f, -60f,
        ];
        int[] faces = [0, 1, 2, 0, 2, 3];

        NavMeshBuildResult result = new TileNavMeshBuilder().Build(new RcSampleInputGeomProvider(vertices, faces),
            NavmeshBuildSettings.Create());
        Assert.NotNull(result?.NavMesh);
        return result!.NavMesh;
    }

    public enum Mover
    {
        Waypoint,
        Crowd,
    }

    public enum Brain
    {
        Combat,
        ThornbackBoar,
    }

    public static TheoryData<Mover, Brain, float, float, bool> Homes() => new()
    {
        // A home on the navmesh, exactly where the fight began.
        { Mover.Waypoint, Brain.Combat, -10f, 0f, false },
        { Mover.Crowd, Brain.Combat, -10f, 0f, false },
        // A home half a metre above the mesh: a procedural spawn keeps its slot's height, unsnapped.
        { Mover.Waypoint, Brain.Combat, -10f, 0.5f, false },
        { Mover.Crowd, Brain.Combat, -10f, 0.5f, false },
        { Mover.Waypoint, Brain.ThornbackBoar, -10f, 0.5f, false },
        { Mover.Crowd, Brain.ThornbackBoar, -10f, 0.5f, false },
        // A home off the mesh sideways: the mesh is eroded by the agent radius (0.6 m) at its edge, so the
        // nearest point any route can end at is about 0.5 m short of it.
        { Mover.Waypoint, Brain.Combat, -59.9f, 0f, true },
        { Mover.Crowd, Brain.Combat, -59.9f, 0f, true },
        { Mover.Waypoint, Brain.ThornbackBoar, -59.9f, 0f, true },
    };

    /// <summary>
    /// The report: kite a creature past its 40 m leash, let it walk home, and it must end the walk reset
    /// (full health, no longer returning) within the time a walk home takes, and a hit must land again. A home
    /// within the locomotion's arrival tolerance of the mesh is reached by walking, with no fallback logged; a
    /// home farther off the mesh than that is reached by the snap at the end of the route, logged once.
    /// </summary>
    [Theory]
    [MemberData(nameof(Homes))]
    public void Reset_And_Take_Hits_Again_After_Walking_Home_From_A_Kited_Fight(Mover mover, Brain script,
        float homeX, float homeY, bool snapsHome)
    {
        var logger = new CollectingLoggerFactory();
        var fight = new Fight(mover, script, new Vector3(homeX, homeY, 0f), logger);

        fight.KitePastTheLeash();
        Assert.Equal(fight.Creature.Health, fight.Creature.CurrentHealth);

        // 40 m at 4 m/s is 10 s; 20 s of ticks is plenty for a creature that can get home at all.
        int ticks = fight.TicksUntilReset(limit: 400);

        Assert.True(ticks > 0,
            $"never reset: at {fight.Creature.Position}, home {fight.Home}, arrived " +
            $"{fight.Locomotion.HasArrived(fight.Creature)}, route end {fight.Locomotion.ResolvedDestination(fight.Creature)}");
        Assert.True(Vector3.Distance(Flat(fight.Creature.Position), Flat(fight.Home)) < 1f,
            $"reset at {fight.Creature.Position}, not home {fight.Home}");
        // A crowd re-adds a teleported agent on the nearest mesh point, so the position is checked on X/Z above.
        Assert.Equal(snapsHome ? 1 : 0, logger.Warnings.Count(w => w.Contains("could not walk home")));

        fight.Hit(10);
        Assert.Equal(fight.Creature.Health - 10, fight.Creature.CurrentHealth);
    }

    /// <summary>
    /// The safety net (#715): a creature that cannot get home at all (here its locomotion is jammed: it plans a
    /// route and never moves along it) is not left returning, and so immune, for good. Within the bound it is
    /// put home, reset exactly as when it walks there, and the fallback is logged once at Warning.
    /// </summary>
    [Fact]
    public void Snap_Home_And_Reset_When_It_Cannot_Get_Home_Within_The_Bound()
    {
        var logger = new CollectingLoggerFactory();
        var fight = new Fight(Mover.Waypoint, Brain.Combat, new Vector3(-10f, 0f, 0f), logger);

        fight.KitePastTheLeash();
        fight.Jammed = true;

        int ticks = fight.TicksUntilReset(limit: 20 * 20);

        Assert.InRange(ticks, 1, 16 * 20); // the stall limit, 5 s, well inside the 15 s cap
        Assert.Equal(fight.Home, fight.Creature.Position);
        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("came no closer to home", warning);

        fight.Hit(10);
        Assert.Equal(fight.Creature.Health - 10, fight.Creature.CurrentHealth);
    }

    /// <summary>
    /// Leaving the encounter at the reset (#614) still holds for a creature the safety net put home: no threat
    /// from before the leash is carried into the next fight.
    /// </summary>
    [Fact]
    public void Leave_Its_Encounter_When_The_Safety_Net_Puts_It_Home()
    {
        var fight = new Fight(Mover.Waypoint, Brain.Combat, new Vector3(-10f, 0f, 0f));
        fight.Hit(5); // an encounter with the kiter's threat on the creature
        Assert.NotNull(fight.Registry.FindEncounterContaining(fight.Creature));

        fight.KitePastTheLeash();
        fight.Jammed = true;
        Assert.True(fight.TicksUntilReset(limit: 400) > 0);

        IEncounter? encounter = fight.Registry.FindEncounterContaining(fight.Creature);
        Assert.Null(encounter);
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    /// <summary>
    /// One creature, home at <c>home</c>, over the ground above, fighting a character who runs off east at 6 m/s,
    /// faster than the creature's 4, until the creature gives up past its leash.
    /// </summary>
    private sealed class Fight
    {
        private readonly ICreatureLocomotion _real;
        private Vector3 _targetPosition;

        public Fight(Mover mover, Brain script, Vector3 home, ILoggerFactory? loggerFactory = null)
        {
            loggerFactory ??= NullLoggerFactory.Instance;
            Home = home;

            if (mover == Mover.Waypoint)
            {
                var navigator = new MapNavigator(NullLoggerFactory.Instance);
                navigator.LoadFromNavMesh(s_ground.Value);
                _real = new WaypointLocomotion(_ => navigator);
            }
            else
            {
                _real = new CrowdLocomotion(s_ground.Value, NavmeshBuildSettings.AgentRadius, NullLogger.Instance);
            }

            Locomotion = new JammableLocomotion(_real, this);

            ICreatureMetadata metadata = Substitute.For<ICreatureMetadata>();
            metadata.SpeedRun.Returns(4f);
            metadata.DetectionRange.Returns(10f);
            Creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, 715),
                Metadata = metadata,
                Name = "Thornback Boar",
                Health = 100,
                CurrentHealth = 100,
                Position = home,
                Speed = 4f,
            };

            _targetPosition = home + new Vector3(3f, 0f, 0f);
            Target = Substitute.For<ICharacter>();
            Target.Guid.Returns(new ObjectGuid(ObjectType.Character, 1));
            Target.Position.Returns(_ => _targetPosition);
            Target.IsDead.Returns(false);

            Context = Substitute.For<ISimulationContext>();
            Context.Locomotion.Returns(Locomotion);
            Context.MeleeSlots.Returns(new MeleeSlots(6, radius: 1.5f));
            Context.Characters.Returns(new Dictionary<ObjectGuid, ICharacter>());
            var config = new CombatConfig();
            Registry = new EncounterRegistry(config);
            Combat = new CombatService(config, Registry, Context);
            Context.CombatService.Returns(Combat);

            _real.Register(Creature, NavmeshBuildSettings.AgentRadius);
            AiScript ai = script == Brain.Combat
                ? new CreatureCombatScript(loggerFactory, Creature, Context)
                : new ThornbackBoarScript(loggerFactory, Creature, Context);
            Creature.Script = ai;
            Script = ai;

            ai.OnEnteredRange(Target);
        }

        public Vector3 Home { get; }
        public Creature Creature { get; }
        public ICharacter Target { get; }
        public ISimulationContext Context { get; }
        public EncounterRegistry Registry { get; }
        public CombatService Combat { get; }
        public ICreatureLocomotion Locomotion { get; }
        public AiScript Script { get; }

        /// <summary>When set, the locomotion accepts every destination and never moves the creature.</summary>
        public bool Jammed { get; set; }

        public bool Returning => Creature.Script is AiScript s && IsReturning(s);

        public void Hit(uint damage) => Combat.ApplyDamage(Target, Creature, damage);

        public void KitePastTheLeash()
        {
            for (int i = 0; i < 600; i++)
            {
                _targetPosition += new Vector3(6f * (float)s_tick.TotalSeconds, 0f, 0f);
                Step();
                if (Returning)
                    return;
            }

            Assert.Fail($"never gave up the kited fight: creature at {Creature.Position}, target at {_targetPosition}");
        }

        /// <summary>Ticks until the creature is no longer returning; the tick count, or -1.</summary>
        public int TicksUntilReset(int limit)
        {
            for (int i = 1; i <= limit; i++)
            {
                Step();
                if (!Returning)
                    return i;
            }

            return -1;
        }

        private void Step()
        {
            Script.Update(s_tick);   // the AI first...
            Locomotion.Update(s_tick); // ...then the locomotion, as MapInstance does
        }

        /// <summary>
        /// Whether the combat script is walking home: its own state, or, for a forest script, the state of the
        /// combat script it chains (read by reflection, since the chain is protected).
        /// </summary>
        private static bool IsReturning(AiScript script) => script switch
        {
            CreatureCombatScript combat => combat.State is CreatureCombatScript.CombatState.Returning,
            _ => ChainedCombat(script)?.State is CreatureCombatScript.CombatState.Returning,
        };

        private static CreatureCombatScript? ChainedCombat(AiScript script)
        {
            var chained = (List<AiScript>)typeof(AiScript)
                .GetProperty("ChainedScripts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(script)!;
            return chained.OfType<CreatureCombatScript>().SingleOrDefault();
        }
    }

    /// <summary>A real locomotion that, while jammed, plans but never moves the creature.</summary>
    private sealed class JammableLocomotion(ICreatureLocomotion real, Fight fight) : ICreatureLocomotion
    {
        public void Register(ICreature creature, float radius) => real.Register(creature, radius);
        public void Unregister(ICreature creature) => real.Unregister(creature);
        public void MoveTo(ICreature creature, Vector3 destination) => real.MoveTo(creature, destination);
        public void Stop(ICreature creature) => real.Stop(creature);
        public void Teleport(ICreature creature, Vector3 position) => real.Teleport(creature, position);
        public bool HasArrived(ICreature creature) => !fight.Jammed && real.HasArrived(creature);
        public float ArrivalTolerance(ICreature creature) => real.ArrivalTolerance(creature);
        public Vector3? ResolvedDestination(ICreature creature) => fight.Jammed ? null : real.ResolvedDestination(creature);
        public void SyncPlayer(ObjectGuid guid, Vector3 position) => real.SyncPlayer(guid, position);
        public void RemovePlayer(ObjectGuid guid) => real.RemovePlayer(guid);

        public void Update(TimeSpan deltaTime)
        {
            if (!fight.Jammed)
                real.Update(deltaTime);
        }
    }

    private sealed class CollectingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
