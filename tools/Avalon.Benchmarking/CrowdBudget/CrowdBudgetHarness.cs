using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Creatures;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Benchmarking.CrowdBudget;

/// <summary>
/// Spike for #425: the per-tick cost of creature locomotion (Waypoint, Crowd, Crowd with players) on a
/// real MapInstance over the real baked town navmesh (Maps/TownLayouts/1.json), with N hostile
/// creatures running the real CreatureCombatScript, all engaged with P players who walk a loop, so the
/// melee station-keeping never settles. Not a BenchmarkDotNet benchmark: it is a steady-state scenario,
/// timed tick by tick.
///
/// Run: dotnet run -c Release --project tools/Avalon.Benchmarking -- crowd-budget [players] [warmupTicks] [measureTicks] [n,n,...] [playerSpeed]
/// </summary>
public static class CrowdBudgetHarness
{
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d / 60d);
    private const double TickBudgetMs = 1000d / 60d;

    private enum Mode { Waypoint, Crowd, CrowdPlayers }

    private static float s_playerSpeed = CharacterMovement.BaseSpeed;

    public static void Run(string[] args)
    {
        int players = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 4;
        int warmup = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 900;
        int measure = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 3600;
        int[] counts = args.Length > 3
            ? args[3].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray()
            : [10, 25, 50, 100];
        s_playerSpeed = args.Length > 4 ? float.Parse(args[4], CultureInfo.InvariantCulture) : CharacterMovement.BaseSpeed;

        Console.WriteLine($"Release={!IsDebug()} Cores={Environment.ProcessorCount} OS={Environment.OSVersion} .NET={Environment.Version}");
        Console.WriteLine($"players={players} playerSpeed={s_playerSpeed} m/s creatureRun=5 m/s warmup={warmup} measure={measure} ticks at 1/60 s");

        using Process self = Process.GetCurrentProcess();
        try { self.PriorityClass = ProcessPriorityClass.High; } catch { /* best effort */ }

        // One logical CPU, so a hybrid CPU cannot move the tick between performance and efficiency cores
        // mid-run (CROWD_AFFINITY is a hex mask; the default, 0x4, is logical CPU 2).
        string mask = Environment.GetEnvironmentVariable("CROWD_AFFINITY") ?? "4";
        try { self.ProcessorAffinity = (IntPtr)long.Parse(mask, NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
        catch { /* best effort */ }
        Console.WriteLine($"affinity=0x{mask}");

        DtNavMesh navMesh = BakeTown();
        Console.WriteLine($"Town navmesh baked: {navMesh.GetMaxTiles()} max tiles");
        if (Environment.GetEnvironmentVariable("CROWD_MAP") == "1")
        {
            var nav = new MapNavigator(NullLoggerFactory.Instance);
            nav.LoadFromNavMesh(navMesh);
            for (int z = 59; z >= 0; z--)
            {
                var line = new System.Text.StringBuilder();
                for (int x = 0; x < 60; x++)
                {
                    var c = new Vector3(x + 0.5f, 0, z + 0.5f);
                    List<Vector3> path = nav.FindPath(new Vector3(15, 0, 15), c);
                    line.Append(path.Count > 0 && Vector3.Distance(path[^1] with { y = 0 }, c) < 0.3f ? '.' : '#');
                }
                Console.WriteLine(line);
            }
            return;
        }

        // Warm the JIT on every path once, small, so the first measured mode is not paying for it.
        foreach (Mode mode in Enum.GetValues<Mode>())
            RunScenario(navMesh, mode, 10, players, 120, 60, print: false);

        Console.WriteLine();
        Console.WriteLine("mode         N  | loco mean  p95   max  (ms) | update mean  p95  (ms) | loco %tick | MoveTo/cr/tick  /s  | FindPath/cr/tick | flips/cr/s | loco B/tick  upd B/tick | inRange% returning");
        foreach (int n in counts)
        {
            foreach (Mode mode in Enum.GetValues<Mode>())
                RunScenario(navMesh, mode, n, players, warmup, measure, print: true);
        }
    }

    private static bool IsDebug()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static void RunScenario(DtNavMesh navMesh, Mode mode, int n, int playerCount, int warmup, int measure, bool print)
    {
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(navMesh);
        var counting = new CountingNavigator(navigator);

        var config = new GameConfiguration
        {
            CreatureLocomotion = mode == Mode.Waypoint ? CreatureLocomotionMode.Waypoint : CreatureLocomotionMode.Crowd,
            CrowdIncludesPlayers = mode == Mode.CrowdPlayers,
        };

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(config);
        world.MapTemplates.Returns(new List<MapTemplate>());
        world.Data.Returns((StaticData)null!);

        var services = new ServiceCollection();
        services.AddSingleton(new CombatConfig());
        services.AddSingleton(Substitute.For<IScriptManager>());
        ServiceProvider sp = services.BuildServiceProvider();

        // Harness-side instrumentation: through MapInstance's locomotion hook (#638), wrap the very
        // implementation production would build in a timing and counting decorator. Waypoint is rebuilt
        // over a counting navigator (production passes GetNavigatorForPosition, which is this
        // navigator). The decorator does the crowd's player sync itself, because MapInstance's
        // `is CrowdLocomotion` test no longer matches: same place in the tick (after the scripts,
        // before the locomotion), same calls.
        ICreatureLocomotion inner = null!;
        MeasuredLocomotion measured = null!;
        ChunkLayout layout = TownLayout();
        var instance = new MapInstance(NullLoggerFactory.Instance, sp, world, new MapTemplateId(1), null, layout,
            navigator, seed: 0, mapType: MapType.Town, locomotion: configured =>
            {
                inner = configured;
                if (mode == Mode.Waypoint)
                    inner = new WaypointLocomotion(_ => counting);
                else if (inner is not CrowdLocomotion)
                    throw new InvalidOperationException("Crowd was configured but the instance fell back to waypoint");

                measured = new MeasuredLocomotion(inner, mode == Mode.CrowdPlayers);
                return measured;
            });
        measured.Instance = instance;

        var rng = new Random(425);
        Vector3 centre = new(15f, 0f, 15f); // the entry room (SW chunk) of the town, 28 m square
        centre = Snap(navigator, centre) ?? throw new InvalidOperationException("Loop centre is off the navmesh");
        const float loopRadius = 8f;
        float playerSpeed = s_playerSpeed;
        float angularSpeed = playerSpeed / loopRadius;

        var chars = new List<CharacterEntity>();
        for (int p = 0; p < playerCount; p++)
        {
            CharacterEntity ch = NewCharacter((uint)(1 + p));
            ch.Spells.Load(Array.Empty<IAbility>());
            ch.InstanceId = instance.InstanceId;
            ch.Position = LoopPoint(navigator, centre, loopRadius, Phase(p, playerCount, 0));
            instance.AddCharacter(new BenchConnection(ch));
            chars.Add(ch);
        }

        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(4),
            Name = "Bench Wolf",
            SpeedWalk = 2.5f,
            SpeedRun = 5f,
            BaseAttackTime = 2.25f,
        };

        var creatures = new List<Creature>();
        var scripts = new List<CreatureCombatScript>();
        for (int i = 0; i < n; i++)
        {
            Vector3 spawn = RandomSpawn(navigator, centre, rng);
            var creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, (uint)(10_000 + i)),
                TemplateId = template.Id,
                Metadata = template,
                Name = template.Name,
                Position = spawn,
                Speed = template.SpeedWalk,
                MoveState = MoveState.Idle,
                Level = 1,
                Health = 100_000_000,
                CurrentHealth = 100_000_000,
                DamageMin = 1,
                DamageMax = 1,
                BaseAttackTime = template.BaseAttackTime,
            };
            var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, instance);
            creature.Script = script;
            instance.AddCreature(creature);
            script.OnEnteredRange(chars[i % playerCount]);
            creatures.Add(creature);
            scripts.Add(script);
        }

        var prevState = new MoveState[n];
        long flips = 0, findPaths = 0, moveTos = 0, inRangeSamples = 0;
        double distSum = 0;
        int offLoop = 0;
        for (int k = 0; k < 64; k++)
        {
            Vector3 lp = LoopPoint(navigator, centre, loopRadius, k * MathF.Tau / 64);
            if (Snap(navigator, lp) is not { } s0 || Vector3.Distance(s0, lp) > 0.3f) offLoop++;
        }
        var locoMs = new double[measure];
        var updMs = new double[measure];
        long locoAlloc = 0, updAlloc = 0;
        var telemetry = new Dictionary<string, long>();
        DtCrowd? crowd = inner is CrowdLocomotion cl
            ? (DtCrowd)typeof(CrowdLocomotion).GetField("_crowd", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cl)!
            : null;

        int total = warmup + measure;
        var sw = new Stopwatch();
        for (int tick = 0; tick < total; tick++)
        {
            bool measuring = tick >= warmup;
            if (tick == warmup)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                counting.FindPathCalls = 0;
                measured.MoveToCalls = 0;
                for (int i = 0; i < n; i++) prevState[i] = creatures[i].MoveState;
            }

            // The players' input for this tick, as PlayerInputHandler would have applied it.
            float t = (float)(tick * Dt.TotalSeconds);
            for (int p = 0; p < chars.Count; p++)
            {
                CharacterEntity ch = chars[p];
                Vector3 before = ch.Position;
                Vector3 next = LoopPoint(navigator, centre, loopRadius, Phase(p, playerCount, t * angularSpeed));
                ch.Position = next;
                ch.Velocity = (next - before) * 60f;
                ch.CurrentHealth = ch.Health;
            }

            world.ClearReceivedCalls();

            long a0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            instance.Update(Dt);
            sw.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread();

            if (!measuring)
                continue;

            int m = tick - warmup;
            updMs[m] = sw.Elapsed.TotalMilliseconds;
            locoMs[m] = measured.LastUpdateMs;
            updAlloc += a1 - a0;
            locoAlloc += measured.LastAlloc;

            for (int i = 0; i < n; i++)
            {
                MoveState s = creatures[i].MoveState;
                if (s != prevState[i]) flips++;
                prevState[i] = s;
                float d = Vector3.Distance(creatures[i].Position, chars[i % playerCount].Position);
                distSum += d;
                if (d <= 1.5f + 0.5f + 0.05f)
                    inRangeSamples++;
            }

            if (crowd is not null)
            {
                foreach (var e in crowd.Telemetry().ToExecutionTimings())
                    telemetry[e.Key] = telemetry.GetValueOrDefault(e.Key) + e.Ticks;
            }
        }

        findPaths = counting.FindPathCalls;
        moveTos = measured.MoveToCalls;
        int returning = scripts.Count(s => s.State is CreatureCombatScript.CombatState.Returning);
        int idle = scripts.Count(s => s.State is CreatureCombatScript.CombatState.None);

        if (!print)
            return;

        double seconds = measure * Dt.TotalSeconds;
        Array.Sort(locoMs);
        Array.Sort(updMs);
        double locoMean = locoMs.Average();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0,-12} {1,3} | {2,8:F3} {3,6:F3} {4,6:F3}      | {5,8:F3} {6,6:F3}      | {7,8:F1}%  | {8,8:F3} {9,7:F0}   | {10,8:F3}         | {11,8:F2}   | {12,9:F0} {13,10:F0} | {14,6:F1}%  {15}/{16}",
            mode, n, locoMean, P(locoMs, 0.95), locoMs[^1], updMs.Average(), P(updMs, 0.95),
            locoMean / TickBudgetMs * 100d,
            moveTos / (double)(n * measure), moveTos / seconds,
            findPaths / (double)(n * measure),
            flips / (double)n / seconds,
            locoAlloc / (double)measure, updAlloc / (double)measure,
            inRangeSamples * 100d / (n * (double)measure), returning, idle));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "                   meanDist={0:F2} offLoopPoints={1}/64 centre={2}",
            distSum / (n * (double)measure), offLoop, centre));

        if (crowd is not null && n >= 50)
        {
            // DtCrowd's own telemetry: each value is a 10-sample running average, summed over the
            // measured ticks, so dividing by the tick count gives an approximate per-tick mean.
            string breakdown = string.Join(", ", telemetry.OrderByDescending(kv => kv.Value)
                .Take(6)
                .Select(kv => string.Format(CultureInfo.InvariantCulture, "{0}={1:F3}ms", kv.Key,
                    kv.Value / (double)measure / TimeSpan.TicksPerMillisecond)));
            Console.WriteLine($"                   crowd stages/tick: {breakdown}");
        }
    }

    private static double P(double[] sorted, double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1)];

    private static float Phase(int p, int count, float angle) => angle + p * (MathF.Tau / count);

    private static Vector3 LoopPoint(IMapNavigator nav, Vector3 centre, float radius, float angle)
    {
        var raw = new Vector3(centre.x + radius * MathF.Cos(angle), centre.y, centre.z + radius * MathF.Sin(angle));
        return raw with { y = nav.SampleGroundHeight(raw.x, centre.y, raw.z) };
    }

    private static Vector3 RandomSpawn(IMapNavigator nav, Vector3 centre, Random rng)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            float angle = (float)(rng.NextDouble() * Math.Tau);
            float dist = 9f + (float)rng.NextDouble() * 4f;
            var candidate = new Vector3(centre.x + dist * MathF.Cos(angle), centre.y, centre.z + dist * MathF.Sin(angle));
            if (Snap(nav, candidate) is { } snapped && Vector3.Distance(snapped, candidate with { y = snapped.y }) < 0.3f)
                return snapped;
        }

        throw new InvalidOperationException("Could not find a reachable spawn point");
    }

    /// <summary>The candidate snapped onto the navmesh, if a path from the loop centre reaches it.</summary>
    private static Vector3? Snap(IMapNavigator nav, Vector3 candidate)
    {
        var from = new Vector3(15f, 0f, 15f);
        List<Vector3> path = nav.FindPath(from, candidate);
        return path.Count == 0 ? null : path[^1];
    }

    private static CharacterEntity NewCharacter(uint id)
    {
        var row = new Character
        {
            Id = new CharacterId(id), AccountId = new AccountId(1), Name = $"Bench{id}",
            Class = CharacterClass.Warrior, CreationDate = DateTime.UtcNow, Health = 100_000_000,
        };
        var ch = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };
        ch.CurrentHealth = ch.Health;
        return ch;
    }

    private static ChunkLayout TownLayout()
    {
        // Maps/TownLayouts/1.json, as PredefinedChunkLayoutSource builds it: rotation 0, cell 30.
        PlacedChunk sw = new(new ChunkTemplateId(1), 0, 0, 0, new Vector3(0, 0, 0));
        PlacedChunk se = new(new ChunkTemplateId(2), 1, 0, 0, new Vector3(30, 0, 0));
        PlacedChunk nw = new(new ChunkTemplateId(3), 0, 1, 0, new Vector3(0, 0, 30));
        PlacedChunk ne = new(new ChunkTemplateId(4), 1, 1, 0, new Vector3(30, 0, 30));
        return new ChunkLayout(0, [sw, se, nw, ne], sw, null, [], new Vector3(15, 0, 15), 30f, null);
    }

    private static DtNavMesh BakeTown()
    {
        // ChunkLayoutNavmeshBuilder reads Maps/Chunks/<name>.obj under the working directory.
        string dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "src", "Server", "Avalon.Server.World", "Maps", "Chunks")))
            dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException("repo root");
        Directory.SetCurrentDirectory(Path.Combine(dir, "src", "Server", "Avalon.Server.World"));

        var library = new TownLibrary();
        var builder = new ChunkLayoutNavmeshBuilder(NullLoggerFactory.Instance, library);
        return builder.BuildAsync(TownLayout(), CancellationToken.None).GetAwaiter().GetResult();
    }


    private sealed class TownLibrary : IChunkLibrary
    {
        private static readonly Dictionary<int, string> Names = new()
        {
            [1] = "town_sw_01", [2] = "town_se_01", [3] = "town_nw_01", [4] = "town_ne_01",
        };

        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

        public ChunkTemplate GetById(ChunkTemplateId id) =>
            new() { Id = id, Name = Names[id.Value], CellSize = 30f };

        public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) => [];

        public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
            ids.ToDictionary(id => id, GetById);
    }

    /// <summary>
    /// Counts FindPath calls on the way to a real navigator. It forwards the buffered overload too
    /// (#638), since that is the one WaypointLocomotion calls on a MapNavigator.
    /// </summary>
    private sealed class CountingNavigator(MapNavigator inner) : IMapNavigator, IPathBufferNavigator
    {
        public long FindPathCalls;

        public List<Vector3> FindPath(Vector3 start, Vector3 end)
        {
            FindPathCalls++;
            return inner.FindPath(start, end);
        }

        public void FindPath(Vector3 start, Vector3 end, List<Vector3> path)
        {
            FindPathCalls++;
            inner.FindPath(start, end, path);
        }

        public bool HasVisibility(Vector3 start, Vector3 end) => inner.HasVisibility(start, end);
        public Vector3 RaycastWalkable(Vector3 from, Vector3 to) => inner.RaycastWalkable(from, to);
        public float SampleGroundHeight(float x, float y, float z) => inner.SampleGroundHeight(x, y, z);
        public object? Mesh => inner.Mesh;
    }

    private sealed class MeasuredLocomotion(ICreatureLocomotion inner, bool syncPlayers) : ICreatureLocomotion
    {
        private readonly Stopwatch _sw = new();

        /// <summary>The instance this decorates, set once the constructor that built it returns.</summary>
        public MapInstance Instance { get; set; } = null!;
        public long MoveToCalls;
        public double LastUpdateMs;
        public long LastAlloc;

        public void Register(ICreature creature, float radius) => inner.Register(creature, radius);
        public void Unregister(ICreature creature) => inner.Unregister(creature);

        public void MoveTo(ICreature creature, Vector3 destination)
        {
            MoveToCalls++;
            inner.MoveTo(creature, destination);
        }

        public void Stop(ICreature creature) => inner.Stop(creature);
        public void Teleport(ICreature creature, Vector3 position) => inner.Teleport(creature, position);
        public bool HasArrived(ICreature creature) => inner.HasArrived(creature);
        public float ArrivalTolerance(ICreature creature) => inner.ArrivalTolerance(creature);
        public Vector3? ResolvedDestination(ICreature creature) => inner.ResolvedDestination(creature);
        public void SyncPlayer(ObjectGuid guid, Vector3 position) => inner.SyncPlayer(guid, position);
        public void RemovePlayer(ObjectGuid guid) => inner.RemovePlayer(guid);

        public void Update(TimeSpan deltaTime)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            _sw.Restart();
            if (syncPlayers && inner is CrowdLocomotion crowd)
            {
                foreach ((ObjectGuid guid, ICharacter character) in Instance.Characters)
                    crowd.SyncPlayer(guid, character.Position);
            }

            inner.Update(deltaTime);
            _sw.Stop();
            LastUpdateMs = _sw.Elapsed.TotalMilliseconds;
            LastAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        }
    }
}
