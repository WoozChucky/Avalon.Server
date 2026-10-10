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

# Not BenchmarkDotNet: the outbox flush over real loopback sockets, plain and TLS (#875)
dotnet run -c Release --project tools/Avalon.Benchmarking -- outbox-flush 200 1 1800
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
| `Session_MethodGroupPerPacket` | A real `AvalonCryptoSession` opening a sealed packet, passed as the method group `_server.Decrypt` on every call: `Connection`'s read loop before #854 |
| `Session_CachedDelegate` | The same, through a `DecryptFunc` created once: `Connection`'s read loop since #854 |

In the first two arms the passthrough `DecryptFunc` (`input.CopyTo(output); return input.Length`) isolates allocation
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
| `Serialize_Encrypted` | Serialize `CCharacterListPacket` through the session's `Encryptor`, as every send does (the `Encrypt` method group before #854) |
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

### Results — the read loop's decrypt delegate (#854, 2026-10-09)

i9-12900K, Windows 11, .NET 10.0.12, DefaultJob, Release. The two `Session_*` arms are new with #854 and call `Read`
through `IPacketReader`, as `Connection` does; the first two arms, run on `main` (`a892abe7`) and with #854, are
unchanged and are the control.

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---:|---:|---:|---:|---:|
| `Legacy_DecryptAndRead` (main) | 157.8 ns | 5.09 ns | 15.00 ns | 0.0086 | 136 B |
| `Fixed_DecryptAndRead` (main) | 162.1 ns | 5.17 ns | 15.23 ns | 0.0050 | 80 B |
| `Legacy_DecryptAndRead` (#854) | 154.1 ns | 2.99 ns | 7.55 ns | 0.0086 | 136 B |
| `Fixed_DecryptAndRead` (#854) | 153.2 ns | 3.09 ns | 7.39 ns | 0.0050 | 80 B |
| `Session_MethodGroupPerPacket` | 412.8 ns | 8.08 ns | 12.58 ns | 0.0091 | 144 B |
| `Session_CachedDelegate` | 411.7 ns | 8.19 ns | 21.15 ns | 0.0048 | 80 B |

- **The method group cost 64 B per packet, in fully optimised code.** Passed through the interface, the delegate
  escapes and is on the heap: 144 B against 80 B, the 80 B being the deserialized `Packet` and nothing else.
- **No time difference**: creating a small delegate is a few nanoseconds against ~410 ns of open and deserialize.
- **Through the concrete `PacketReader` the JIT hides it.** A first run of the `Session_*` arms called `Read` on the
  concrete type, which .NET 10 inlines; the delegate stayed on the stack and both arms allocated 80 B. That is the
  dependency #854 removes: whether the per-packet delegate cost anything depended on the JIT, not on the code.

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

### Results — #854, before and after (2026-10-09)

Same machine (Windows 11 build 26200.9457, i9-12900K, .NET SDK 10.0.401, .NET 10.0.12, DefaultJob, Release). Before:
`main` at `a892abe7`, `Serialize_Encrypted` passing `_client.Encrypt` as a method group. After: #854, passing
`_client.Encryptor`.

| Method | Before mean | Before allocated | After mean | After allocated |
|---|---:|---:|---:|---:|
| `Serialize_NoEncryption` | 269.2 ns | 872 B | 265.8 ns | 872 B |
| `Serialize_Encrypted` | 441.8 ns | 648 B | 457.0 ns | 584 B |
| `Deserialize_Encrypted` | 324.9 ns | 288 B | 379.7 ns | 288 B |
| `Deserialize_NoEncryption` | 224.5 ns | 208 B | 233.8 ns | 208 B |

- **The send loses its 64 B delegate** (648 → 584 B), even here, in fully optimised code: the method group escapes
  into `Create`, which is not inlined, so it is on the heap. The means move within the runs' spread (StdDev
  10-37 ns); `Deserialize_Encrypted` uses no delegate at all, so its 55 ns is run-to-run noise.

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

## Outbox flush — the send path over real sockets (#875)

`OutboxFlush/OutboxFlushHarness.cs`, run as `outbox-flush [connections] [packets] [ticks] [modes]` (defaults 200, 1,
1800 and every mode). Not a BenchmarkDotNet suite: like `crowd-budget`, it is a steady state timed tick by tick.

The first capacity ramp (#875) held about 150 players. At 200, about two thirds of a median tick (some 4.2 ms of
6.2 ms) was spent after the world update, where no histogram looked; `world.post_update.duration` now times each stage
of it ([instrumentation](instrumentation.md#tick-and-instance-time)). The [scenario baseline](#scenario-baseline)
cannot show that cost: its outbox writes to a stream that only counts bytes. This harness puts the world's real
`TickDrivenOutbox` (capacity 100) in front of each kind of stream and times what `WorldServer.Update`'s outbox stage
does: flush every connection's outbox, one after the other, on one thread.

For each mode it opens the connections over loopback (the server end is what the world holds; the client end is read
and discarded on the thread pool, as a peer would), then ticks at 60 Hz: each tick queues `packets` movement
acknowledgements (a frame of about 70 B, the size of the world's acks and state updates) on every connection and
times the loop that flushes every outbox. It reports the flush time per tick, per connection, the tick thread's
allocations per tick and the collections over the measured ticks, after 300 warm-up ticks.

| Mode | Stream behind the outbox |
|---|---|
| `memory` | The scenario runner's counting stream: no socket, no TLS |
| `tcp` | A plain `NetworkStream` |
| `tls` | An `SslStream` over the socket (TLS 1.2 or 1.3, a self-signed P-256 certificate), as the world serves |
| `tls-pool` | Prototype: the same, with each write started on the thread pool; the tick frames the packets and queues the write |
| `tls-parallel` | Prototype: the same streams, the flush split among four thread-pool workers that the tick waits for |
| `receive` | The other direction, off the tick: each client sends one frame per tick, and the server end reads it through `PacketStream.EnumerateRawFramesAsync` and `InboundPacketFrame.ParseFrame`; reports the bytes allocated per frame read |

The two prototypes measure what the tick would pay if the write left it; they allocate (a `Task.Run` per write, a
`Parallel.For` per tick), and the server does neither.

### Results — 200 connections, one packet each per tick (2026-10-09)

i9-12900K. Linux: a container limited to 4 CPUs (Docker on WSL2, Ubuntu 24.04, .NET 10.0.5, workstation GC). Windows
11, .NET 10.0.12, workstation GC. 1800 measured ticks; flush time per tick in ms, and per connection in µs.

| Mode | Linux mean | p50 | p99 | µs/conn | Windows mean | p50 | p99 | µs/conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `memory` | 0.067 | 0.063 | 0.159 | 0.33 | 0.063 | 0.053 | 0.186 | 0.31 |
| `tcp` | 0.827 | 0.846 | 1.178 | 4.14 | 2.666 | 2.715 | 3.846 | 13.33 |
| `tls` | 1.899 | 1.919 | 2.538 | 9.50 | 2.782 | 2.812 | 4.244 | 13.91 |
| `tls-pool` | 0.189 | 0.184 | 0.289 | 0.94 | 0.129 | 0.119 | 0.472 | 0.65 |
| `tls-parallel` | 0.716 | 0.710 | 1.130 | 3.58 | 1.068 | 1.012 | 1.928 | 5.34 |

The TLS flush on Linux by connection count (µs per connection): 50, 0.533 ms (10.7); 100, 1.026 ms (10.3); 200,
1.899 ms (9.5); 400, 3.748 ms (9.4). `tls-pool` at 400: 0.361 ms.

- **The write is the cost, not the outbox.** Framing 200 connections' packets into their burst buffers takes 0.07 ms
  a tick; writing them out takes 25 to 40 times that. On Linux about 4 µs per connection is the send call and about
  5 µs the TLS record layer, linear in connections. On the homelab the world talks to its edge proxy over the pod
  network rather than loopback, and the ramp's 4.2 ms after the world update at 200 players is about 20 µs per
  connection: this cost, with pod networking added.
- **The flushers are not.** With nothing changed, the inventory, sheet and ability-amount flushers took 0.8, 8.8 and
  2.6 µs per tick for 200 connections together (a one-off in-process measurement), and the quest flusher's steady
  state is a handful of comparisons per connection.
- **Off the tick, the tick's share falls by a factor of 10.** `tls-pool` leaves the tick 0.9 µs per connection
  (framing and queueing), `tls-parallel` 3.6 µs (the same work on four cores, waited for). Whether the write should
  leave the tick is an open decision (#875): it changes when a packet leaves relative to the tick and puts the TLS
  and socket work on other threads.

### Results — the read loop, per frame (#875, 2026-10-09)

`receive`, 200 connections each sending one frame per tick, 1800 ticks: 360,000 frames read over TLS.
`PacketStream.EnumerateRawFramesAsync` refilled its buffer through `RefillAsync`, which took a delegate to set the
new end of the data (one per read: the lambda captured the iterator's state) and was an `async ValueTask<bool>` (a
state-machine box per read that waited for data, which is nearly every read of a client sending 60 small packets a
second). It now returns the new end, or -1 once the stream ended, and its state machine is pooled
(`PoolingAsyncValueTaskMethodBuilder`).

| | Linux | Windows |
|---|---:|---:|
| Before, B allocated per frame read | 208.1 | 208.2 |
| After | 64.1 | 64.2 |
| After the header struct (#875, 2026-10-10) | 0.0 to 0.1 | 0.0 to 0.1 |

On 2026-10-10 (#875) `NetworkPacketHeader` became a struct, and `InboundPacketFrame.ParseFrame` reads its four
fields by hand rather than through protobuf-net (which allocates for a struct it deserializes too): before, 64.1 B per
frame read on Linux and 64.3 to 64.6 B on Windows; after, 0.0 to 0.1 B on both (two runs each, Linux in a 4-CPU
`dotnet/sdk:10.0` container, .NET 10.0.12). The read loop itself no longer allocates per frame; what is left is the
`Packet` the decrypt and deserialize that follow create (80 B, see
[GC-008](#packet-reader-decrypt-gc-008--benchmark-results)). This is off the tick, on the connection's read loop: at
the ramp's 12,000 packets a second in, the two changes together leave about 2.5 MB/s less garbage.

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

- The counting stream stands in for production's `SslStream`, so the TLS record layer and the socket send are **not**
  in the numbers: [outbox flush](#outbox-flush--the-send-path-over-real-sockets-875) measures them, about 10 µs per
  connection per tick on Linux loopback against 0.3 µs here. The counting stream completes every write at once, as a
  socket with room in its send buffer does; the outbox then reads the write's outcome inline (#875), so a flush over
  either allocates nothing on the tick thread.
- Scenario connections share key material. The point is the cost of sealing, not the secrecy of the result.
- The `DiagnosticsConfig` counters (bytes, packets sent and dropped) are skipped, so their cost is **excluded** from the
  numbers. They allocate nothing while no listener is attached, but production attaches an OpenTelemetry listener.

Not modelled at all: the session pass (the first of the tick's two passes), the readiness barrier, the quest flusher,
party member status, presence, pings, continuations, persistence and saves, and combat. Of the flushers `WorldServer`
runs after the world update, the scenario tick runs the three that need no service, in its order and before the
outbox flush (#875): inventory, character sheet and ability amounts. Nothing a scenario does changes an inventory, the
stats or the abilities, so they send nothing; they must allocate nothing either, and with a closure the inventory
flusher allocated on every call (32 B per player per tick, 57,600 B per window in `town-idle`), which the gate would
fail. The quest flusher needs the quest service and the reference data, which no scenario builds. Combat is the next scenario to add (`dungeon-combat`). Of `World.Update`
itself, the scenario tick runs the registry's publication of finished builds (`InstanceRegistry.PublishFinished`) and
the instance pass, and leaves out:

- `Time.Update`, the content and script hot reloads, and the parties tick.
- `InstanceRegistry.ProcessExpiredInstances`, the last step. Its walk of the registry allocates a 72 B enumerator per
  tick in an unoptimized (Debug) build and, measured, nothing in Release, so it would add nothing to the committed
  figure and would fail `town-idle`'s gate (4,320 B per window over a 0 B baseline) in every local Debug run.
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
with the committed figure, in every build (Debug and Release, see below):

- **Fail** when the current figure is more than 1% **and** more than 256 B over the committed one. The 256 B floor
  keeps a scenario that allocates almost nothing (`town-idle`) from failing on one stray object.
- **Improvement notice** (test output, not a failure) when it is more than 1% and more than 256 B under.
- **Detection floor.** With the committed figures, the gate fails on a rise of more than **256 B per window** in
  `town-idle` (about 4 B per tick: it allocates nothing since #851, so the floor decides, and any one object allocated
  every tick fails it), **8 B per player per tick** in `town-walk` (14,402 B per window: it fails from 1,454,643 B), and
  **3.9 B per player per tick** in `many-instances` (7.7 B per instance per tick, 115,897 B per window: it fails from
  11,705,626 B). So one new object, of any size, per walking player per tick fails both moving scenarios, and so does
  one per instance per tick in `many-instances`; one object per tick for the whole town (about 1.4 KB per window) does
  not fail `town-walk`. Under the 5% band before #852 the floors were 40 and 19 B per player per tick.
- **Why 1%.** Every run measured after #854 reads within ±0.05% of the committed figures (the table below), and
  CI's runner, before #854, read within about 0.1% of the developer machine once the per-send delegate it alone
  paid was taken out (+1,384 B in `town-walk`, +4,000 B in `many-instances`, see #851's results). 1% is ten times the
  widest of these.
- **Ratchet.** The committed file goes down only by an explicit commit. When a change makes a scenario cheaper,
  regenerate and commit the lower figure, so the gain cannot be lost again unnoticed. When an increase is intended,
  regenerating is also the fix, and the JSON diff shows the reviewer what it costs.

Regenerate (every scenario, in Release; the runner refuses `--write-allocations` for a subset):

```bash
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-allocations perf/scenario-allocations.json
```

**A red gate with no code change: check the runtime, then regenerate.** CI installs the latest .NET 10 SDK and runtime
(`dotnet-version: 10.0.x`), and `global.json` lets the SDK roll forward (`rollForward: latestMajor`), so a new runtime
patch can move the figures with no change in this repository. The CI run's summary table names the runtime and OS that measured it; when they differ from the run that
committed the baseline and the code did not change, regenerate the baseline on the new runtime and commit it.

**The gate decides in every build, and every machine reads the same figures.** CI builds Release on a GitHub-hosted
runner; a plain local `dotnet test` builds Debug. Since #854 the committed figures are a plain Release run on the
developer machine, and Debug, Release, Windows and Linux read them within run-to-run noise. Measured at `2fe836e8`
(#852, #859), bytes per window, committed `town-walk` 1,440,240 and `many-instances` 11,589,728:

| Run | `town-walk` | `many-instances` |
|---|---|---|
| Windows, Release, the gate alone (×3) | 1,440,000 / 1,439,760 / 1,440,208 | 11,589,728 / 11,587,728 / 11,593,728 |
| Windows, Debug, the gate alone (×3) | 1,440,208 / 1,440,688 / 1,440,432 | 11,589,728 / 11,589,728 / 11,593,728 |
| Linux container (4 CPUs, 16 GB, CI's runner size), Release, the gate alone (×3) | 1,440,240 / 1,440,000 / 1,440,960 | 11,593,728 / 11,589,728 / 11,589,728 |
| Linux container, Release, the whole solution's `dotnet test` as CI runs it (×2) | 1,440,664 / 1,439,944 | 11,593,728 / 11,589,728 |
| Windows, Release runner, `DOTNET_JitObjectStackAllocation=0` (#854) | 1,439,520 | 11,589,728 |

The CI runs on `main` after #854 passed the gate, but CI's test step does not print a passing test's output, so their
figures are not in the logs, only the verdict. Since #852 the gate writes them to the run's summary page: when
`GITHUB_STEP_SUMMARY` is set (on GitHub Actions), every scenario appends a row, pass or fail, to a "Scenario allocation
gate" table (scenario, committed, measured, change in percent, tolerance, verdict) on the workflow run's Summary tab. A
summary file that cannot be written is skipped with a line in the test output and never fails the gate. Read CI's
figures there.

A Debug build never optimises, so it never keeps an object on the stack; a Release build keeps some there once the
optimised JIT tier applies. That Debug reads the same as Release shows that nothing on the measured path allocates
differently depending on how far the JIT has got, which is what made the CI runner read higher before #854 (below).
**Escape hatch:** should a change make Debug and Release diverge again (an allocation the optimised JIT keeps on the
stack and unoptimised code does not), or should another machine or architecture read differently in Debug (arm64,
for one, has not been measured), the gate goes back to deciding in Release only, as #856 had it: put the
`Regressed` branch of `ScenarioAllocationsShould` under `#if !DEBUG` and write the comparison in Debug instead. A
scenario that stopped doing its work still fails its `Verify` check in every build.

Until #854 the figures depended on the machine, through one object: the delegate each send created by passing
`CryptoSession.Encrypt` as a method group, about 64 B per walking player per tick. At most send sites the optimised JIT
tier kept it on the stack (not everywhere: the Serialization benchmark shows it on the heap, fully optimised, where
`Create` is not inlined); until that tier applied, it was on the heap. The CI runner (a small, shared VM, with the other
test assemblies running in parallel) ended the gate's 5 s warm-up before the optimised tier reached the send path, and a
Debug build never optimises, so both read about 7% higher in `town-walk` and 14% higher in `many-instances` than a
developer machine or the homelab node in Release; the committed figures were CI's, and a faster machine's run was not to
be committed. #854 passes the session's `Encryptor`, a delegate created once with the session, so how far the JIT has
got no longer changes what a send allocates. The gate runs in a non-parallel xUnit collection, so no other test in the
World assembly competes for the CPU during the wall-clock warm-up; other test assemblies still run in parallel processes
under a solution-wide `dotnet test`, and the minimum-of-windows rule absorbs that. The gate adds about 18 s to the World
suite.

### Results — no closure per flusher call (#875, 2026-10-09)

Two methods the post-update flushers call for every connection on every tick declared a lambda over a local whose
scope was the whole method, so the compiler allocated the lambda's closure on entry, before the early return that
almost every call takes: `InventoryUpdateFlusher.Flush` (the slot query captures the character, 32 B) and
`QuestService.EnteredInstanceIfChanged` (the hook captures the instance, 40 B). Both moved the lambda into a method
called only when there is work. Measured in process with 200 connections and nothing changed: the inventory flush
went from 6,400 B and 1.6 µs per tick to 0 B and 0.8 µs, and `EnteredInstanceIfChanged` from 40 B per call to 0. At
200 players that is 72 B per player per tick, about 0.86 MB/s less garbage from the tick thread. The committed
figures do not move: the scenario tick did not run the flushers before, and now runs the inventory, sheet and
ability-amount flushers, guarded at 0 by `town-idle`.

### Results — no task per outbox flush (#875, 2026-10-09)

`TickDrivenOutbox.Flush` observed every write through `WriteAsync(...).AsTask().ContinueWith(...)`. A write that is
over before `WriteAsync` returns, which is every write to a socket with room in its send buffer, `SslStream` included,
and every write to the scenarios' counting stream, still cost a continuation task: 112 B per connection that had
anything to send, every tick. The flush now reads a finished write's outcome inline and keeps the continuation for a
write still in flight or already failed. The existing `TickDrivenOutbox` tests cover the three ways a write ends.

Allocations, from `perf/scenario-allocations.json` (the developer machine's Release run, i9-12900K, Windows 11,
.NET 10.0.12): each scenario falls by about 112 B per player per tick, the continuation (111.7 in `town-walk`, 111.9
in `many-instances`).

| Scenario | Players | bytes/window | B/tick | B/player/tick | Before #875 | Change |
|---|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 0 | 0 |
| `town-walk` | 30 | 1,239,120 | 20,652 | 688.40 | 1,440,240 | −14.0% |
| `many-instances` | 500 | 8,233,728 | 137,229 | 274.46 | 11,589,728 | −29.0% |

Like for like on the same machine (Release, the runner, 5 s warm-up, 3600 timed ticks), `77000278` and with #875:

| Scenario | bytes/window before | after | Tick ms mean before | after | p99 before | after | gen0 before | after |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-walk` | 1,440,960 | 1,238,640 | 0.142 | 0.143 | 0.573 | 0.469 | 33 | 27 |
| `many-instances` | 11,589,728 | 8,233,728 | 1.278 | 1.209 | 2.292 | 2.239 | 239 | 42 |

Over real sockets ([outbox flush](#outbox-flush--the-send-path-over-real-sockets-875), 200 connections), the tick
thread allocated 22,400 B per tick before, in every mode, TLS included, and 0 B after, on Linux and Windows. The flush
time does not move by more than run-to-run noise: the task was cheap to run, only not to collect. At 200 players with
something to send each tick, that is about 1.3 MB/s less garbage from the tick thread.

### Results — the cached cipher delegates (#854, 2026-10-09)

Every send passed `CryptoSession.Encrypt` to its packet's `Create` as a method group, a new delegate per packet; the
read loop did the same with `CryptoSession.Decrypt`. The session now exposes `Encryptor`, created once with it, and
`Connection` creates its decrypt delegate once with its session. `SessionDelegatesShould` fails if a method group
comes back anywhere in `src/`.

Allocations, from `perf/scenario-allocations.json`: the developer machine's Release run with #854 (i9-12900K, Windows
11, .NET 10.0.12). "Before #854" is the committed figure before it (#851's, which carried the CI runner's `Encrypt`
delegate). "Fails from" and "Notice at" are at the 5% band of the time; #852 narrowed it to 1% (see
[The allocation gate](#the-allocation-gate)).

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at | Before #854 | Change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 257 | — | 0 | 0 |
| `town-walk` | 30 | 1,440,240 | 24,004 | 800.13 | 1,512,253 | 1,368,227 | 1,570,560 | −8.3% |
| `many-instances` | 500 | 11,589,728 | 193,162 | 386.32 | 12,169,215 | 11,010,241 | 13,769,728 | −15.8% |

Like for like on the developer machine (Release, the runner, 10 s warm-up, 3600 timed ticks), `main` at `a892abe7`
and with #854:

| Scenario | bytes/window before | after | Change | Tick ms mean before | after | p95 before | after |
|---|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 0 | 0 | 0 | 0.037 | 0.031 | 0.065 | 0.054 |
| `town-walk` | 1,455,840 | 1,440,240 | −15,600 | 0.193 | 0.191 | 0.550 | 0.490 |
| `many-instances` | 11,849,728 | 11,589,728 | −260,000 | 2.021 | 1.954 | 3.501 | 3.500 |

- **The committed figures fall by the delegate CI paid**: 130,320 B per window in `town-walk` and 2,180,000 B in
  `many-instances`, about 72 B per walking player per tick.
- **The developer machine gains too**, about 8.7 B per walking player per tick (15,600 and 260,000 B per window), in
  code the optimised tier had already reached: most likely not every send site's delegate was kept on the stack. Both
  scenarios fall by the same amount per player per tick, as a cost of the per-player send path would.
- **No dependency on the JIT is left on this path.** The same Release runner with stack allocation turned off
  (`DOTNET_JitObjectStackAllocation=0`) reads 1,439,520 and 11,589,728 B, and the Debug gate 1,440,688 and
  11,587,728 B, within run-to-run noise of the committed figures. Before #854 the same switch added 64 B per walking
  player per tick.
- **The timings are not read as a change.** They move between runs by more than a delegate could cost.

### Results — the tick's instance snapshot (#851, 2026-10-09)

`World.Update` used to tick a fresh copy of the registry's instance list (`InstanceRegistry.ActiveInstances`, a
`ToList` of a `ConcurrentDictionary`'s values) every tick and walk it through a boxed enumerator. It now ticks
`InstanceRegistry.TickInstances`: an array the registry rebuilds only after it publishes or removes an instance, in
publication order, walked as a span. `ActiveInstances` keeps its fresh copy for the readers off the tick (the gauges,
presence, the hot reload walk).

Allocations, from `perf/scenario-allocations.json` at the time: the developer machine's Release run at `7a40ed08`
plus the `Encrypt` delegate CI still allocated then (see #854 above). They were derived, not measured on CI: the
Release run plus 64 B × 60 ticks × the walking players (115,200 B per window in `town-walk`, 1,920,000 B in
`many-instances`); the offset #850 measured between CI and the developer machine was slightly larger (+116,584 B and
+1,924,000 B). "Before #851" is the committed figure before it, CI's run
of #850.

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at | Before #851 | Change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 257 | — | 9,600 | −100% |
| `town-walk` | 30 | 1,570,560 | 26,176 | 872.53 | 1,649,089 | 1,492,031 | 1,581,064 | −0.7% |
| `many-instances` | 500 | 13,769,728 | 229,495 | 458.99 | 14,458,215 | 13,081,241 | 14,018,368 | −1.8% |

Like for like on the developer machine (Release, the same i9-12900K), before and after:

| Scenario | bytes/window before | after | Change | Tick ms mean before | after | p95 before | after |
|---|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 9,600 | 0 | −9,600 | 0.034 | 0.030 | 0.057 | 0.053 |
| `town-walk` | 1,464,480 | 1,455,360 | −9,120 | 0.211 | 0.171 | 0.739 | 0.444 |
| `many-instances` | 12,094,368 | 11,849,728 | −244,640 | 2.235 | 2.027 | 3.799 | 3.621 |

- **An idle tick allocates nothing.** `town-idle` falls from 160 B per tick to 0: the copy was all it allocated.
- **The saving grows with the instances.** About 160 B per tick with one instance and about 4 KB per tick
  (244,640 B per window) with 250, the size of the copy; with no instance published or removed, the walk allocates
  nothing at any count. A tick that publishes or removes one allocates one array of the live instances.
- **The timings are not read as a change.** The tick time moves between runs by more than the copy could cost.

### Results — after the platform cipher (#850, 2026-10-09)

Allocations, from `perf/scenario-allocations.json` at the time: CI's Release run of #850 on its GitHub-hosted runner
(2026-10-09). "Fails from" is the first figure the gate fails on; "Notice at" the highest figure that prints the
improvement notice. "Before #850" is the committed figure before it (the first baseline below, a Release run on the
developer machine).

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at | Before #850 | Change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 9,600 | 160 | 5.33 | 10,081 | 9,119 | 9,600 | 0 |
| `town-walk` | 30 | 1,581,064 | 26,351 | 878.37 | 1,660,118 | 1,502,010 | 5,964,720 | −73.5% |
| `many-instances` | 500 | 14,018,368 | 233,639 | 467.28 | 14,719,287 | 13,317,449 | 62,226,368 | −77.5% |

The committed change mixes machines. Like for like, on the developer machine below, the walking scenarios fell to
1,464,952 B (`town-walk`, −75.4%) and 12,098,368 B (`many-instances`, −80.6%); the difference was the `Encrypt`
delegate, still on the heap when CI's warm-up ended (see #854 above).

Timing and GC, from the developer machine's Release run (2026-10-09; the same machine as the first baseline:
i9-12900K, Windows 11 Pro, .NET 10.0.12, Server GC; 10 s warm-up, 3600 timed ticks):

| Scenario | Tick ms mean | p95 | p99 | max | % > 16.7 ms | gen0 | gen1 | gen2 | GC pause ms | GC pause % |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 0.033 | 0.059 | 0.076 | 0.175 | 0.00 | 0 | 0 | 0 | 0.00 | 0.00 |
| `town-walk` | 0.182 | 0.479 | 0.858 | 2.900 | 0.00 | 7 | 0 | 0 | 2.52 | 0.38 |
| `many-instances` | 2.099 | 3.533 | 4.456 | 11.257 | 0.00 | 234 | 0 | 0 | 58.12 | 0.77 |

The homelab k3s node, the machine the capacity runs will measure (2026-10-09; Linux, AMD Ryzen 9 5900X, 12 cores / 24
threads, .NET 10.0.12, Server GC, Release, in the `dotnet/sdk:10.0` container, 0 players online on the node's worlds
at the time). Its allocations match the developer machine's, not CI's:

| Scenario | Players / instances | bytes/window | B/player/tick | Tick ms mean | p95 | p99 | max | % > 16.7 ms | gen0 | gen1 | gen2 | GC pause ms | GC pause % |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 / 1 | 9,600 | 5.33 | 0.041 | 0.042 | 0.045 | 0.054 | 0.00 | 0 | 0 | 0 | 0.00 | 0.00 |
| `town-walk` | 30 / 1 | 1,466,160 | 814.53 | 0.212 | 0.594 | 0.654 | 1.909 | 0.00 | 7 | 0 | 0 | 4.04 | 0.53 |
| `many-instances` | 500 / 250 | 12,094,368 | 403.15 | 1.626 | 2.562 | 2.789 | 5.312 | 0.00 | 235 | 0 | 0 | 54.15 | 0.92 |

- **The cipher was three quarters of what a walking player allocated.** `town-walk` falls from 3.3 KB to 814 B per
  player per tick and `many-instances` from 2.1 KB to 403 B on the developer machine and the homelab node (878 B and
  467 B in CI's figures);
  `town-idle`, which sends nothing, is unchanged. What is
  left per walker is the state acknowledgement itself (its serialization and the sealed `byte[]`) and the input path.
- **GC follows.** `town-walk` ran 7 gen0 collections in its timed minute against 116, and spent 0.38% of the time in
  GC pauses against 3.09%; `many-instances` 0.77% against 1.63%, with no gen1 or gen2 collection. Its gen0 count is
  about the same (234 against 225) at a fifth of the allocation, which was not investigated. The mean and p95
  tick fell (`town-walk` 0.220 → 0.182 ms mean, `many-instances` 2.564 → 2.099 ms); the max is one tick per run and
  moves between runs (`many-instances` 11.3 ms here against 7.7 ms), so it is not read as a change.

### Results — first baseline (2026-10-08)

Allocations, from `perf/scenario-allocations.json` at the time (commit `9dec695b`, Release; regenerated when the
scenario tick gained the registry's `PublishFinished`, which moved no figure beyond run-to-run noise), before #850.

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
  It is small, but it is a fixed cost of every tick, and the floor the other scenarios stand on. #851 removed it
  (results above).
- **Walking costs kilobytes per player per tick, and most of it is encryption.** About 3.3 KB per player per tick in
  `town-walk` and 2.1 KB in `many-instances`, dominated by the per-packet BouncyCastle encrypt (1.7–4.6 KB per call in
  the session cipher results above) of the state acknowledgement each walker is sent every tick. Issue #850 was the
  fix, measured in the results above it.
- **The allocation rate shows up as GC.** `town-walk` allocates about 6 MB per 60-tick window on the tick thread and
  ran 116 gen0 collections in its timed minute, 3.1% of the time in GC pauses; `many-instances` allocates about 62 MB
  per window, with 225 gen0, 2 gen1 and 2 gen2 collections. No tick went over the 16.7 ms budget, but the worst ticks
  (4.4 ms and 7.7 ms) sit well above the p99, as a few ticks absorbing a GC pause would.
- **Tick time follows the walking players, not the instance count.** `many-instances` holds about 17 times the
  players of `town-walk` in 250 times the instances, and its mean tick is about 12 times longer: a little less per
  player (`town-walk` also runs ten creatures), so at this scale the instance count adds no cost that stands out. Its p95 of 4.4 ms is about a quarter of the tick
  budget for 500 walking players, before saves, combat or the session pass.
