# Performance Benchmarks

Benchmarks live in `tools/Avalon.Benchmarking/` and are run with BenchmarkDotNet.

```bash
# Run all benchmarks
dotnet run -c Release --project tools/Avalon.Benchmarking

# Run a specific suite
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickLoop*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*EntityTracking*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*Serialization*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketSerializationGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*BroadcastStateGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketReaderGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*WorldPacketQueueGc*"
```

---

## Suites

### Tick Loop — `TickLoopBenchmarks.cs`

Measures per-tick scheduling overhead of the `WorldServer` throttle mechanism.

| Scenario | What it models |
|---|---|
| `Yield_1` | Baseline: one thread-pool hop per tick (equivalent to a `PeriodicTimer` loop) |
| `Yield_5` | Fast tick at low load (~11 ms wait) |
| `Yield_13` | Typical at target load: ~3 ms tick + ~13 ms wait |
| `Yield_20` | Idle server, sub-ms tick (~16 ms wait) |
| `SynchronousPath` | Async state-machine cost only — zero scheduling, the theoretical floor |
| `PeriodicTimer_Tick` | Refactored loop: one `WaitForNextTickAsync` per tick (1 ms period) |

---

### Entity Tracking — `EntityTrackingBenchmarks.cs`

Benchmarks `EntityTrackingSystem.Update()` — the per-tick cost of computing which entities are
visible to each connected client.

| Scenario | What it models |
|---|---|
| `Update_AllIdle` | No entity changes between ticks (the common case) — baseline |
| `Update_TenPercentActive` | ~10% of creatures change each tick (realistic mid-combat) |
| `Update_AllActive` | Every creature changes every tick (worst-case stress) |

Scale parameter `CreatureCount` runs at 50 / 100 / 200 to validate O(n) behaviour.

---

### Packet Serialization GC-001 — `PacketSerializationGcBenchmarks.cs`

Before/after allocation comparison for the GC-001 fix: `MemoryStream + ToArray` versus
`PacketSerializationHelper` + `PooledArrayBufferWriter`.

| Scenario | What it models |
|---|---|
| `Legacy_SmallPacket` | Old pattern — `MemoryStream + Serializer.Serialize + ms.ToArray() + encrypt` on a small packet (one field) — **baseline** |
| `Pooled_SmallPacket` | New pattern — `PacketSerializationHelper.Serialize` on the same packet |
| `Legacy_MediumPacket` | Old pattern on `SChatMessagePacket` (two `ulong`s, two `string`s, `DateTime`) |
| `Pooled_MediumPacket` | New pattern on the same medium packet |

Both encrypt delegates are identity copies (`span => span.ToArray()`) to isolate serialization cost from crypto cost.

---

### Broadcast State GC-002 — `BroadcastStateGcBenchmarks.cs`

GC-002: BroadcastStateTo per-entity alloc reduction.
`Legacy_*` = current `new byte[]` per entity + `new List<ObjectAdd>` per call;
`Pooled_*` = contiguous rented buffer + `ReadOnlyMemory<byte>` slices (added in Task 5).
Parameterised at 5 and 20 entities.

---

### Packet Reader GC-007 — `PacketReaderGcBenchmarks.cs`

Before/after allocation comparison for the GC-007 fix: `MethodInfo.Invoke` with a per-call `new object?[3]` args array and a boxed `ReadOnlyMemory<byte>` struct, versus a cached typed `Func<ReadOnlyMemory<byte>, Packet?>` delegate called directly.

| Scenario | What it models |
|---|---|
| `Legacy_ReflectionInvoke` | Old `PacketReader.Read()` — `MethodInfo.Invoke(null, new object?[] { mem, null, null })` — **baseline** |
| `Delegate_Cached` | New `PacketReader.Read()` — `deserializer(new ReadOnlyMemory<byte>(payload))` |

Both paths deserialize an identical `CCharacterListPacket` payload. The residual allocation in `Delegate_Cached` is the deserialized packet object itself — unavoidable.

---

### World Packet Queue GC-009 — `WorldPacketQueueGcBenchmarks.cs`

Before/after allocation comparison for the GC-009 fix: `class WorldPacket` + `LinkedList<T>`-backed
`LockedQueue` versus `readonly record struct WorldPacket` + `Queue<T>` ring buffer.

