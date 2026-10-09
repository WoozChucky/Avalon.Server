# Performance Benchmarks

Benchmarks live in `tools/Avalon.Benchmarking/` and are run with BenchmarkDotNet.

Whole-tick numbers (tick-thread allocations per tick, tick times and GC for fixed world scenarios) come from the
scenario runner instead, and the allocations are gated in CI: see [Scenario baseline](#scenario-baseline) at the end.

```bash
# Run all benchmarks
dotnet run -c Release --project tools/Avalon.Benchmarking

# Run a specific suite
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickLoop*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*EntityTracking*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*Serialization*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*SessionCipher*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketSerializationGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketReaderGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*WorldPacketQueueGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketReaderDecryptGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*GetContextPacketGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*CallListenerGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickThreadGuard*"
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

### Broadcast State GC-002 — removed

Both shapes it compared are gone. Entity replication no longer places bytes into a buffer by
hand, so there is no rented buffer, no per-entity copy and no slice whose lifetime has to
outlive it; the serializer owns the memory. The results below are kept as a record of what the
old path cost, not as something that can be re-run.

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

### Packet Reader Decrypt GC-008 — `PacketReaderDecryptGcBenchmarks.cs`

Before/after allocation comparison for the GC-008 fix: old two-step `Decrypt` (allocates a new
`byte[]` for the decrypted payload, swaps `packet.Payload`) + `Read` versus the new single
`Read(packet, decrypt)` call that rents an `ArrayPool<byte>` buffer, decrypts via spans, and
returns the buffer to the pool within the same call.

| Scenario | What it models |
|---|---|
| `Legacy_DecryptAndRead` | Old pattern — `packet.Payload = decryptFunc(packet.Payload)` (new `byte[]`) then `Read` — **baseline** |
| `Fixed_DecryptAndRead` | New pattern — `Read(packet, decrypt)` with rented buffer, no payload swap |

The passthrough `DecryptFunc` (`input.CopyTo(output); return input.Length`) isolates allocation
from actual cipher cost. At steady state the `ArrayPool` bucket for this payload size is pre-warmed —
zero net allocation per call for the buffer.

---

### Context Factory Delegate GC-010 — `GetContextPacketGcBenchmarks.cs`

Before/after CPU cost comparison for the GC-010 fix: `Activator.CreateInstance` + `PropertyInfo.SetValue × 2` on a warm cache versus a cached typed `Func<IConnection, Packet?, object>` delegate called directly. Applied to both `WorldServer` and `AuthServer`; benchmark covers the `WorldPacketContext<T>` path.

| Scenario | What it models |
|---|---|
| `Legacy_ActivatorAndSetValue` | Old `GetContextPacket` warm-cache path — `Activator.CreateInstance` + `SetValue × 2` on pre-reflected `PropertyInfo` fields — **baseline** |
| `Delegate_Cached` | New `GetContextPacket` — single `Func<IConnection, Packet?, object>` delegate invocation |

Note: `[MemoryDiagnoser]` will show **equal allocations** on both sides (~32 B). `WorldPacketContext<T>` is a struct that gets boxed to `object` in both paths — the win is CPU speed, not allocation count.

---

### CallListener DIM Dispatch GC-011 — `CallListenerGcBenchmarks.cs`

Before/after allocation comparison for the GC-011 fix: `MethodInfo.Invoke` with a per-call `new object[2]` args array and a boxed `CancellationToken`, versus a single cast to `IPacketHandlerNew` + virtual dispatch. Both paths call the same `CClientInfoHandler` to include handler-internal overhead equally.

| Scenario | What it models |
|---|---|
| `Legacy_ReflectionInvoke` | Old `CallListener` warm-cache path — `MethodInfo.Invoke(handler, new object[] { ctx, token })` — **baseline** |
| `Interface_Dispatch` | New `CallListener` — `((IPacketHandlerNew)handler).ExecuteAsync(ctx, token)` via DIM bridge |

The allocation difference is the eliminated `new object[2]` args array and boxed `CancellationToken` per dispatch.

---

### Serialization — `SerializationBenchmarks.cs`

Measures Protobuf-net packet serialization and deserialization, with and without the AES-GCM
session layer.

| Scenario | What it models |
|---|---|
| `Serialize_NoEncryption` | Serialize `CClientInfoPacket` — no encryption |
| `Serialize_Encrypted` | Serialize `CCharacterListPacket` through `AvalonCryptoSession.Encrypt` |
| `Deserialize_Encrypted` | Deserialize + decrypt + inner-deserialize an encrypted packet |
| `Deserialize_NoEncryption` | Deserialize an unencrypted `NetworkPacket` |

Both sides of the key agreement use P-256, the curve `CryptoManager` and `AvalonCryptoSession`
use in production, so the session key is a 256-bit AES key negotiated the way a live connection
negotiates it.

**Status:** Baseline recorded 2026-09-10. No active refactor in progress.

---

### Session Cipher — `SessionCipherBenchmarks.cs`

Compares the session cipher as the packet pipeline calls it — `AvalonCryptoSession.Encrypt` /
`Decrypt` — against a bare `System.Security.Cryptography.AesGcm` over the same key and the same
nonce + ciphertext + tag layout. Since #850 the session is itself the platform `AesGcm`, keyed once
per direction; before it, the session was BouncyCastle AES-GCM re-keyed per call, and its arms were
named `BouncyCastle_Encrypt` / `BouncyCastle_Decrypt`.

| Scenario | What it models |
|---|---|
| `Session_Encrypt` | Production `AvalonCryptoSession.Encrypt` — lock, counter nonce, one freshly allocated result sealed in place |
| `Session_Decrypt` | Production `AvalonCryptoSession.Decrypt` — lock, caller-supplied output buffer |
| `AesGcm_Encrypt` | Bare platform encrypt into an equivalently allocated result buffer (random nonce) |
| `AesGcm_Decrypt` | Bare platform decrypt into the same caller-supplied output buffer |

`PayloadSize` is parameterised at 64, 256 and 1024 bytes. The key is a real P-256 ECDH agreement
shared by both arms, so the two differ in call shape only — never in key material.

**Status:** Baseline recorded 2026-09-10; re-measured 2026-10-09 before and after #850.

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
- **Gen0 rate halved** — Gen0 drops from 0.0117 to 0.0076 (small) and 0.0381 to 0.0162
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

> These numbers were measured against an entity-replication path that no longer exists. Both
> the benchmark and the buffer plumbing it modelled were removed when entity state became a
> message instead of a hand-placed byte payload.

### Problem (before GC-002)

`BroadcastStateTo` allocated `new byte[bytesWritten]` per visible entity and
`new List<ObjectAdd>()` per player per call. With 20 entities × 60 players at
10 Hz that generated 1,200+ short-lived `byte[]` per broadcast tick.

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

---

## Packet Reader Decrypt GC-008 — Benchmark Results

**Date:** 2026-04-17

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`Connection.ExecuteAsync` called `_packetReader.Decrypt(packet, CryptoSession.Decrypt)` followed
by `_packetReader.Read(packet)`. `PacketReader.Decrypt` assigned `packet.Payload = decryptFunc(packet.Payload)`.
Inside `AvalonCryptoSession.Decrypt`, three `byte[]` objects were allocated per call:

1. `data.Take(12).ToArray()` — 12-byte nonce copy (LINQ)
2. `data.Skip(12).ToArray()` — full ciphertext copy (LINQ)
3. `_encryptCipher.DoFinal(ciphertext)` — decrypted output buffer

The Protobuf-allocated `packet.Payload` was then replaced by the DoFinal output.

### Fix (After)

- `IAvalonCryptoSession.Decrypt` → `int Decrypt(ReadOnlySpan<byte>, byte[])`: nonce and ciphertext extracted via span slices (no LINQ); decrypted output written directly into the caller-supplied `byte[]`
- `PacketReader.Decrypt` removed; `Read(packet, DecryptFunc?)` rents one `ArrayPool<byte>` buffer, decrypts into it, deserializes from it, returns the buffer — `packet.Payload` never swapped
- One 12-byte nonce `byte[]` per call remains (unavoidable — `ParametersWithIV` requires `byte[]`)
- One ciphertext `byte[]` per call remains (BouncyCastle 2.6.2 `IBufferedCipher` only provides `byte[]` overloads — no span overloads on `netstandard2.0` or `net6.0`)

### Results — GC-008 fix (2026-04-17)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3

| Method                | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| Legacy_DecryptAndRead | 124.5 ns | 2.12 ns | 1.98 ns |  1.00 |    0.02 | 0.0086 |     136 B |        1.00 |
| Fixed_DecryptAndRead  | 132.3 ns | 2.66 ns | 3.07 ns |  1.06 |    0.03 | 0.0050 |      80 B |        0.59 |
```

### Key observations

- **41% allocation reduction**: 136 B → 80 B per call. The eliminated allocation is the `byte[]` that used to be assigned to `packet.Payload` after decryption — it now stays in an `ArrayPool` rented buffer for the duration of deserialization, then is returned to the pool.
- **42% Gen0 reduction**: 0.0086 → 0.0050 GC pressure per call. At 500 encrypted packets/s (50 players × 10 per second) this eliminates ~150 000 Gen0 bytes/s of GC pressure from the decrypted payload swap alone.
- **Residual 80 B**: The deserialized `Packet` object — unavoidable until `NetworkPacket.Payload` is changed to `Memory<byte>` (Approach C, tracked separately).
- **Throughput unchanged**: Mean is 124.5 ns vs 132.3 ns — the small overhead (~6%) is the `ArrayPool.Rent/Return` cost plus `input.CopyTo(output)` in the passthrough; real crypto will dominate this.

---

## Context Factory Delegate GC-010 — Benchmark Results

**Date:** 2026-04-17

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`WorldServer.GetContextPacket` (and the identical `AuthServer` path) ran `Activator.CreateInstance` + `PropertyInfo.SetValue × 2` on **every packet dispatch**, even with a warm `(PropertyInfo, PropertyInfo)` cache:

```csharp
object context = Activator.CreateInstance(contextType)!;
packetProp.SetValue(context, packet);
connectionProp.SetValue(context, connection);
return context;
```

The property cache avoided re-calling `GetProperty`, but `Activator.CreateInstance` and two reflection `SetValue` calls still ran unconditionally per dispatch.

### Fix (After)

Replaced the `ConcurrentDictionary<Type, (PropertyInfo, PropertyInfo)>` cache with a `ConcurrentDictionary<Type, Func<IConnection, Packet?, object>>` cache. The delegate is built once per packet type via `BuildContextFactory<TPacket>()` + `MakeGenericMethod`. `GetContextPacket` becomes one dictionary lookup + one delegate call:

```csharp
var factory = _contextFactoryCache.GetOrAdd(packetType, static t =>
    (Func<IConnection, Packet?, object>)s_buildContextMethod.MakeGenericMethod(t).Invoke(null, null)!);
return factory(connection, packet as Packet);
```

The `static` lambda captures no variables — zero closure allocation per call.

### Results

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|---|---|---|
| `Legacy_ActivatorAndSetValue` | 19.896 ns | 0.2428 ns | 0.2027 ns | 1.00 | 0.0020 | 32 B | 1.00 |
| `Delegate_Cached` | 5.706 ns | 0.0939 ns | 0.0784 ns | 0.29 | 0.0020 | 32 B | 1.00 |

### Key observations

- **3.49× faster** — 19.896 ns → 5.706 ns (Ratio = 0.29). `Activator.CreateInstance` + `SetValue × 2` have non-trivial reflection overhead even on a warm cache; a direct delegate invocation eliminates all of it.
- **Equal allocations** — both paths allocate exactly 32 B. `WorldPacketContext<T>` is a struct; boxing it to `object` is unavoidable in both paths. This is a **CPU benchmark**, not an allocation benchmark — the spec predicted this outcome and `[MemoryDiagnoser]` confirms it.
- **Gen0 rate unchanged** — `Gen0 = 0.0020` on both paths. The GC pressure is the single boxed struct, identical in both cases.
- **At dispatch scale** — at 50 players × 10 packets/s = 500 `GetContextPacket` calls/s, the legacy path burns ~9.9 µs/s in pure reflection overhead. The delegate path reduces that to ~2.9 µs/s. The absolute saving is modest per-call, but the fix eliminates a reflection barrier that blocked JIT inlining of the construction path entirely.
- **Applied to both servers** — `WorldServer` and `AuthServer` share the identical pattern. Both are fixed; the benchmark covers the `WorldPacketContext<T>` path as representative.

---

## CallListener DIM Dispatch GC-011 — Benchmark Results

**Date:** 2026-04-17

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`ServerBase.CallListener` dispatched every inbound packet via `MethodInfo.Invoke`:

```csharp
await ((Task)handlerCache.ExecuteMethod.Invoke(
    packetHandler,
    new[] { context, _stoppingToken.Token }  // new object[2] per call + CancellationToken boxed
)!).ConfigureAwait(false);
```

Two heap allocations per dispatch:
1. `new object[2]` — the reflection invoke args array
2. Boxing of `_stoppingToken.Token` (`CancellationToken` is a struct → `object`)

### Fix (After)

Added `Task ExecuteAsync(object context, CancellationToken token)` to `IPacketHandlerNew`. Both `IAuthPacketHandler<T>` and `IWorldPacketHandler<T>` provide a DIM that unboxes the context and delegates to the strongly-typed overload. `CallListener` becomes one cast + one virtual dispatch:

```csharp
await ((IPacketHandlerNew)packetHandler).ExecuteAsync(context, _stoppingToken.Token).ConfigureAwait(false);
```

`ExecuteMethod: MethodInfo` removed from `PacketHandlerCache`; `using System.Reflection` removed from `ServerBase.cs`.

### Results

| Method | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|---|---|---|---|
| `Legacy_ReflectionInvoke` | 39.90 ns | 0.583 ns | 0.572 ns | 1.00 | 0.02 | 0.0081 | 128 B | 1.00 |
| `Interface_Dispatch` | 24.50 ns | 0.385 ns | 0.360 ns | 0.61 | 0.01 | 0.0041 | 64 B | 0.50 |

### Key observations

- **39% faster** — 39.90 ns → 24.50 ns (Ratio = 0.61). `MethodInfo.Invoke` carries measurable overhead even on a pre-reflected, warm cache; a single virtual dispatch via an interface is significantly cheaper for the JIT to schedule.
- **50% less allocation** — 128 B → 64 B per dispatch. The 64 B eliminated is the `new object[2]` args array and the boxed `CancellationToken` that `MethodInfo.Invoke` required on every call.
- **Gen0 rate halved** — Gen0 drops from 0.0081 to 0.0041 per 1 000 operations. Fewer Gen0 collections means less STW pause time during packet processing.
- **At dispatch scale** — at 50 players × 10 packets/s = 500 `CallListener` dispatches/s, the legacy path allocates ~64 KB/s of short-lived Gen0 objects from this site alone. The DIM path reduces that to ~32 KB/s, a saving of ~32 KB/s.
- **Dead code removed** — `IPacketHandler`/`IPacketRegistry`/`PacketRegistry`/`AvalonTcpClient` (all unreferenced in production) deleted alongside the fix, reducing build surface and eliminating dead maintenance burden.

---

## Serialization — Benchmark Results

**Date:** 2026-09-10

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
```

### Results — first baseline (2026-09-10)

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|
| `Serialize_NoEncryption` | 205.5 ns | 1.28 ns | 1.07 ns | 0.0556 | 872 B |
| `Serialize_Encrypted` | 951.8 ns | 17.08 ns | 15.98 ns | 0.1364 | 2,144 B |
| `Deserialize_Encrypted` | 527.0 ns | 10.06 ns | 12.36 ns | 0.1106 | 1,744 B |
| `Deserialize_NoEncryption` | 182.0 ns | 3.03 ns | 2.83 ns | 0.0131 | 208 B |

### Key observations

- **Encryption costs 4.6× on the send path** — 205.5 ns → 951.8 ns, and 872 B → 2,144 B. These
  are end-to-end packet figures, not cipher figures: `CCharacterListPacket.Create` serializes the
  inner packet to a `MemoryStream`, encrypts that, then the outer `NetworkPacket` is serialized
  again. The cipher's own share is isolated in the Session Cipher suite below.
- **Encrypt costs more than decrypt** — 951.8 ns versus 527.0 ns, even though the decrypt scenario
  does strictly more work (outer deserialize, decrypt, inner deserialize). The asymmetry is
  allocation: `AvalonCryptoSession.Decrypt` writes into a caller-supplied buffer, while `Encrypt`
  still copies the input span, takes a fresh `DoFinal` output buffer, and then joins nonce and
  ciphertext through `Concat().ToArray()`. The send path never received the treatment the receive
  path got.
- **The unencrypted rows are the protobuf floor** — 205.5 ns / 872 B to serialize and 182.0 ns /
  208 B to deserialize a small packet. Anything above that on the encrypted rows is the session
  layer, not Protobuf-net.

---

## Session Cipher — Benchmark Results

**Date:** 2026-09-10

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
```

### Results — first baseline (2026-09-10)

| Method | PayloadSize | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---|---|---|---|---|---|---|
| `BouncyCastle_Encrypt` | 64 | 776.4 ns | 5.14 ns | 4.56 ns | 0.1125 | - | 1,768 B |
| `BouncyCastle_Decrypt` | 64 | 376.2 ns | 7.14 ns | 8.22 ns | 0.0968 | - | 1,520 B |
| `AesGcm_Encrypt` | 64 | 258.9 ns | 2.89 ns | 2.71 ns | 0.0076 | - | 120 B |
| `AesGcm_Decrypt` | 64 | 195.8 ns | 1.63 ns | 1.27 ns | - | - | - |
| `BouncyCastle_Encrypt` | 256 | 901.1 ns | 17.33 ns | 17.02 ns | 0.1488 | - | 2,344 B |
| `BouncyCastle_Decrypt` | 256 | 456.9 ns | 2.59 ns | 2.03 ns | 0.1087 | - | 1,712 B |
| `AesGcm_Encrypt` | 256 | 277.7 ns | 3.78 ns | 3.35 ns | 0.0196 | - | 312 B |
| `AesGcm_Decrypt` | 256 | 220.9 ns | 2.83 ns | 2.65 ns | - | - | - |
| `BouncyCastle_Encrypt` | 1024 | 1,397.5 ns | 24.96 ns | 24.52 ns | 0.2956 | - | 4,648 B |
| `BouncyCastle_Decrypt` | 1024 | 819.2 ns | 16.30 ns | 20.01 ns | 0.1574 | 0.0010 | 2,480 B |
| `AesGcm_Encrypt` | 1024 | 385.7 ns | 7.19 ns | 6.37 ns | 0.0687 | - | 1,080 B |
| `AesGcm_Decrypt` | 1024 | 283.8 ns | 1.16 ns | 1.08 ns | - | - | - |

### Key observations

- **The platform primitive is 3.0–3.6× faster on encrypt and 1.9–2.9× on decrypt**, at every size
  measured, on identical key material.
- **Allocation is the wider gap.** BouncyCastle allocates 1.7–4.6 KB per encrypt and 1.5–2.5 KB per
  decrypt. The platform arm allocates only the result buffer on encrypt (120–1,080 B) and nothing
  at all on decrypt, because it writes straight into the caller's span.
- **Two independent effects, separable across the three sizes.** A straight-line fit of the encrypt
  means against payload size puts the fixed per-call cost at ~735 ns for BouncyCastle versus
  ~250 ns for the platform, and the marginal cost at ~0.65 ns/byte versus ~0.13 ns/byte. Decrypt
  shows the same shape (~347 ns versus ~190 ns fixed, ~0.46 versus ~0.09 ns/byte). So roughly
  480 ns per call is call shape and the remaining ~5× is cipher throughput.
- **The fixed component is the per-call re-key.** `AvalonCryptoSession` holds one shared
  `IBufferedCipher` and calls `Init(...)` on it for every packet, in both directions, under a
  single lock. Re-keying GCM regenerates the GHASH multiplication tables, which is also where most
  of the per-call allocation goes.
- **At broadcast scale** — 50 connections × 60 Hz state at 256 B is 3,000 encrypts/s. That
  is ~2.7 ms/s of CPU and ~6.7 MB/s of Gen0 on the current path, against ~0.8 ms/s and ~0.9 MB/s
  on the platform primitive: a saving of ~1.9 ms/s and ~5.8 MB/s from this site alone.
- **This measures the current call shape, not BouncyCastle at its best.** A BouncyCastle arm that
  keyed its cipher once and reused it would close the fixed-cost half of the gap. It would not
  close the per-byte half, which is a property of the implementation rather than of how it is
  called.

The table above is the record from before #850, when the session arm was BouncyCastle (named
`BouncyCastle_*`). The runs below are the production figures since.

