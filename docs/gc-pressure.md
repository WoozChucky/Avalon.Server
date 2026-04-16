# GC Pressure & Heap Allocation Findings

Findings from a full-scope static analysis of the World server — network layer, packet
pipeline, and simulation tick loop. Issues are ordered by severity / frequency.
Each entry tracks its current status so they can be resolved one at a time.

---

## GC-001 — Outbound packet serialization: `MemoryStream` + `ToArray()` + encrypt copy

**Status:** Resolved — `PacketSerializationHelper` + `PooledArrayBufferWriter` + `EncryptFunc(ReadOnlySpan<byte>)`; 1 alloc per packet (was 3)  
**Severity:** Critical  
**Files:** Every `SXxx.Create()` static factory across `src/Shared/Avalon.Network.Packets/**/*.cs`

### Problem

Every outbound packet follows this pattern:

```csharp
using var memoryStream = new MemoryStream();       // alloc 1 (+ internal buffer growth)
Serializer.Serialize(memoryStream, p);
var buffer = encryptFunc(memoryStream.ToArray());  // alloc 2 (ToArray copy), alloc 3 (encrypt result)
```

Three heap allocations minimum per outbound packet. The state broadcast alone sends
Add + Update + Remove packets to every player on each broadcast interval (~10 Hz), so
this fires hundreds of times per second under any load.

### Fix Direction

- Replace `MemoryStream` with an `ArrayPool`-backed buffer or `RecyclableMemoryStream`.
- Change `encryptFunc` signature to accept/return `Span<byte>` or write into a caller-supplied
  `IBufferWriter<byte>` so the encrypt step can also be allocation-free.
- Ideally serialize and encrypt directly into the socket send buffer in one pass.

---

## GC-002 — Per-tick entity state: `new byte[]` per visible entity per broadcast

**Status:** Resolved — single contiguous `ArrayPool` buffer (65 536 bytes) per player per call; `ReadOnlyMemory<byte>` slices on `ObjectAdd`/`ObjectUpdate.Fields`; pre-allocated `List<ObjectAdd>`/`List<ObjectUpdate>` per player (`PerPlayerBroadcastState` on `MapInstance`). Eliminates per-entity `byte[]` copy; residual allocations are N `ObjectAdd`/`ObjectUpdate` class instances + 1 encrypted payload `byte[]` per call (~50% allocation reduction vs. legacy; Gen1 promotions eliminated).  
**Severity:** Critical  
**File:** `src/Server/Avalon.World/Instances/MapInstance.cs:252–295`

### Problem

`BroadcastStateTo` correctly rents an `ArrayPool` scratch buffer, but then copies each
entity's serialized bytes into a freshly allocated `byte[]` stored on `ObjectAdd.Fields`
/ `ObjectUpdate.Fields`:

```csharp
List<ObjectAdd> addedObjects = [];                                        // new list per call
ObjectAdd obj = new() { Fields = new byte[bytesWritten] };               // new byte[] per entity
buffer.AsSpan(0, (int)bytesWritten).CopyTo(obj.Fields);
```

With 20 visible entities and 60 players this is 1,200+ short-lived `byte[]` allocations
per broadcast tick flowing through Gen0.

### Fix Direction

- Pre-allocate per-player `ObjectAdd`/`ObjectUpdate` lists and reset them each frame
  instead of creating new ones.
- Consider building the full state packet into a single rented buffer per player (one
  `MemoryStream` or `ArrayBufferWriter` per player, writing all entity blobs contiguously)
  and passing a `ReadOnlyMemory<byte>` slice rather than per-entity copies.

---

## GC-003 — `WorldServer.Connections` rebuilds `ImmutableArray` every tick

**Status:** Resolved — `private ImmutableArray<T> _typedConnections` field in `ServerBase<T>` (exposed as `protected ImmutableArray<T> TypedConnections`), maintained via `ImmutableInterlocked.Update` on connect/disconnect. `WorldServer.Connections` and `AuthServer.Connections` read via zero-allocation `CastArray<TInterface>()`. Rebuild cost moved from O(n) per tick to O(n) per connection lifecycle event.  
**Severity:** Critical  
**File:** `src/Server/Avalon.Hosting/Networking/ServerBase.cs`

### Problem

```csharp
public new ImmutableArray<IWorldConnection> Connections =>
    [.. base.Connections.Values.Cast<WorldConnection>()];
```