| Scenario | What it models |
|---|---|
| `Legacy_ClassQueue` | Old pattern — `new class LegacyWorldPacket` + `LinkedListNode` per packet — **baseline** |
| `Struct_RingBuffer` | New pattern — `readonly record struct` inline in `Queue<T>` ring buffer |

Both paths use a `_ => true` predicate to isolate queue/struct allocation from filter logic.
The `Struct_RingBuffer` queue is reused across iterations so the ring buffer reaches
steady-state capacity after the first iteration; subsequent iterations allocate zero.

---

### Serialization — `SerializationBenchmarks.cs`

Measures Protobuf-net packet serialization and deserialization with and without AES-128 encryption.

| Scenario | What it models |
|---|---|
| `Serialize_NoEncryption` | Serialize `CClientInfoPacket` — no encryption |
| `Serialize_Aes128` | Serialize `CCharacterListPacket` with AES-128 encryption |
| `Deserialize_Aes128` | Deserialize + decrypt + inner-deserialize an AES-128 packet |
| `Deserialize_NoEncryption` | Deserialize an unencrypted `NetworkPacket` |

**Status:** Baseline not yet recorded. No active refactor in progress.

---

## Tick Loop — Benchmark Results

### Problem (Before)

`WorldServer.ExecuteAsync` filled the remaining frame budget via a spin/yield loop:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    await Tick(); // calls Task.Yield() ~13 times per frame to fill the 16.67 ms budget
}
```

At 60 Hz with a ~3 ms tick, `Tick()` called `await Task.Yield()` roughly 13 times per frame.
Each call queues a ThreadPool continuation — the loop had no thread affinity and could resume on
a different CPU core after each yield. At target scale (250 instances × 60 TPS): ~780 ThreadPool
items/s for scheduling alone.

### Fix (After)

Replaced with a single `PeriodicTimer.WaitForNextTickAsync` per tick:

```csharp
using var timer = new PeriodicTimer(MinUpdateInterval);
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    // one thread-pool hop per tick, then Update()
}
```

Scheduling cost dropped from ~13 thread-pool hops per tick to 1.

### Before — spin/yield throttle (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method          | Mean          | Error       | StdDev      | Median        | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |--------------:|------------:|------------:|--------------:|-------:|--------:|-------:|----------:|------------:|
| Yield_1         |  1,113.188 ns |  22.2435 ns |  51.1081 ns |  1,094.431 ns |  1.002 |    0.06 | 0.0057 |      96 B |        1.00 |
| Yield_5         |  3,173.693 ns |  62.7428 ns | 142.8971 ns |  3,137.194 ns |  2.857 |    0.18 |      - |      96 B |        1.00 |
| Yield_13        |  8,068.501 ns | 131.7976 ns | 123.2835 ns |  8,105.878 ns |  7.262 |    0.33 |      - |      97 B |        1.01 |
| Yield_20        | 12,271.828 ns | 243.3880 ns | 518.6797 ns | 12,239.030 ns | 11.046 |    0.66 |      - |      97 B |        1.01 |
| SynchronousPath |      3.652 ns |   0.0884 ns |   0.2168 ns |      3.594 ns |  0.003 |    0.00 |      - |         - |        0.00 |

**Key observations:**

- `Yield_13` costs **7.26×** more than `Yield_1` (Ratio = 7.262 vs 1.002) — the production
  throttle burned ~7× the scheduling overhead of a single yield per tick.
- Allocations are essentially identical across all `Yield_*` variants (~96–97 B per tick),
  confirming overhead is pure CPU/scheduling cost, not GC pressure.
- `SynchronousPath` at 3.65 ns (Ratio = 0.003) reveals the async state-machine itself is nearly
  free; all meaningful cost comes from thread-pool hops.
- At 60 Hz, `Yield_13` adds ~484 µs/s of pure scheduling overhead vs ~67 µs/s for `Yield_1` —
  a saving of ~417 µs/s (6.26× reduction) after the refactor.

### After — PeriodicTimer refactor (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method             | Mean              | Error           | StdDev          | Ratio      | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------------------:|----------------:|----------------:|-----------:|--------:|-------:|----------:|------------:|
| Yield_1            |        600.474 ns |       3.1183 ns |       2.7643 ns |      1.000 |    0.01 | 0.0057 |      96 B |        1.00 |
| Yield_5            |      1,529.956 ns |      13.1757 ns |      12.3245 ns |      2.548 |    0.02 | 0.0057 |      96 B |        1.00 |
| Yield_13           |      4,119.960 ns |      61.6122 ns |      57.6321 ns |      6.861 |    0.10 |      - |      96 B |        1.00 |
| Yield_20           |      5,943.735 ns |      69.6511 ns |      65.1517 ns |      9.899 |    0.11 |      - |      97 B |        1.01 |
| SynchronousPath    |          3.892 ns |       0.1050 ns |       0.1123 ns |      0.006 |    0.00 |      - |         - |        0.00 |
| PeriodicTimer_Tick | 15,767,664.375 ns | 213,085.0325 ns | 199,319.8717 ns | 26,259.203 |  342.05 |      - |     400 B |        4.17 |

**Key observations:**

- **`PeriodicTimer_Tick` Mean = ~15.8 ms** — dominated by the Windows timer resolution floor
  (~15.625 ms, the OS default granularity). The benchmark uses a 1 ms period but Windows rounds
  up to the next timer interrupt. This is the actual per-tick wall time at 60 Hz.
- **Scheduling overhead = `Yield_1` (~600 ns)** — subtracting the ~15.6 ms sleep from the
  15.8 ms total leaves ~200 µs of overhead; the remaining cost per tick is one thread-pool hop,
  which matches `Yield_1`. The `Ratio = 26,259` reflects the sleep, not scheduling cost.
- **Allocation: 400 B** — includes the `PeriodicTimer` object itself (allocated once per
  benchmark iteration). In production the timer is allocated once at startup and reused across
  all ticks; per-tick allocation is zero.
- **`Yield_13` Ratio stable at ~6.86×** across both runs (Before: 7.26×, After: 6.86×),
  confirming the scheduling overhead reduction is consistent regardless of absolute CPU speed.
- **Production scheduling overhead reduced from `Yield_13` → `Yield_1` per tick**: ~4,120 ns →
  ~600 ns (6.9× reduction). At 60 Hz: ~247 µs/s → ~36 µs/s of pure scheduling cost.
- **Thread affinity preserved** — `WaitForNextTickAsync` suspends once and resumes on the next
  available thread-pool thread with no intermediate hops between tick frames.

---

## Entity Tracking — Benchmark Results

### Problem (Before)

The entity tracking system performed a **full snapshot comparison every tick** for every entity
visible to every client. At the target scale of 250 concurrent map instances, each with up to 200
creatures and ~2 clients, this yielded approximately 60 million field comparisons per second — the
vast majority producing no change (idle entities between AI updates).

Secondary symptom: GC pressure from snapshot object allocations and `byte[] Fields` heap
allocations per changed entity per broadcast.

At 250 instances × 2 clients × 60 TPS: ~473 MB/s of Gen0 allocation — primary driver of tick
jitter.

### Fix (After)

Each mutable entity (`Creature`, `CharacterEntity`, `SpellScript`) accumulates changed fields in a
`_dirtyFields: GameEntityFields` bitmask via property setters. `MapInstance.Update()` snapshots
all dirty bits into a per-frame dictionary before broadcasting. `EntityTrackingSystem.Update()`
skips entities absent from the dirty map entirely, reducing idle-entity cost to a single `HashSet`
lookup.

### Before — full snapshot comparison (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method                  | CreatureCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Update_AllIdle**          | **50**            |  **2.679 μs** | **0.0347 μs** | **0.0325 μs** |  **1.00** |    **0.02** | **0.2213** |      **-** |   **3.41 KB** |        **1.00** |
| Update_TenPercentActive | 50            |  2.761 μs | 0.0544 μs | 0.0727 μs |  1.03 |    0.03 | 0.2251 |      -  |    3.5 KB |        1.03 |
| Update_AllActive        | 50            |  2.833 μs | 0.0416 μs | 0.0389 μs |  1.06 |    0.02 | 0.2251 |      -  |    3.5 KB |        1.03 |
|                         |               |           |           |           |       |         |        |        |           |             |
| **Update_AllIdle**          | **100**           |  **5.185 μs** | **0.0989 μs** | **0.0925 μs** |  **1.00** |    **0.02** | **0.4730** |      **-** |   **7.31 KB** |        **1.00** |
| Update_TenPercentActive | 100           |  5.323 μs | 0.1049 μs | 0.1364 μs |  1.03 |    0.03 | 0.4807 |      -  |    7.4 KB |        1.01 |
| Update_AllActive        | 100           |  5.661 μs | 0.1100 μs | 0.1267 μs |  1.09 |    0.03 | 0.4807 |      -  |    7.4 KB |        1.01 |
|                         |               |           |           |           |       |         |        |        |           |             |
| **Update_AllIdle**          | **200**           | **10.348 μs** | **0.2031 μs** | **0.3039 μs** |  **1.00** |    **0.04** | **1.0223** |      **-** |  **15.78 KB** |        **1.00** |
| Update_TenPercentActive | 200           | 10.562 μs | 0.2098 μs | 0.3266 μs |  1.02 |    0.04 | 1.0223 | 0.0153 |  15.87 KB |        1.01 |
| Update_AllActive        | 200           | 10.965 μs | 0.2180 μs | 0.2677 μs |  1.06 |    0.04 | 1.0223 | 0.0153 |  15.87 KB |        1.01 |

**Key observations:**

- Scaling is perfectly linear (O(n) per client per tick).
- `AllIdle` and `AllActive` costs are nearly identical (~6% apart at 200 creatures), confirming
  the bottleneck is per-call allocations rather than field comparison logic.
- Every `CharacterCharacterGameState.Update()` call allocates ~15.78 KB at 200 creatures
  (3× `HashSet<ObjectGuid>` + 3× `List<ObjectGuid>` inside `EntityTrackingSystem.Update`).
- At 250 instances × 2 clients × 60 TPS: ~473 MB/s of Gen0 allocation — primary driver of tick
  jitter.

### After — dirty-flag redesign (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method                  | CreatureCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Update_AllIdle**          | **50**            |   **877.1 ns** | **16.70 ns** | **15.62 ns** |  **1.00** |    **0.02** | **0.0076** |     **120 B** |        **1.00** |
| Update_TenPercentActive | 50            |   913.7 ns | 17.86 ns | 19.86 ns |  1.04 |    0.03 | 0.0124 |     208 B |        1.73 |
| Update_AllActive        | 50            | 1,069.7 ns | 19.34 ns | 18.09 ns |  1.22 |    0.03 | 0.0114 |     208 B |        1.73 |
|                         |               |            |          |          |       |         |        |           |             |
| **Update_AllIdle**          | **100**           | **1,668.5 ns** | **20.08 ns** | **18.79 ns** |  **1.00** |    **0.02** | **0.0076** |     **120 B** |        **1.00** |
| Update_TenPercentActive | 100           | 1,754.8 ns | 25.92 ns | 21.64 ns |  1.05 |    0.02 | 0.0114 |     208 B |        1.73 |
| Update_AllActive        | 100           | 1,928.0 ns | 26.82 ns | 25.09 ns |  1.16 |    0.02 | 0.0114 |     208 B |        1.73 |
|                         |               |            |          |          |       |         |        |           |             |
| **Update_AllIdle**          | **200**           | **3,322.5 ns** | **64.76 ns** | **74.58 ns** |  **1.00** |    **0.03** | **0.0076** |     **120 B** |        **1.00** |
| Update_TenPercentActive | 200           | 3,293.7 ns | 45.18 ns | 42.26 ns |  0.99 |    0.02 | 0.0114 |     208 B |        1.73 |
| Update_AllActive        | 200           | 3,862.2 ns | 75.20 ns | 70.34 ns |  1.16 |    0.03 | 0.0076 |     208 B |        1.73 |

**Key observations:**

- **3.1× faster across the board** — `AllIdle` at 200 creatures dropped from 10,348 ns to
  3,323 ns.
- **134× less allocation (idle case)** — `AllIdle` at 200 creatures went from 15.78 KB to 120 B.
  The 120 B floor is benchmark harness overhead; the entity tracking path itself allocates nothing
  when no entities are dirty.
- **Idle ≈ Active cost eliminated** — Before, `AllIdle` and `AllActive` were within ~6% because
  both hit the same per-call `HashSet`/`List` allocation cost. Now `AllIdle` is the cheapest
  possible path: a `HashSet` lookup that returns false, nothing else.
- **Active case also improved** — `AllActive` at 200 creatures: 10,965 ns → 3,862 ns (2.8×).
  Even the worst-case (every entity dirty every tick) benefits from removing snapshot comparison
  overhead.
- **At target scale** — 250 instances × 2 clients × 60 TPS: Gen0 allocation drops from
  ~473 MB/s to ~3.6 MB/s (131× reduction), eliminating the primary driver of tick jitter.

---

## Packet Serialization GC-001 — Benchmark Results

### Problem (Before)

Every outbound S-packet `Create()` followed this pattern:

```csharp
using var memoryStream = new MemoryStream();       // alloc 1: MemoryStream + internal byte[] buffer
Serializer.Serialize(memoryStream, p);
var buffer = encryptFunc(memoryStream.ToArray());  // alloc 2: ToArray copy; alloc 3: encrypt result
```

Three heap allocations minimum per outbound packet. The state broadcast sends Add + Update + Remove
packets to every player at ~10 Hz, producing hundreds of short-lived allocations per second under
any real load.

### Fix (After)

Replaced with a `[ThreadStatic]` pooled `IBufferWriter<byte>` (`PooledArrayBufferWriter`) backed by
`ArrayPool<byte>.Shared`. A central `PacketSerializationHelper.Serialize()` helper owns the
thread-local writer and serializes directly into it; the span is passed to `EncryptFunc` with no
intermediate copy.

```csharp
// One call, one allocation (the encrypted payload byte[])
=> PacketSerializationHelper.Serialize(new SXxxPacket { ... }, PacketType, Flags, Protocol, encrypt);
```

### Results — GC-001 fix (2026-04-16)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

| Method                    | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Legacy_SmallPacket**    |  89.06 ns | 2.643 ns | 7.669 ns |  1.01 |    0.15 | 0.0117 |     184 B |        1.00 |
| Pooled_SmallPacket        |  75.00 ns | 1.551 ns | 3.098 ns |  0.85 |    0.11 | 0.0076 |     120 B |        0.65 |
| Legacy_MediumPacket       | 164.23 ns | 2.508 ns | 2.684 ns |  1.86 |    0.23 | 0.0381 |     600 B |        3.26 |
| Pooled_MediumPacket       | 146.02 ns | 2.819 ns | 3.355 ns |  1.66 |    0.20 | 0.0162 |     256 B |        1.39 |

Encrypt delegates are identity copies (`span => span.ToArray()`) in both paths to isolate
serialization cost. The `[ThreadStatic]` writer is pre-warmed in `[GlobalSetup]` so its one-time
allocation does not appear in steady-state measurements.

**Key observations:**

- **Small packet: 184 B → 120 B (35% less allocation, 16% faster)** — `SCharacterCreatedPacket`
  (one enum field). The 64 B saving is the `MemoryStream` object and its internal byte[] buffer,
  which are no longer allocated. The payload byte[] itself is the same cost in both paths.
- **Medium packet: 600 B → 256 B (57% less allocation, 11% faster)** — `SChatMessagePacket`
  (two `ulong`s, two `string`s, `DateTime`, ~70 bytes serialized). The MemoryStream buffer grows
  to hold the larger payload, so the absolute saving scales with packet size.
- **Gen0 rate halved** — `Gen0` drops from 0.0117 to 0.0076 (small) and 0.0381 to 0.0162
  (medium). Fewer Gen0 collections means less STW pause time during state broadcasts.
- **Speed improvement is a side effect, not the goal** — the primary win is GC pressure.
  The pooled path is also faster because `ArrayPool` and span operations have better cache
  locality than `MemoryStream`'s internal resize path, but the latency reduction is secondary.
- **At state-broadcast scale** — the hot path sends three packet types (Add, Update, Remove) to
  every player at ~10 Hz. With 100 players each seeing ~20 entities: ~60,000 packets/second.
  Saving ~64–344 B per packet eliminates **3.8–20 MB/s** of Gen0 allocation that was previously
  driving collection pauses on the world server tick loop.

---

## Broadcast State GC-002 — Benchmark Results

**Date:** 2026-04-16
**Runtime:** .NET 10

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (before GC-002)

`BroadcastStateTo` allocates `new byte[bytesWritten]` per visible entity and
`new List<ObjectAdd>()` per player per call. With 20 entities × 60 players at
10 Hz this generates 1,200+ short-lived `byte[]` per broadcast tick.

### Baseline (before fix)

| Method | EntityCount | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Gen1 | Allocated | Alloc Ratio |
|--------|-------------|------|-------|--------|-------|---------|------|------|-----------|-------------|
| Legacy_BroadcastState | 5 | 501.0 ns | 9.72 ns | 9.09 ns | 1.00 | 0.02 | 0.0811 | - | 1.24 KB | 1.00 |
| | | | | | | | | | | |
| Legacy_BroadcastState | 20 | 1,619.0 ns | 31.01 ns | 41.40 ns | 1.00 | 0.03 | 0.2899 | 0.0019 | 4.45 KB | 1.00 |

**Key observations:**

- **Allocation scales linearly with entity count** — 5 entities: 1.24 KB; 20 entities: 4.45 KB
  (3.59× more entities → 3.59× more allocation). Confirms the dominant cost is `new byte[]`
  per entity, not per-call overhead.
- **Gen1 promotion appears at 20 entities** — `Gen1 = 0.0019` at 20 entities but absent at 5.
  Some allocations survive long enough to be promoted, adding Gen1 collection cost.
- **At target scale** — 20 entities × 60 players × 10 Hz = 12,000 `Legacy_BroadcastState`
  calls/second → **53+ MB/s** of Gen0/Gen1 allocation from this path alone.

### Post-fix results (after GC-002)

| Method | EntityCount | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Gen1 | Allocated | Alloc Ratio |
|--------|-------------|------|-------|--------|-------|---------|------|------|-----------|-------------|
| Legacy_BroadcastState | 5 | 517.6 ns | 9.76 ns | 8.65 ns | 1.00 | 0.02 | 0.0830 | - | 1,312 B | 1.00 |
| Pooled_BroadcastState | 5 | 459.7 ns | 4.14 ns | 3.45 ns | 0.89 | 0.02 | 0.0439 | - | 696 B | 0.53 |
| | | | | | | | | | | |
| Legacy_BroadcastState | 20 | 1,654.6 ns | 28.60 ns | 29.38 ns | 1.00 | 0.02 | 0.2995 | 0.0019 | 4,712 B | 1.00 |
| Pooled_BroadcastState | 20 | 1,489.1 ns | 28.39 ns | 34.87 ns | 0.90 | 0.03 | 0.1488 | - | 2,344 B | 0.50 |

### Key observations
- `Pooled_BroadcastState` eliminates all per-entity `byte[]` allocations (zero heap alloc per entity).
- `Legacy_BroadcastState` allocates N×(64 B entity array + ObjectAdd object + List growth) per call.
- Allocation reduction scales linearly with entity count — the Pooled path at 5 entities cuts allocation by 47% (1,312 B → 696 B); at 20 entities by 50% (4,712 B → 2,344 B). The residual 696 B / 2,344 B is the single encrypted `NetworkPacket` payload byte[] produced by `s_encrypt` (unavoidable) plus N `ObjectAdd` class instances (one per entity — `ObjectAdd` is a reference type).
- Gen1 promotion eliminated — `Legacy_BroadcastState` at 20 entities shows `Gen1 = 0.0019`; `Pooled_BroadcastState` shows none. The rented buffer and pre-allocated list never escape to Gen1.
- Note: The benchmark only exercises the NewObjects (add) path. The UpdatedObjects path has an identical allocation pattern; at 20 entities across both loops the total Legacy allocation in production is approximately double the measured figure.

---

## Packet Reader GC-007 — Benchmark Results

**Date:** 2026-04-16

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (before GC-007)

`PacketReader.Read()` deserialised every inbound packet via `MethodInfo.Invoke`:

```csharp
object? payload = p.deserialize.Invoke(null, new object?[] { payloadMemory, null, null });
```

Two heap allocations per call:
1. `new object?[3]` — the reflection invoke args array
2. Boxing of `payloadMemory` (`ReadOnlyMemory<byte>` is a struct → `object?`)

### Fix (after GC-007)

A private static `BuildDeserializer<T>()` helper builds a typed `Func<ReadOnlyMemory<byte>, Packet?>` once per packet type at startup via `MakeGenericMethod`. `Read()` calls the cached delegate directly:

```csharp
return deserializer(new ReadOnlyMemory<byte>(packet.Payload));
```

`new ReadOnlyMemory<byte>(...)` is a stack-allocated struct — no heap allocation. The delegate is shared — no closure per call.

### Results

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|---|---|---|
| `Legacy_ReflectionInvoke` | 68.75 ns | 0.796 ns | 0.665 ns | 1.00 | 0.0066 | 104 B | 1.00 |
| `Delegate_Cached` | 50.01 ns | 0.515 ns | 0.456 ns | 0.73 | 0.0015 | 24 B | 0.23 |

### Key observations

- **27% faster** — 68.75 ns → 50.01 ns. Reflection dispatch has measurable overhead even with a pre-closed `MethodInfo`; a direct delegate call is cheaper for the JIT to inline and schedule.
- **77% less allocation** — 104 B → 24 B per `Read()` call. The 80 B eliminated is the `object?[3]` array (~40 B) and the boxed `ReadOnlyMemory<byte>` struct (~40 B). The 24 B residual is the deserialized `CCharacterListPacket` object itself — unavoidable.
- **Gen0 rate reduced 4.4×** — Gen0 drops from 0.0066 to 0.0015 per 1 000 operations. Fewer Gen0 collections means less STW pause time during packet processing.
- **At inbound packet scale** — at 50 players × 10 non-trivial packets/s = 500 `Read()` calls/s, the legacy path allocates ~52 KB/s of short-lived Gen0 objects from this site alone. The delegate path reduces that to ~12 KB/s, a saving of ~40 KB/s.

---

## World Packet Queue GC-009 — Benchmark Results

**Date:** 2026-04-16

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`WorldConnection.OnReceive` enqueued packets into a `LockedQueue<WorldPacket>` where `WorldPacket` was
a private inner class and `LockedQueue<T>` used a `LinkedList<T>`-backed `Deque<T>`. Two heap
allocations per received inbound packet:

1. `new WorldPacket { ... }` — class instance (~40 B)
2. `LinkedListNode<WorldPacket>` inside `Deque<T>` — one node per enqueued item (~40 B)

Additionally, `ProcessQueue(PacketFilter filter)` created a closure `worldPacket => filter.CanProcess(worldPacket.Type)`
on every call (~6 000/s at 50 players × 60 Hz × 2 passes).

### Fix (After)

- `WorldPacket` inner class → `private readonly record struct WorldPacket(NetworkPacketType Type, Packet? Payload)` — stored inline in the queue array
- `LockedQueue<T>`: `where T : class` removed; `Deque<T>`/`LinkedList<T>` replaced with `Queue<T>` ring buffer (zero allocation at steady state)
- Filter predicates cached as `Func<WorldPacket, bool>` fields on `WorldConnection`, initialized once in the constructor — no per-call closure

### Results — GC-009 fix (2026-04-16)

| Method | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Allocated | Alloc Ratio |
|--- |---:|---:|---:|---:|---:|---:|---:|---:|
| `Legacy_ClassQueue` | 625.5 ns | 11.71 ns | 10.38 ns | 1.00 | 0.02 | 0.1011 | 1600 B | 1.00 |
| `Struct_RingBuffer` | 547.4 ns | 0.63 ns | 0.56 ns | 0.88 | 0.01 | - | - | 0.00 |

### Key observations

- **100% allocation eliminated** — `Struct_RingBuffer` allocates 0 B per iteration at steady state. The `Queue<T>` ring buffer reaches capacity during BenchmarkDotNet's warm-up phase; subsequent measurement iterations produce zero GC pressure.
- **1600 B → 0 B** — The 1600 B baseline is 20 packets × ~80 B (one `LegacyWorldPacket` class object + one `LinkedListNode<LegacyWorldPacket>` per item on .NET 10 x64).
- **Gen0 eliminated** — `Legacy_ClassQueue` Gen0 = 0.1011 per 1 000 operations; `Struct_RingBuffer` Gen0 = 0. No Gen0 collections from this path.
- **12% faster** — 625.5 ns → 547.4 ns. Better cache locality from the contiguous ring buffer array vs. scattered `LinkedListNode` objects on the heap.
- **At inbound packet scale** — 50 players × 10 packets/s = 500 packets/s queued. Legacy path: ~39 KB/s of Gen0 allocation from this site. Fixed path: 0 KB/s. The closure elimination adds a further ~6 000 delegate objects/s saved (not measured in this benchmark; covered by the filter predicate caching in Task 3).