### Results — #850, before and after (2026-10-09)

Same machine and toolchain as above (Windows 11 build 26200.9457, i9-12900K, .NET SDK 10.0.401,
.NET 10.0.12, BenchmarkDotNet 0.15.8, DefaultJob, Release). Both runs on this date, one on `main` at
`c18f8cd7` and one with #850 applied; the bare `AesGcm_*` arms are unchanged between them and agree
within run-to-run noise, which is the control.

Before — the session on BouncyCastle, re-keyed per call (`main`, `c18f8cd7`):

| Method | PayloadSize | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---|---|---|---|---|---|---|
| `BouncyCastle_Encrypt` | 64 | 447.4 ns | 8.97 ns | 24.09 ns | 0.1040 | - | 1,632 B |
| `BouncyCastle_Decrypt` | 64 | 494.3 ns | 12.97 ns | 38.25 ns | 0.0916 | - | 1,440 B |
| `AesGcm_Encrypt` | 64 | 297.4 ns | 5.78 ns | 8.29 ns | 0.0076 | - | 120 B |
| `AesGcm_Decrypt` | 64 | 219.3 ns | 4.31 ns | 4.03 ns | - | - | - |
| `BouncyCastle_Encrypt` | 256 | 614.4 ns | 12.23 ns | 35.49 ns | 0.1402 | - | 2,208 B |
| `BouncyCastle_Decrypt` | 256 | 622.0 ns | 16.18 ns | 47.71 ns | 0.1040 | - | 1,632 B |
| `AesGcm_Encrypt` | 256 | 331.9 ns | 6.62 ns | 11.94 ns | 0.0196 | - | 312 B |
| `AesGcm_Decrypt` | 256 | 250.1 ns | 3.84 ns | 3.60 ns | - | - | - |
| `BouncyCastle_Encrypt` | 1024 | 1,247.5 ns | 29.90 ns | 88.15 ns | 0.2861 | 0.0019 | 4,512 B |
| `BouncyCastle_Decrypt` | 1024 | 1,115.2 ns | 22.17 ns | 37.04 ns | 0.1526 | 0.0010 | 2,400 B |
| `AesGcm_Encrypt` | 1024 | 489.8 ns | 9.73 ns | 17.29 ns | 0.0687 | - | 1,080 B |
| `AesGcm_Decrypt` | 1024 | 336.2 ns | 6.59 ns | 10.83 ns | - | - | - |