This property is called inside `Update()` on every tick (and also in `DelayedDisconnect`).
Each access enumerates the dictionary, casts every value, and packs the result into a
brand-new `ImmutableArray<IWorldConnection>`.

### Fix Direction

- Maintain a cached `ImmutableArray<IWorldConnection>` field that is rebuilt only when
  connections are added or removed (i.e. in `OnClientAccepted` and `RemoveConnection`).
- Alternatively, iterate `base.Connections.Values` directly in `Update()` and avoid the
  cast entirely since `WorldConnection` is the only concrete type.

---

## GC-004 — `WorldSessionFilter` re-allocated per connection per tick

**Status:** Resolved — `IWorldConnection.Update(TimeSpan, PacketFilter)` split into `UpdateSession(TimeSpan)` (called by `WorldServer`, uses stored `_worldSessionFilter`) and `UpdateMap(TimeSpan)` (called by `MapInstance`, uses stored `_worldMapFilter`). Shared logic extracted to private `ProcessQueue(PacketFilter)`. Zero filter allocations per tick.  
**Severity:** High  
**File:** `src/Server/Avalon.World/WorldServer.cs:201`

### Problem

```csharp
foreach (IWorldConnection worldConnection in Connections)
{
    WorldSessionFilter filter = new(worldConnection);  // new object every tick per player
    worldConnection.Update(elapsedTime, filter);
}
```

Each `WorldConnection` already owns a `_worldSessionFilter` instance (constructed once
in the `WorldConnection` constructor and stored at line 44 / 207). The per-tick
re-allocation is redundant.

### Fix Direction

- Expose the existing `_worldSessionFilter` (or a `PacketFilter` property) on
  `IWorldConnection` and pass it directly, or have `worldConnection.Update` use its own
  stored filter without accepting one as a parameter.

---

## GC-005 — `List<WorldPacket> requeuePackets` allocated per tick per connection, never populated

**Status:** Resolved — dead `List<WorldPacket> requeuePackets = []` local and `_receiveQueue.Readd(requeuePackets)` call removed from `WorldConnection`. Requeue was unreachable: `LockedQueue.Next(check)` peeks first, so packets that fail the filter are never dequeued and cannot be re-added. Removed alongside GC-004 fix.  
**Severity:** Medium  
**File:** `src/Server/Avalon.World/WorldConnection.cs:100`

### Problem

```csharp
public void Update(TimeSpan deltaTime, PacketFilter filter)
{
    List<WorldPacket> requeuePackets = [];   // allocated every tick
    // ... nothing ever added to requeuePackets ...
    _receiveQueue.Readd(requeuePackets);     // always passes an empty list
}
```

The list is constructed on every call but never written to. If re-queue logic is not yet
implemented this is pure waste at 60 Hz per connected player.

### Fix Direction

- Remove the local variable and the `Readd` call until re-queue logic is actually
  implemented, or pre-allocate a single `List<WorldPacket>` field on `WorldConnection`
  and clear it each tick.

---

## GC-006 — `JsonSerializer.Serialize` evaluated eagerly before log-level guard

**Status:** Resolved — `_logger.IsEnabled(LogLevel.Debug/Trace)` guard added before both `JsonSerializer.Serialize` calls; no allocation in production with debug/trace logging off.  
**Severity:** High  
**File:** `src/Server/Avalon.Hosting/Networking/Connection.cs:136, 227`

### Problem

```csharp
// Inbound (line 136)
_logger.LogDebug("IN: {Type} => {Data}", packet.Header.Type, JsonSerializer.Serialize(packet));

// Outbound (line 227)
_logger.LogTrace("OUT: {Type} => {Packet}", packet.Header.Type, JsonSerializer.Serialize(packet));
```

`JsonSerializer.Serialize(packet)` is **always** evaluated regardless of whether debug/trace
logging is enabled. In production with debug logging off this allocates a temporary JSON
string for every non-movement inbound packet and every non-state outbound packet.

### Fix Direction

```csharp
if (_logger.IsEnabled(LogLevel.Debug))
    _logger.LogDebug("IN: {Type} => {Data}", packet.Header.Type, JsonSerializer.Serialize(packet));
```

---

## GC-007 — `PacketReader.Read` boxes reflection arguments per inbound packet

