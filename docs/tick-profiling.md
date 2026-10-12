# Tick phase traces

The opt-in `Avalon-World-Tick` EventSource breaks down steady-state simulation and replication work without adding per-player metric series. It is disabled without a listener. Collect a bounded trace on a deployed build that contains the provider:

```sh
dotnet-trace collect --process-id <world-pid> --providers Avalon-World-Tick:0:4 --duration 00:00:30 --output tick-phases.nettrace
```

Use the world's diagnostic socket/PID from the diagnostic container in Kubernetes. Keep captures outside the repository. Capture only after the population has settled; record image, population, instance distribution, scenario, start/end times and concurrent collectors. Compare with an unprofiled run: enabled timestamp reads and event emission have a measurable cost. Do not use a traced run as a capacity verdict.

## Correlation and durations

Events are numeric, with no names or player identifiers:

- Event 2, `WorldTick`: tickId, durationTicks, frequency, completed. The tickId is the tick-start Stopwatch timestamp supplied to WorldServer.Update. Its duration covers time from that start through the end of Update.
- Event 3, `InstanceBatch`: tickId, durationTicks, frequency, instances, completed. This wraps the sequential InstanceTicker pass.
- Event 1, `InstanceTick`: tickId, instanceLow, instanceHigh, mapType, startTimestamp, durationTicks, frequency, completed, broadcast, players, creatures, projectiles, the phase fields and work counters below. The two instance fields are the Int64 halves of Guid.TryWriteBytes output.

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

Players and creatures are roster counts at update entry; projectiles are counted after combat. Broadcast marks whether this tick reaches the state-update cadence. Adds, removes and aura work can occur outside that cadence. Packet counters count the handoff even if the connection later refuses enqueue; they are not socket delivery counters. A failed phase can be absent from the accumulated phase totals, which is why completed=0 records must not be used as successful cheap ticks.

## Density benchmark

```sh
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter '*TickPhaseBenchmarks*' --job short --warmupCount 10 --iterationCount 8
```

TickPhaseBenchmarks moves 300 or 600 players within mutual visibility, either together or across 20 towns. It uses real instance ticks, packet encoding and inline scenario sends/sealing. It excludes real sockets/TLS, database work, AI/combat and input validation, so it isolates density costs rather than reproducing a production world tick. Production sends run on separate threads.

Each invocation contains 42 ticks: six broadcast periods with ScenarioWorld's rounded fixed timestep. Setup warms for 15 seconds. Use the identical harness on both revisions and report allocations and uncertainty alongside timing. Investigate actual captured broadcast counts before comparing short windows. See [benchmarks](benchmarks.md) and [world metrics](instrumentation.md) for complementary measurements.
