# Tick phase traces

The opt-in `Avalon-World-Tick` EventSource breaks down steady-state simulation and replication work without adding per-player metric series. It is disabled without a listener. Collect a bounded trace on a deployed build that contains the provider:

```sh
dotnet-trace collect --process-id <world-pid> --providers Avalon-World-Tick:0:4 --duration 00:00:30 --output tick-phases.nettrace
```

Use the world's diagnostic socket/PID from the diagnostic container in Kubernetes. Keep captures outside the repository. Capture only after the population has settled; record image, population, instance distribution, scenario, start/end times and concurrent collectors. Compare with an unprofiled run: enabled timestamp reads and event emission have a measurable cost. Do not use a traced run as a capacity verdict.

## Capturing on a deployed world

A world container has no diagnostic tools in it. A trace is taken from a debug container attached to the pod, which shares the world's process namespace. These are the steps used on the homelab's load-test world (`avalon-world-loadtest-0`, namespace `avalon`); the same works for any world pod. From Git Bash, set `MSYS_NO_PATHCONV=1` first, or the `/root/...` paths below are rewritten.

Before starting: check that the worlds sharing the node are quiet (see [load testing](load-testing.md#cautions)), and capture under a held load, not while it ramps.

1. **Attach a debug container.** It stays until the pod restarts, so give each one a new name.

   ```sh
   kubectl -n avalon debug avalon-world-loadtest-0 --image=mcr.microsoft.com/dotnet/sdk:10.0 \
     --target=avalon-world --container=trace1 --profile=general -- sleep 3600
   ```

2. **Install the tool and find the process.** The world is process 1 of the shared namespace. Its diagnostic socket is in its own `/tmp`, which the debug container reaches through `/proc/1/root`: without `TMPDIR` set to that, `dotnet-trace` finds no process.

   ```sh
   kubectl -n avalon exec avalon-world-loadtest-0 -c trace1 -- sh -c \
     'export PATH=$PATH:/root/.dotnet/tools TMPDIR=/proc/1/root/tmp; dotnet tool install -g dotnet-trace >/dev/null 2>&1; dotnet-trace ps'
   ```

3. **Collect**, with the same two exports, for a bounded time:

   | For | Command |
   |---|---|
   | Tick phases (this page) | `dotnet-trace collect -p 1 --providers Avalon-World-Tick:0:4 --duration 00:00:00:30 -o /root/tick.nettrace` |
   | Collections, pauses and what is allocated (sampled by type) | `dotnet-trace collect -p 1 --profile gc-verbose --duration 00:00:05:00 -o /root/gc.nettrace` |
   | Collections and pauses only, cheaper | `dotnet-trace collect -p 1 --profile gc-collect --duration 00:00:05:00 -o /root/gc.nettrace` |

   `gc-verbose` samples allocations and costs the world some throughput while it runs; say so beside any figure taken with it attached.

4. **Copy the file out.**

   ```sh
   kubectl -n avalon exec avalon-world-loadtest-0 -c trace1 -- cat /root/gc.nettrace > gc.nettrace
   ```

Record with each capture the image, the population and mix, the report of the ramp or hold it was taken under, and its start and end times; write the result on the issue it serves, not only in a pull request.

### Reading a GC trace

A `.nettrace` opens in PerfView or Visual Studio. For a summary on any machine, this reader (a console project referencing `Microsoft.Diagnostics.Tracing.TraceEvent`) prints the collections by generation and reason, the pauses, the bytes promoted per collection and the sampled allocation by type, which is what the allocation findings in [GC pressure](gc-pressure.md) were read from:

```csharp
using Microsoft.Diagnostics.Tracing;

using var source = new EventPipeEventSource(args[0]);
var collections = new List<(double At, int Generation, string Reason, string Type)>();
var pauses = new List<(double Ms, int Generation)>();
var allocated = new Dictionary<string, long>();
var promoted = new List<long>();
double suspendedAt = -1;
int generation = -1;

source.Clr.GCSuspendEEStart += e => suspendedAt = e.TimeStampRelativeMSec;
source.Clr.GCStart += e =>
{
    generation = e.Depth;
    collections.Add((e.TimeStampRelativeMSec, e.Depth, e.Reason.ToString(), e.Type.ToString()));
};
source.Clr.GCRestartEEStop += e =>
{
    if (suspendedAt >= 0)
        pauses.Add((e.TimeStampRelativeMSec - suspendedAt, generation));
    suspendedAt = -1;
};
source.Clr.GCHeapStats += e => promoted.Add(e.TotalPromotedSize0);
source.Clr.GCAllocationTick += e =>
    allocated[e.TypeName ?? "?"] = allocated.GetValueOrDefault(e.TypeName ?? "?") + e.AllocationAmount64;
source.Process();

foreach (var group in collections.GroupBy(c => (c.Generation, c.Type, c.Reason)).OrderBy(g => g.Key.Generation))
    Console.WriteLine($"gen{group.Key.Generation} {group.Key.Type} {group.Key.Reason} x{group.Count()}");
foreach (var group in pauses.GroupBy(p => p.Generation).OrderBy(g => g.Key))
    Console.WriteLine($"gen{group.Key} pauses: {group.Count()}, mean {group.Average(p => p.Ms):F2} ms, max {group.Max(p => p.Ms):F2} ms");
if (promoted.Count > 0)
    Console.WriteLine($"promoted from gen0 per collection: mean {promoted.Average() / 1048576.0:F1} MB, max {promoted.Max() / 1048576.0:F1} MB");
foreach (var type in allocated.OrderByDescending(a => a.Value).Take(15))
    Console.WriteLine($"{type.Value / 1048576.0,8:F1} MB  {type.Key}");
```

The allocation figures are sampled (one event per about 100 KB allocated), so they rank types well and are not exact byte counts.

## Correlation and durations

Events are numeric, with no names or player identifiers:

- Event 2, `WorldTick` (version 1): tickId, durationTicks, frequency, completed, sessionPackets. The tickId is the tick-start Stopwatch timestamp supplied to WorldServer.Update. Its duration covers time from that start through the end of Update. sessionPackets is the packets the session pass (the tick's first pass) took off every connection's receive queue.
- Event 3, `InstanceBatch` (version 0): tickId, durationTicks, frequency, instances, completed. This wraps the sequential InstanceTicker pass.
- Event 1, `InstanceTick` (version 1): tickId, instanceLow, instanceHigh, mapType, startTimestamp, durationTicks, frequency, completed, broadcast, players, creatures, projectiles, the phase fields and work counters below, then mapPackets (field 31). The two instance fields are the Int64 halves of Guid.TryWriteBytes output.

Fields are only ever appended, and an event that gains one goes up a version, so a reader that reads by position keeps working on an older trace by stopping at the fields it has. Version 1 (#976) appended `mapPackets` to `InstanceTick` and `sessionPackets` to `WorldTick`; a version 0 trace (the 950-player profile, #974) has neither.

A packet counts when a pass takes it off a connection's receive queue: dispatched to its handler (or logged as having none), or, in the session pass, dropped as stale because its character has left. A packet a pass holds back for the other pass, or for a later tick, is counted by the pass that finally takes it; a connection not yet authorised for gameplay discards its queue uncounted. Map packets are counted by the instance whose character update ran its connection's map pass, so the sum over a tick's instances plus sessionPackets is every packet the tick processed. The scenario harness (`tools/Avalon.Scenarios`, `TickPhaseBenchmarks`) hands its walkers' input straight to the handler, not through a queue, so its records read 0 for both.

Join by process, thread and tickId; preserve 64-bit integers in the reader. A standalone InstanceTicker uses its own start as tickId. A direct MapInstance.Update without an outer ticker has tickId zero. Attaching or detaching mid-tick can leave partial groups: discard uncorrelated boundary groups and any tick containing an incomplete record. Verify the instance count against its batch. A batch may complete even when it contains an instance failure, because InstanceTicker contains those failures.

Convert duration ticks with `milliseconds = ticks * 1000 / frequency`. These are elapsed wall times, including GC and scheduling delays, not CPU samples. World, batch and instance durations nest; never add them together. The instance phases are disjoint. Their difference from instance duration is residual work plus instrumentation overhead. The batch-minus-instances difference includes iteration, metrics and event emission. Use correlated whole-tick records to explain slow ticks; do not add independent percentile values.

## Instance phases and counters

| Field | Measures |
|---|---|
| inputTicks | Character update, including map input, PvP expiry and save scheduling |
| combatTicks | Abilities, combat, threat and aura simulation |
| creatureTicks | Creature scripts and locomotion |
| dirtyTicks | Dirty-field snapshot pass |
| visibilityTicks | Clearing recipient state lists and scanning characters, creatures and projectiles for visibility |
| trackingTicks | Reconciling visible entities with recipient tracking state |
| statePreparationTicks | Preparing recipient add/update lists and state objects |
| stateEncodingTicks | Creating and encoding state add/update/remove packets |
| auraTicks | Aura replication, including its packet creation and enqueue |
| enqueueTicks | Passing state add/update/remove packets to the connection |
| candidateChecks | Entity candidates visited by visibility scans, summed across recipients |
| visiblePairs | Visible recipient/entity pairs, including the watcher itself |
| dirtyEntities | Entities in the instance dirty snapshot |
| added / updated / removed | Recipient tracking deltas; these are pairs, not unique entities |
| statePackets / stateBytes | State packets handed to Send and their plain header-plus-payload bytes; excludes aura packets, sealing and TLS |
| mapPackets | Received packets the map pass took off this instance's connections' queues (version 1; see above). `inputTicks` also covers each character's own update, PvP expiry and save scheduling, so `inputTicks` ÷ `mapPackets` bounds the cost per packet from above |

Players and creatures are roster counts at update entry; projectiles are counted after combat. Broadcast marks whether this tick reaches the state-update cadence. Adds, removes and aura work can occur outside that cadence. Packet counters count the handoff even if the connection later refuses enqueue; they are not socket delivery counters. A failed phase can be absent from the accumulated phase totals, which is why completed=0 records must not be used as successful cheap ticks.

## Density benchmark

```sh
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter '*TickPhaseBenchmarks*' --job short --warmupCount 10 --iterationCount 8
```

TickPhaseBenchmarks moves 300 or 600 players within mutual visibility, either together or across 20 towns. It uses real instance ticks, packet encoding and inline scenario sends/sealing. It excludes real sockets/TLS, database work, AI/combat and input validation, so it isolates density costs rather than reproducing a production world tick. Production sends run on separate threads.

Each invocation contains 42 ticks: six broadcast periods with ScenarioWorld's rounded fixed timestep. Setup warms for 15 seconds. Use the identical harness on both revisions and report allocations and uncertainty alongside timing. Investigate actual captured broadcast counts before comparing short windows. See [benchmarks](benchmarks.md) and [world metrics](instrumentation.md) for complementary measurements.