**Status:** Resolved — `_packetTypes` dict value changed from `(Type, MethodInfo)` to `Func<ReadOnlyMemory<byte>, Packet?>` built once per type at startup via `BuildDeserializer<T>()` + `MakeGenericMethod`. `Read()` calls the cached delegate directly — zero per-call allocation (was: `new object?[3]` + boxed `ReadOnlyMemory<byte>` struct).  
**Severity:** Medium  
**File:** `src/Server/Avalon.Hosting/Networking/PacketReader.cs:82`

### Problem

```csharp
object? payload = p.deserialize.Invoke(null, new object?[] { payloadMemory, null, null });
```

Every inbound packet deserialization allocates a fresh `object?[3]` for the reflection
invoke and boxes `payloadMemory` (`ReadOnlyMemory<byte>` is a struct).

### Fix Direction

Cache a typed open delegate per packet type:

```csharp
// Build once per type during registration
Func<ReadOnlyMemory<byte>, object?> factory =
    (Func<ReadOnlyMemory<byte>, object?>)Delegate.CreateDelegate(...);
```

Eliminates the array and the boxing on every deserialization call.

---

## GC-008 — `PacketReader.Decrypt` replaces payload with a freshly allocated `byte[]`

**Status:** Open  
**Severity:** Medium  
**File:** `src/Server/Avalon.Hosting/Networking/PacketReader.cs:71`

### Problem

```csharp
public void Decrypt(NetworkPacket packet, DecryptFunc decryptFunc) =>
    packet.Payload = decryptFunc(packet.Payload);
```

`decryptFunc` returns a new `byte[]`. The original payload (also a `byte[]` from
Protobuf deserialization) becomes unreachable immediately. Two short-lived buffers
exist simultaneously for every encrypted inbound packet.

### Fix Direction

- Change `DecryptFunc` to `void DecryptFunc(Span<byte> input, IBufferWriter<byte> output)`
  (or decrypt in-place where the cipher allows it) so the payload slot is written to
  once rather than swapped.
- Alternatively, decrypt directly into a pooled buffer during `PacketStream.EnumerateAsync`
  before yielding the packet.

---

## GC-009 — `WorldPacket` inner class allocated per received packet

**Status:** Open  
**Severity:** Medium  
**File:** `src/Server/Avalon.World/WorldConnection.cs:189, 199–203`

### Problem

```csharp
private class WorldPacket          // reference type → heap allocation
{
    public NetworkPacketType Type { get; set; }
    public Packet? Payload { get; set; }
}

// In OnReceive:
_receiveQueue.Add(new WorldPacket { Type = packet.Header.Type, Payload = payload });
```

Every inbound packet that passes the filter creates a new `WorldPacket` class object.

### Fix Direction

Convert to a `readonly record struct` (or plain `struct`). The queue must then hold value
types — verify `LockedQueue<T>` is generic enough to store structs without boxing.

---

## GC-010 — `WorldServer.GetContextPacket` uses `Activator.CreateInstance` + reflection `SetValue` per packet

**Status:** Open  
**Severity:** Medium  
**File:** `src/Server/Avalon.World/WorldServer.cs:238–242`

### Problem

```csharp
object context = Activator.CreateInstance(typeof(WorldPacketContext<>).MakeGenericType(packetType))!;
cachedProperties.packetProperty.SetValue(context, packet);
cachedProperties.connectionProperty.SetValue(context, connection);
```

`MakeGenericType` is cached, but `Activator.CreateInstance` and `SetValue` via
`PropertyInfo` still run on every auth/handshake packet dispatch — both are significantly
slower and more allocating than direct construction.

### Fix Direction

Cache a compiled factory + setter delegate per packet type:

```csharp
// Built once per type, stored in _propertyCache:
Func<IConnection, Packet, object> factory = (conn, pkt) => new WorldPacketContext<TPacket>
    { Connection = (IWorldConnection)conn, Packet = (TPacket)pkt };
```

---

## GC-011 — `ServerBase.CallListener` boxes invoke arguments per packet

**Status:** Open  
**Severity:** Medium  
**File:** `src/Server/Avalon.Hosting/Networking/ServerBase.cs:183`

### Problem

```csharp
await ((Task)handlerCache.ExecuteMethod.Invoke(
    packetHandler,
    new[] { context, _stoppingToken.Token }  // new object[] per call, CancellationToken boxed
)!).ConfigureAwait(false);
```

### Fix Direction