After — the session on the platform `AesGcm`, keyed once per direction (#850):

| Method | PayloadSize | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|---|
| `Session_Encrypt` | 64 | 257.1 ns | 2.53 ns | 2.25 ns | 0.0076 | 120 B |
| `Session_Decrypt` | 64 | 232.5 ns | 4.53 ns | 5.22 ns | - | - |
| `AesGcm_Encrypt` | 64 | 295.8 ns | 5.88 ns | 11.88 ns | 0.0076 | 120 B |
| `AesGcm_Decrypt` | 64 | 216.5 ns | 2.62 ns | 2.45 ns | - | - |
| `Session_Encrypt` | 256 | 292.9 ns | 5.58 ns | 6.20 ns | 0.0196 | 312 B |
| `Session_Decrypt` | 256 | 257.9 ns | 5.14 ns | 8.00 ns | - | - |
| `AesGcm_Encrypt` | 256 | 323.3 ns | 5.93 ns | 5.26 ns | 0.0196 | 312 B |
| `AesGcm_Decrypt` | 256 | 236.8 ns | 4.50 ns | 3.99 ns | - | - |
| `Session_Encrypt` | 1024 | 423.9 ns | 8.44 ns | 11.83 ns | 0.0687 | 1,080 B |
| `Session_Decrypt` | 1024 | 337.8 ns | 6.76 ns | 13.51 ns | - | - |
| `AesGcm_Encrypt` | 1024 | 450.8 ns | 8.50 ns | 15.33 ns | 0.0687 | 1,080 B |
| `AesGcm_Decrypt` | 1024 | 321.4 ns | 6.42 ns | 11.25 ns | - | - |

- **Sealing allocates the sealed packet and nothing else; opening allocates nothing.** 120, 312 and
  1,080 B are exactly a `byte[]` of nonce + payload + tag at 64, 256 and 1,024 bytes, down from
  1.6–4.5 KB; decrypt goes from 1.4–2.4 KB to zero. `SessionKeyDerivationShould` pins both.
- **1.7–2.9× faster to seal and 2.1–3.3× faster to open**, the gain growing with the payload
  (at 256 B: 614 → 293 ns and 622 → 258 ns). The session now runs within ~20 ns of the bare
  primitive on decrypt (the lock and the length checks). It seals slightly faster than the bare
  arm, because the bare arm draws a random nonce while the session copies its counter.
- **The BouncyCastle "before" ran faster than on 2026-09-10** (447 against 776 ns at 64 B) and
  with a much wider spread (a bimodal decrypt): treat it as one noisy run, not a trend. The
  allocation columns are deterministic and are the solid comparison.
- **At broadcast scale** — 50 connections × 60 Hz × 256 B, 3,000 encrypts/s — sealing now costs
  ~0.9 ms/s of CPU and ~0.9 MB/s of Gen0, against ~1.8 ms/s and ~6.6 MB/s in the before run.


---

### Tick-thread guard — `TickThreadGuardBenchmarks.cs`

The cost of `TickThreadGuard.AssertOnTick` (#639), compiled into every build and checked only while
`Game:TickThreadGuard` is on. The switch can only be turned on in a process, so the two states are
two classes: `TickThreadGuardOffBenchmarks` refuses to run if the guard is already on, and
`TickThreadGuardOnBenchmarks` turns it on and binds its own thread in its global setup. Both rely on
BenchmarkDotNet's default out-of-process toolchain, which runs every benchmark in a process of its
own; no reset hook was added to production code.

| Scenario | What it models |
|---|---|
| `Baseline_EmptyCall` | An empty method of the guard's shape, called the way the guard is (`x?.Method("...")`) |
| `Guard_Off` / `Guard_OnBound` | The guard itself: off (production default), and on, bound, called on the bound thread |
| `IgnoreList_AddRemove_*` | One ignore and one unignore, a real guarded write: no guard handed over, guard off, guard on |
| `PartyService_Leave_*` | `PartyService.Leave` for a character in no party, the cheapest guarded mutator |

### Results — first baseline (2026-10-02)

Windows 11, 12th Gen Intel Core i9-12900K, .NET 10.0.12, BenchmarkDotNet 0.15.8, DefaultJob, Release.

| Method | Mean | StdDev | Allocated |
|---|---:|---:|---:|
| `Baseline_EmptyCall` (off run) | 0.000 ns | 0.000 ns | - |
| `Guard_Off` | 1.008 ns | 0.065 ns | - |
| `IgnoreList_AddRemove_NoGuard` | 32.85 ns | 0.71 ns | 64 B |
| `IgnoreList_AddRemove_GuardOff` | 26.13 ns | 1.24 ns | 128 B |
| `PartyService_Leave_NoGuard` | 0.550 ns | 0.023 ns | - |
| `PartyService_Leave_GuardOff` | 0.563 ns | 0.025 ns | - |
| `Baseline_EmptyCall` (on run) | 0.016 ns | 0.022 ns | - |
| `Guard_OnBound` | 0.726 ns | 0.005 ns | - |
| `IgnoreList_AddRemove_GuardOn` | 43.63 ns | 5.11 ns | 128 B |
| `PartyService_Leave_GuardOn` | 0.475 ns | 0.064 ns | - |

### Key observations

- **The guard costs about a nanosecond a call, on or off.** Off it is one static volatile read;
  on and bound it adds a volatile read of the bound thread and a compare with the current thread
  id. The empty baseline is inlined away to nothing, so the whole ~1 ns is the guard.
- **In a real write it is lost in the noise.** `PartyService.Leave` measures the same with and
  without it (all three under a nanosecond, below what the harness resolves reliably).
  `IgnoreList` add-and-remove varies more between runs (26-44 ns) than the guard costs.
- **The 64 B against 128 B on `IgnoreList` is the JIT, not the guard.** With no guard handed over,
  .NET 10's escape analysis keeps one of the two short-lived objects (most likely the removal's
  closure) on the stack; the extra guard code changes the inlining and it moves to the heap. With
  `DOTNET_JitObjectStackAllocation=0` both variants allocate 128 B and run in the same time
  (short job: 26.4 ns against 27.5 ns). The guard itself never allocates.
- **The tick runs a few guarded calls every tick** (the party tick, the member-status flush, the
  publish and the expiry pass) and more only on rare requests (transfers, spawns, party requests,
  ignores), so at 60 Hz the guard costs well under a microsecond a second.

---

## Scenario baseline

The BenchmarkDotNet suites above measure one call site at a time. The scenario baseline measures whole ticks: a world
server built in process over the real `InstanceRegistry`, instances, input handler, replication and send path, with
scenario players connected, ticked the way the server ticks. It answers two questions a micro-benchmark cannot: how
much the tick thread allocates per tick under a given load, and how long the tick takes. The allocation figure is
gated in CI; the timings and GC figures are reported and never gated.

The scenarios live in `tests/Avalon.World.Testing/Scenarios`, the runner in `tools/Avalon.Scenarios`
([tooling](tooling.md#the-scenario-runner)), and the gate in
`tests/Avalon.Server.World.UnitTests/Performance/ScenarioAllocationsShould.cs`.

### What each scenario models

| Scenario | Instances | Players | What runs every tick |
|---|---:|---:|---|
| `town-idle` | 1 town | 30 | Players standing on a grid of points checked against the town's navmesh. Nothing moves, so this is the floor: what a tick costs when there is nothing to do. |
| `town-walk` | 1 town | 30 | Players walking an 8 m circle centred at (45, 10), the south-east chunk's open square. Each player's input goes through the real `PlayerInputHandler` (one input packet per player, reused every tick), and each player is sent the real `SPlayerStateAckPacket` through the real cipher every tick. Ten engaged "Bench Wolf" creatures (seed 425) share the instance. |
| `many-instances` | 250 normal maps | 500 | Two walking players in each of 250 normal-map instances, every instance created through the real `InstanceRegistry.GetOrCreateNormalInstanceAsync`. It measures many small instances rather than one crowd. |

No scenario was reduced in size: `many-instances` takes about 7 s for its warm-up and windows.

The walking circle is not centred on the entry spawn (15, 15), where the crowd-budget tests stand their players: an
8 m circle there crosses a building and a wall, and a walker pressed into one stops for good. A half-metre scan of the
town found two adjacent centres whose circle stays a metre clear of every wall, (45, 10) and (45.5, 10).

### The send path is real

Every scenario player has a real `AvalonCryptoSession` (the platform `AesGcm` since #850) and a real
`TickDrivenOutbox`, which writes to a stream that only counts bytes. So the per-packet encryption cost, and its
allocations, are in the numbers; the [session cipher results](#session-cipher--benchmark-results) above show what it
costs per call. Three things differ from production, all on the cost side only:

- The counting stream stands in for production's `SslStream`, so the TLS record layer is **not** in the numbers. The
  counting stream also completes every write at once, so the outbox's flush finishes synchronously on the tick thread;
  over `SslStream` a write usually does not, and production may allocate an async state-machine box per flush there.
- Scenario connections share key material. The point is the cost of sealing, not the secrecy of the result.
- The `DiagnosticsConfig` counters (bytes, packets sent and dropped) are skipped, so their cost is **excluded** from the
  numbers. They allocate nothing while no listener is attached, but production attaches an OpenTelemetry listener.

Not modelled at all: the session pass (the first of the tick's two passes), the readiness barrier, the flushers,
pings, persistence and saves, and combat. Combat is the next scenario to add (`dungeon-combat`). Of `World.Update`
itself, the scenario tick runs the registry's publication of finished builds (`InstanceRegistry.PublishFinished`) and
the instance pass, and leaves out:

- `Time.Update`, the content and script hot reloads, and the parties tick.
- `InstanceRegistry.ProcessExpiredInstances`, the last step. Its walk of the registry allocates a 72 B enumerator per
  tick in an unoptimized (Debug) build and, measured, nothing in Release, so it would add nothing to the committed
  figure and would fail `town-idle`'s gate (4,320 B per window over a 9,600 B baseline) in every local Debug run.
- The map pass's dispatch wrapper: `WorldConnection.ProcessQueue` and `PacketDispatchTelemetry.Begin`. `LoopWalker`
  calls `PlayerInputHandler` directly, so the queue, the per-packet session filter and the dispatch telemetry are not
  in the numbers.

### How a scenario is measured

- **Window.** 60 ticks, one second of game time at 60 Hz. Allocations are read with
  `GC.GetAllocatedBytesForCurrentThread` on the tick thread, so work on other threads does not count.
- **Warm-up.** The scenario first ticks for a wall-clock warm-up (5 s in the gate; `--warmup-seconds`, default 10, in
  the runner), so the JIT's tiering, first-use caches and pools have settled.
- **Gated figure.** `bytesPerWindow` is the **minimum** of five consecutive windows after the warm-up. The minimum
  ignores a window that a one-off allocation (a pool growing, a late tier-up) happened to land in.
- **Checked.** After the windows, outside the measured region, each scenario checks that it still did its work
  (`IScenario.Verify`): every player still in its instance; in the walking scenarios every player sent packets and
  its input advanced by one per tick (300) with a change of position; in `town-walk` every wolf still in combat within
  4 m of the player it fights. A scenario that stopped walking or fighting would allocate less and pass the gate as an
  improvement, so a failed check throws instead, failing the gate and stopping the runner.
- **Two levels.** `town-walk` and `many-instances` alternate between two window levels from one window to the next;
  the minimum lands on the lower one, and that lower level is the committed figure.
- **Timing.** After the windows the runner times `--measure-ticks` ticks (default 3600, one minute at 60 Hz) and
  reports the tick-time distribution, the share of ticks over the 16.7 ms budget, and the collections and GC pause
  time of that phase. The gate skips this phase.

### Running it and reading the table

```bash
# Every scenario, keeping the run as this machine's baseline (perf/local/ is ignored by git)
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-baseline perf/local/$(hostname).json

# A later run against it: current, baseline and change per figure; never fails
dotnet run -c Release --project tools/Avalon.Scenarios -- --baseline perf/local/$(hostname).json

# One scenario, a shorter warm-up
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario town-walk --warmup-seconds 5
```

Options: `--scenario <name>|all` (default `all`), `--warmup-seconds` (default 10), `--measure-ticks` (default 3600;
0 skips timing), `--json <file>`, `--baseline <file>`, `--write-baseline <file>`, `--write-allocations <file>`. The
runner uses Server GC with concurrent collection, the world server's own settings, and warns when built in Debug.

The table has one row per scenario: `bytes/window` (the gated figure), `B/player/tick` (that figure over 60 ticks and
the player count), tick ms mean, p95, p99 and max, `% > 16.7 ms`, gen0, gen1 and gen2 collections, and GC pause in ms
and as a share of the timed phase. Only `bytes/window` is gated. Everything else depends on the machine and its load,
and compares only against a baseline taken on the same machine.

### The allocation gate

`perf/scenario-allocations.json` is the committed baseline: per scenario its `bytesPerWindow` (and, for reading,
`bytesPerPlayerPerTick`), with the commit and date it was generated at. `perf/local/` holds per-machine runner
baselines and is never committed. `ScenarioAllocationsShould` runs every scenario and compares its `bytesPerWindow`
with the committed figure:

- **Fail** when the current figure is more than 5% **and** more than 256 B over the committed one. The 256 B floor
  keeps a scenario that allocates almost nothing (`town-idle`) from failing on one stray object.
- **Improvement notice** (test output, not a failure) when it is more than 5% and more than 256 B under.
- **Detection floor.** The 5% band is relative, so in the moving scenarios it is wide in absolute terms. With the
  committed figures below, the gate fails only on a rise of about **8 B per tick** in `town-idle` (the 256 B floor is
  below 5% of its 9,600 B, so 5% decides), **166 B per player per tick** in `town-walk` (298 KB per window), and
  **104 B per player per tick** in `many-instances` (about 207 B per instance per tick, 3.1 MB per window). A smaller
  regression, such as one new 64 B object per walking player per tick, passes. The floors shrink as the per-packet
  cipher cost falls (#850) and the ratchet lowers the baseline; a follow-up may tighten the tolerance once CI's Linux
  figures are known.
- **Ratchet.** The committed file goes down only by an explicit commit. When a change makes a scenario cheaper,
  regenerate and commit the lower figure, so the gain cannot be lost again unnoticed. When an increase is intended,
  regenerating is also the fix, and the JSON diff shows the reviewer what it costs.

Regenerate (every scenario, in Release; the runner refuses `--write-allocations` for a subset):

```bash
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-allocations perf/scenario-allocations.json
```

The baseline is generated in Release and CI builds Release, so CI's run of the gate is the one that decides. A local
`dotnet test` builds Debug and reads up to about 3% higher (`many-instances` +3.08%, `town-walk` +1.91%), so locally
the gate has less headroom: under 2% on `many-instances`. The gate runs in a non-parallel xUnit collection, so no other
test in the World assembly competes for the CPU during the wall-clock warm-up; other test assemblies still run in
parallel processes under a solution-wide `dotnet test`, and the minimum-of-windows rule absorbs that. The gate adds
about 18 s to the World suite.

### Results — first baseline (2026-10-08)

Allocations, from `perf/scenario-allocations.json` (commit `9dec695b`, Release; regenerated when the scenario tick
gained the registry's `PublishFinished`, which moved no figure beyond run-to-run noise). "Fails from" is the first
figure the gate fails on; "Notice at" the highest figure that prints the improvement notice.

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at |
|---|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 9,600 | 160 | 5.33 | 10,081 | 9,119 |
| `town-walk` | 30 | 5,964,720 | 99,412 | 3,313.73 | 6,262,957 | 5,666,483 |
| `many-instances` | 500 | 62,226,368 | 1,037,106 | 2,074.21 | 65,337,687 | 59,115,049 |

Timing and GC, from the runner's first Release run (2026-10-08, commit `799fc830`; 12th Gen Intel Core i9-12900K,
16 cores / 24 logical processors, 64 GB, Windows 11 Pro, .NET 10.0.12, Server GC, latency mode Interactive; 10 s
warm-up, 3600 timed ticks). Its allocation figures were within 0.01% of the committed ones.

| Scenario | Tick ms mean | p95 | p99 | max | % > 16.7 ms | gen0 | gen1 | gen2 | GC pause ms | GC pause % |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 0.037 | 0.065 | 0.082 | 0.273 | 0.00 | 0 | 0 | 0 | 0.00 | 0.00 |
| `town-walk` | 0.220 | 0.702 | 1.052 | 4.385 | 0.00 | 116 | 0 | 0 | 24.57 | 3.09 |
| `many-instances` | 2.564 | 4.399 | 5.766 | 7.713 | 0.00 | 225 | 2 | 2 | 150.57 | 1.63 |

### Key observations

- **An idle tick still allocates 160 B, every tick.** `town-idle` has nothing to do, yet each tick copies
  `InstanceRegistry.ActiveInstances` and `InstanceTicker` walks the copy through a boxed enumerator (issue #851).
  It is small, but it is a fixed cost of every tick, and the floor the other scenarios stand on.
- **Walking costs kilobytes per player per tick, and most of it is encryption.** About 3.3 KB per player per tick in
  `town-walk` and 2.1 KB in `many-instances`, dominated by the per-packet BouncyCastle encrypt (1.7–4.6 KB per call in
  the session cipher results above) of the state acknowledgement each walker is sent every tick. Issue #850 is the
  fix; this baseline is how its gain will be measured and then locked in.
- **The allocation rate shows up as GC.** `town-walk` allocates about 6 MB per 60-tick window on the tick thread and
  ran 116 gen0 collections in its timed minute, 3.1% of the time in GC pauses; `many-instances` allocates about 62 MB
  per window, with 225 gen0, 2 gen1 and 2 gen2 collections. No tick went over the 16.7 ms budget, but the worst ticks
  (4.4 ms and 7.7 ms) sit well above the p99, as a few ticks absorbing a GC pause would.
- **Tick time follows the walking players, not the instance count.** `many-instances` holds about 17 times the
  players of `town-walk` in 250 times the instances, and its mean tick is about 12 times longer: a little less per
  player (`town-walk` also runs ten creatures), so at this scale the instance count adds no cost that stands out. Its p95 of 4.4 ms is about a quarter of the tick
  budget for 500 walking players, before saves, combat or the session pass.
