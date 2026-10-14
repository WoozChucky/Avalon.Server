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

### Captures for slow ticks (#976)

The 950-player profile ([#974](https://github.com/WoozChucky/Avalon.Server/issues/974), sections 6 and 11) left open why the same work sometimes runs 1.2 to 1.6 times slower, what one 14.5 ms stall outside the instance batch was, whether the tick thread really waits on locks, what an input packet costs, and what the provider itself costs. The captures below answer those. None replaces the other: each adds a kind of event the tick provider's wall times cannot show, in the same session as the provider, so a slow tick can be matched with what happened during it.

**Under a held load, not a ramp.** Hold one population with the load-test tool, `ramp --start N --max N --hold 900s` (one step held 15 minutes; [load testing](load-testing.md#ramp)), with the bot PC otherwise idle, after checking that no players are on the other worlds of the node. Take each capture **two to five minutes long, and repeat it** two or three times in the same hold, rather than one 30 s window: the slow stretches last from 100 ms to a few seconds and cluster, so one short window can miss them or hold only one. The tick provider alone writes about **2.8 MB a second at 121 instances** (84 MB in 30 s at 950 players), so a five-minute capture is about 840 MB; check the debug container's free space (`df -h /root`) before starting, and copy each file out and delete it before the next.

**Reading the times.** `WorldTick`, `InstanceBatch` and `InstanceTick` are written when their span ends, so an event's timestamp is the end of its span and its start is that timestamp less `durationTicks` (converted as below). Use that interval to find what overlaps a tick.

| Capture | Container | Answers |
|---|---|---|
| A. Tick provider with runtime GC, suspension and contention events | `general` (as above) | The stall outside the batch (#974 M4): a GC suspension overlapping the tick, or not. Blocked time on locks (M5): contention on the tick thread, with its duration, during slow ticks or not. Input packets per tick and the cost per packet (the provider's `mapPackets` and `sessionPackets`, version 1) |
| B. Kernel CPU samples and scheduler events with the tick provider | `sysadmin` (privileged; the owner's go-ahead first) | The 1.2–1.6x slowdowns (M1): for each slow tick, whether the tick thread was switched out, woke or migrated to another CPU, and what else ran on its CPU (other threads of the world, its neighbours, network interrupts, M3); on-CPU time per phase |
| C. Per-CPU node usage at one second | any | Whether the node, or the tick thread's CPUs, were busy during a slow stretch: the 35% node figure of #974 is a three-minute average |
| D. Provider overhead | `general` | How much the provider itself adds to the tick (item 5 of #976), so traced figures can be read against unprofiled ones |

#### A. Tick provider with runtime events

Not run on a world pod yet: dotnet-trace 10.0.750501 accepted this provider string, with D's options, against a local process.

In the `general` debug container of steps 1 and 2:

```sh
dotnet-trace collect -p 1 --providers Avalon-World-Tick:0:4,Microsoft-Windows-DotNETRuntime:0x4001:4 \
  --duration 00:00:03:00 -o /root/tick-rt1.nettrace
```

Runtime keyword `0x1` (GC) at level 4 adds each collection's start and end and the execution engine's suspension and restart (`GCSuspendEEBegin`, `GCSuspendEEEnd`, `GCRestartEEBegin`, `GCRestartEEEnd`) without the verbose allocation samples, which are level 5. Keyword `0x4000` (contention) adds `ContentionStart` and `ContentionStop` on the thread that waited, the stop carrying the wait's duration. Both are low-rate. With them a tick whose part outside the batch stalls can be read against a suspension window, and a lock wait on the tick thread (thread of the `WorldTick` events) shows as a contention pair inside a tick, measured where the managed sampler of trace 2 in #974 could only guess.

#### B. Kernel CPU samples and scheduler events (privileged)

Kernel sampling needs `perf_event_open`, and the homelab node has `perf_event_paranoid = 4`, which refuses it to any process without `CAP_PERFMON` or `CAP_SYS_ADMIN`: a `general` debug container cannot sample. The `sysadmin` profile grants those, which makes the container **root on the node**. Use it only with the owner's go-ahead, given in that session, for that capture; give it a short `sleep` so it ends by itself.

```sh
kubectl -n avalon debug avalon-world-loadtest-0 --image=mcr.microsoft.com/dotnet/sdk:10.0 \
  --target=avalon-world --container=ktrace1 --profile=sysadmin -- sleep 1800
```

`collect-linux` is a dotnet-trace 10 command (the 950 capture used 10.0.750501); install that version, with the same two exports as step 2, and probe first that the world can be traced this way (it needs the runtime's user_events support):

```sh
dotnet tool install -g dotnet-trace --version 10.0.750501
dotnet-trace collect-linux --probe -p 1 -o stdout
```

dotnet-trace 10 lists two kernel profiles for `collect-linux` (`dotnet-trace list-profiles`): `cpu-sampling` (kernel CPU sampling, about one sample per millisecond of CPU per thread, with kernel and user stacks) and `thread-time` (kernel context-switch events: when each thread went off and on a CPU, so wall time splits into running and waiting). `--perf-events` adds kernel tracepoints by name (`category:name`); `sched:sched_migrate_task` records each move of a thread to another CPU, and `sched:sched_wakeup` each wake. `--providers` adds .NET providers in the same session, and their events are written through the kernel on the same clock as the samples:

```sh
dotnet-trace collect-linux -p 1 --profile cpu-sampling,thread-time \
  --perf-events sched:sched_migrate_task,sched:sched_wakeup \
  --providers Avalon-World-Tick:0:4,Microsoft-Windows-DotNETRuntime:0x4001:4 \
  --duration 00:00:02:00 -o /root/ktick1.nettrace
```

Kernel events are recorded for every process on the node by default; `-p 1` limits only the .NET events to the world. That is wanted here: what ran on the tick thread's CPU when it was off it is the question. This combination has not been run on the homelab yet (#974 ran `cpu-sampling` alone, without the provider). On the first capture, survey the file before anything else ([the trace reader](#the-trace-reader)'s `survey` mode) and check that it holds `WorldTick` records, samples, context switches and migrations; and expect a larger file than capture A, so start with two minutes.

What to read: for each `WorldTick` over 16.67 ms and a set of median ticks, the tick thread's (the thread writing `WorldTick`) switches out and their off-CPU time inside the tick, its migrations and the CPUs it ran on, the samples on those CPUs of other threads while it was off them, and interrupt handling (`asm_common_interrupt`, `net_rx_action`) on its CPU. A slowdown with no time off the CPU and no interrupts points instead to the thread running slower on the CPU (its hyperthread sibling busy, cache, frequency), which needs C and hardware counters.

**Reading kernel samples.** `dotnet-trace convert` cannot read these samples: it produced an empty speedscope from the 950 capture. The trace reader's `kcpu` mode does ([the trace reader](#the-trace-reader)): it reads the file through TraceEvent's `TraceLog`, which resolves the stacks. Managed frames resolve from the trace's own method events and kernel frames as `vmlinux!...`; `libcoreclr.so` and some runtime stubs stay as raw addresses unless symbols for that exact runtime build are found (`dotnet-symbol`). On a thread busy throughout, consecutive samples were 1.000 ms apart, so one sample counts as one millisecond of CPU.

**Hardware counters** (cycles, instructions, cache misses on the tick thread) are not covered by a tried recipe. `collect-linux --perf-events` takes tracepoints; whether it takes hardware events was not tried. From the same privileged container, a `perf` built for the node's kernel (6.8) could count them per second for the tick thread's TID (`perf stat -e cycles,instructions,cache-misses -t <tid> -I 1000`), to line up with the ticks by wall time.

#### C. Per-CPU node usage at one second

Not run on the homelab yet.

Prometheus's node figures are scraped too coarsely to show a 100 ms to few-second stretch. `/proc/stat` is not per-container, so any debug container reads the node's per-CPU counters; sample it every second for the length of the capture, beside A or B:

```sh
sh -c 'end=$(( $(date +%s) + 180 )); while [ $(date +%s) -lt $end ]; do date -u +%s.%N; grep "^cpu[0-9]" /proc/stat; sleep 1; done' > /root/cpu1.txt
```

Busy share of a CPU over an interval is the change in its non-idle fields (all but `idle` and `iowait`) over the change in all of them. Line the seconds up with the trace by UTC wall time (a `.nettrace` records its session's start time).

#### D. Provider overhead

Not run on a world pod yet.

No unprofiled run exists in the same window as a traced one (#974, provider overhead caveat). In one hold, alternate the provider on and off in 30 s windows, logging each window's UTC start and end:

```sh
for i in 1 2 3 4 5 6 7 8 9 10; do
  date -u +on-%H:%M:%S
  dotnet-trace collect -p 1 --providers Avalon-World-Tick:0:4 --rundown:false --duration 00:00:00:30 -o /root/on$i.nettrace
  date -u +off-%H:%M:%S; sleep 30
  rm -f /root/on$i.nettrace
done
```

`--rundown:false` skips the method rundown at the end of each session, which the tick provider's numeric records do not need and which would otherwise land at the start of each off window. Compare the world's own `world.tick.duration` histogram over the on windows against the off windows, from Prometheus: on the load-test world it is exported every 10 s, so drop the export interval each switch falls in and keep the two whole intervals of each window. Compare the mean (the histogram's `_sum` over its `_count`, exact) and the share of ticks over 16,667 µs (a bucket edge, exact); its p99 is interpolated inside the 16,667–25,000 µs bucket, too coarse to resolve a difference of a millisecond. Ten pairs give twenty intervals a side; repeat in a second hold before calling the difference.

### The trace reader

`tools/Avalon.TraceReader` reads these captures into plain files for analysis (a script, a spreadsheet); the analysis itself stays outside the repository. It runs on any machine with the .NET SDK, against a copied-out `.nettrace`:

```sh
dotnet run -c Release --project tools/Avalon.TraceReader -- survey tick.nettrace
dotnet run -c Release --project tools/Avalon.TraceReader -- tick tick.nettrace out/tick
dotnet run -c Release --project tools/Avalon.TraceReader -- kcpu ktick1.nettrace out/kcpu
```

A bad command line prints the usage and exits 2; a missing or unreadable trace exits 1.

- **`survey <trace>`** prints the session's start (UTC), length, pointer size and processor count, every event type in the trace (provider, event name, id and version) with its count, and the events per process. Run it first on a capture of a new kind. Runtime rundown events are listed under their provider with raw task and event ids.
- **`tick <trace> <out-dir>`** writes the `Avalon-World-Tick` records to `instance.csv` (event 1), `world.csv` (event 2) and `batch.csv` (event 3), one row per event, and prints the row counts. Each file's columns are `thread` (the writing thread's id), `ts_ms` (the event's time in ms from the session start, four decimals; the end of the record's span, see [correlation](#correlation-and-durations)), then the event's fields in the provider's order under their own names (the lists in [correlation and durations](#correlation-and-durations) and [instance phases](#instance-phases-and-counters)). Each column is filled from the field of its name, so a version 0 record (before #976) leaves `mapPackets` and `sessionPackets` empty. Values are written as 64-bit integers, never through a floating-point type. A field the reader has no column for is named on the console and not written: the reader's columns are kept equal to the provider's fields by `TickTraceReaderShould`.
- **`kcpu <trace> <out-dir>`** reads a `collect-linux` trace through TraceEvent's `TraceLog` (`TraceLog.CreateFromEventPipeDataFile`; the `trace.etlx` it builds stays in the output directory) and writes two tab-separated files with a header row:
  - `ksamples.tsv`, one row per CPU sample: `pid`, `tid`, `cpu` (the processor it was taken on) and `ts_ms` (ms from the session start).
  - `kstacks.tsv`, every event that carries a stack, counted: `pid`, `tid`, `event` (`provider/name`; a CPU sample is `Universal.Events/cpu`), `count` and `stack` (root first, frames joined by `;`, each `module!method`, or `module!0xaddress` where the module has no symbols), most frequent first.

  It also prints the processes and threads the trace holds and the samples per processor.

Read against the 950-player captures (#974), `tick` wrote 217,934 instance, 1,800 world and 1,800 batch rows from `tick.nettrace` (version 0: the two packet columns empty), and `kcpu` 155,757 sample rows and 34,544 stack rows from `kcpu.nettrace`, 22,566 of the samples on the tick thread, as the report counts.

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
| statePreparationTicks | Describing the entities a recipient is added or updated: since #980 each entity's entry is described once per broadcast, by the first recipient sent it, and a character's own entry for itself alone |
| stateEncodingTicks | Encoding each entity's add and update entry once per broadcast, copying the entries into each recipient's add and update packet, and creating those and the remove packets (#980) |
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