Replace `MethodInfo.Invoke` with a cached `Func<object, object, CancellationToken, Task>`
delegate (created once via `Delegate.CreateDelegate` or expression trees).

---

## GC-012 — `EnqueueContinuation<T>` boxes result and allocates two closures per character action

**Status:** Open  
**Severity:** Medium  
**File:** `src/Server/Avalon.World/WorldConnection.cs:214–221`

### Problem

```csharp
Task<object> wrappedTask =
    task.ContinueWith(t => (object)t.Result, ...);   // closure + boxing of T result
Action<object> wrappedCallback = result => callback((T)result);  // second closure
_genericTaskQueue.Enqueue((wrappedTask, wrappedCallback));
```

Every character event (select, enter map, attack hit) allocates two closure display classes
and boxes the typed result through `object`.

### Fix Direction

Store the continuation as `(Task task, object state, Action<object, object> invoke)` where
`state` is the typed callback, avoiding the inner closure. Or redesign the queue to hold
`(Task<T>, Action<T>)` pairs via an interface, removing boxing entirely.

---

## GC-013 — `Task.Delay(1)` in idle send loop fires ~1000×/s per connection

**Status:** Open  
**Severity:** Medium  
**File:** `src/Server/Avalon.Hosting/Networking/Connection.cs:233`

### Problem

```csharp
NetworkPacket? packet = await _packetsToSend.DequeueAsync(...);
if (packet != null)
{
    // ... send ...
}
else
{
    await Task.Delay(1).ConfigureAwait(false);   // new Task allocated every idle ms
}
```

When no packets are queued the loop spins at ~1 kHz, creating a `Task` per iteration.
Each connection that is idle but connected contributes continuously.

### Fix Direction

`RingBuffer.DequeueAsync` already accepts a `CancellationToken` — if it blocks until a
packet is available there is no idle branch at all. Verify `DequeueAsync` truly suspends
(does not busy-loop) and remove the `Task.Delay(1)` fallback. If `DequeueAsync` can
return `null` immediately (non-blocking), switch to a `Channel<NetworkPacket>` or
`SemaphoreSlim`-signalled queue instead.

---

## GC-014 — LINQ `ToList()` on removes inside `SInstanceStateRemovePacket.Create`

**Status:** Open  
**Severity:** Low  
**File:** `src/Shared/Avalon.Network.Packets/State/SInstanceStatePacket.cs:110`

### Problem

```csharp
Removes = removes.Select(r => r.RawValue).ToList()
```

Allocates a new `List<ulong>` on every remove packet. Remove packets are sent whenever
an entity leaves a player's view.

### Fix Direction

Accept `IReadOnlyList<ulong>` (pre-projected upstream) or project directly into a
pre-allocated / pooled list. Given the small typical size (<10 entries) an
`ImmutableArray<ulong>` or stack-allocated span would also work.

---

## Priority Order for Resolution

| # | ID | Description | Impact |
|---|----|-------------|--------|
| 1 | ~~GC-003~~ | ~~`Connections` ImmutableArray rebuilt every tick~~ | ~~Critical~~ |
| 2 | ~~GC-004~~ | ~~`WorldSessionFilter` new() per tick per player~~ | ~~High~~ |
| 3 | ~~GC-005~~ | ~~Dead `requeuePackets` list per tick per player~~ | ~~Medium~~ |
| 4 | ~~GC-006~~ | ~~`JsonSerializer.Serialize` without log guard~~ | ~~High~~ |
| 5 | GC-001 | Outbound packet `MemoryStream+ToArray+encrypt` | Critical |
| 6 | GC-002 | Per-entity `new byte[]` in state broadcast | Critical |
| 7 | GC-009 | `WorldPacket` class → struct | Medium |
| 8 | ~~GC-007~~ | ~~`PacketReader` reflection `new object?[]` per packet~~ | ~~Medium~~ |
| 9 | GC-013 | `Task.Delay(1)` idle send loop | Medium |
| 10 | GC-012 | `EnqueueContinuation` boxing + two closures | Medium |
| 11 | GC-008 | Decrypt allocates new `byte[]` for payload | Medium |
| 12 | GC-010 | `Activator.CreateInstance` per packet dispatch | Medium |
| 13 | GC-011 | `ServerBase.CallListener` reflection `new[]` per call | Medium |
| 14 | GC-014 | LINQ `ToList()` in remove packet | Low |
