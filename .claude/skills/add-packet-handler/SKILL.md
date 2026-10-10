---
name: add-packet-handler
description: Scaffold a new packet handler for the Auth or World server following Avalon conventions
---

The user wants to add a new packet handler. Gather the following before writing any code:

1. **Packet name** — e.g. `CInventoryRequest` (client→server always starts with `C`, server→client with `S`)
2. **Target server** — `Auth` or `World`
3. **Opcode** — the `NetworkPacketType` enum value name (e.g. `CMSG_INVENTORY_REQUEST`) and its hex value (pick the next available in the appropriate range: client packets `0x2xxx`, server packets `0x3xxx`)
4. **Payload fields** — the Protobuf-net fields the packet carries (name, type, ProtoMember index)
5. **Response packet** — is there a server→client response? If so, what fields does it carry?
6. **For World game packets only** — what connection state is required? Pre-character (character selection screen) or in-map (character loaded and in a map)?

If any of the above are unclear, ask before writing code.

---

## World packet routing — understand this before implementing

`WorldConnection.OnReceive` routes packets on two distinct paths:

```
OnReceive(packet)
  ├─ WorldSessionFilter.CanProcess() == true  ─┐
  │  OR MapSessionFilter.CanProcess() == true  ─┤→ _receiveQueue → tick thread → WorldPacketHandler<T>
  │                                             │   (Avalon.World/Handlers/)
  └─ neither filter matches ────────────────────┘→ Server.CallListener() → IWorldPacketHandler<T>
                                                    (Avalon.Server.World/Handlers/, runs on receive thread)
```

**Tick-thread path** (`Avalon.World` handlers, `WorldPacketHandler<T>`):
- Packets are queued in `LockedQueue<WorldPacket>` and dequeued during `WorldServer.Update()` on the tick thread at ~60 Hz
- Safe to read/mutate `MapInstance`, `CharacterEntity`, and any other game state
- Handler `Execute` is **synchronous** — never block or long-await inside it
- For async work (e.g. DB queries), use `connection.EnqueueContinuation` — the callback fires on the next tick when the task completes

**Receive-thread path** (`Avalon.Server.World` handlers, `IWorldPacketHandler<T>`):
- Executes immediately on the network receive thread (async)
- Suitable for connection setup/teardown (world key exchange, handshake)
- **Do not touch `MapInstance` or character state here** — no tick-thread safety

**Filter logic** (both checked in `OnReceive`):

| Filter | Condition | Allowed opcodes |
|---|---|---|
| `WorldSessionFilter` | `connection.Character == null` | `CMSG_CHARACTER_LIST`, `CMSG_CHARACTER_CREATE`, `CMSG_CHARACTER_DELETE`, `CMSG_CHARACTER_SELECTED`, `CMSG_PONG` (always) |
| `MapSessionFilter` | `connection.Character != null && character.Map >= 1` | `CMSG_MOVEMENT`, `CMSG_ATTACK`, `CMSG_CHARACTER_RUN_WALK`, `CMSG_ENTER_MAP`, `CMSG_CHAT_MESSAGE` |

**A new game packet must be added to the correct filter** — without this, `OnReceive` will route it to `Server.CallListener` instead of the tick-thread queue.

---

## Step-by-step scaffold

### Step 1 — Add the opcode to `NetworkPacketType`

File: `src/Shared/Avalon.Network.Packets.Abstractions/NetworkPacketType.cs`

Add to the correct section (Client Packets or Server Packets) — keep hex values ordered and add a comment for the logical group if it's new.

### Step 2 — Define the packet contract

File: `src/Shared/Avalon.Network.Packets/<Group>/<PacketName>.cs`

Choose an existing group folder (Auth, Character, Combat, Social, Map, etc.) or create a new one if none fits.

Template for a **client packet** (received by server):
```csharp
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.<Group>;

[ProtoContract]
[Packet(HandleOn = ComponentType.<Auth|World>, Type = NetworkPacketType.<CMSG_...>)]
public class <PacketName> : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.<CMSG_...>;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public <Type> <Field> { get; set; }
    // ... additional fields
}
```

Template for a **server packet** (sent by server — needs a static `Create` factory):
```csharp
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.<Group>;

[ProtoContract]
[Packet(HandleOn = ComponentType.<Auth|World>, Type = NetworkPacketType.<SMSG_...>)]
public class <PacketName> : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.<SMSG_...>;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public <Type> <Field> { get; set; }

    // Built in the thread's scratch message and encoded, plain, into a pooled segment before it returns; the
    // connection's outbox seals it (Flags) as it frames it (#875). Assign collections, never add to one found there.
    public static OutboundPacket Create(<params>, PacketEncoder encoder)
    {
        <PacketName> message = PacketEncoder.Scratch<<PacketName>>();
        message.<Field> = <param>;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
```

### Step 3 — Implement the handler

#### For Auth (`Avalon.Server.Auth`)

File: `src/Server/Avalon.Server.Auth/Handlers/<HandlerName>.cs`

- Implement `IAuthPacketHandler<TPacket>` (extends `IPacketHandlerNew`)
- Inject dependencies via constructor — use `ILoggerFactory` and call `loggerFactory.CreateLogger<T>()`
- **No manual DI registration needed** — handlers are discovered automatically by `PacketManager` via reflection scan of all `IPacketHandlerNew` implementations across loaded assemblies

```csharp
using Avalon.Network.Packets.<Group>;

namespace Avalon.Server.Auth.Handlers;

public class <HandlerName> : IAuthPacketHandler<<PacketName>>
{
    private readonly ILogger<<HandlerName>> _logger;
    // inject other dependencies

    public <HandlerName>(ILoggerFactory loggerFactory /*, other deps */)
    {
        _logger = loggerFactory.CreateLogger<<HandlerName>>();
    }

    public async Task ExecuteAsync(AuthPacketContext<<PacketName>> ctx, CancellationToken token = default)
    {
        // implementation
        // Send response: ctx.Connection.Send(S<ResponsePacket>.Create(..., PacketEncoder.Shared));
    }
}
```

#### For World — connection setup packets (receive-thread path)

File: `src/Server/Avalon.Server.World/Handlers/<HandlerName>.cs`

Use this for packets that bypass both session filters (e.g. handshake, world key exchange). Executes on the network receive thread — do not touch game state.

- Implement `IWorldPacketHandler<TPacket>`
- **No manual DI registration or attribute needed** — discovered automatically by `PacketManager`

```csharp
using Avalon.Network.Packets.<Group>;

namespace Avalon.Server.World.Handlers;

public class <HandlerName> : IWorldPacketHandler<<PacketName>>
{
    private readonly ILogger<<HandlerName>> _logger;

    public <HandlerName>(ILogger<<HandlerName>> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(WorldPacketContext<<PacketName>> ctx, CancellationToken token = default)
    {
        // implementation — safe to use async/await freely here
    }
}
```

#### For World — game state packets (tick-thread path)

This is the path for all gameplay packets. Two things are required:

**A) Add the opcode to the correct filter.**

For packets valid before character selection (no character loaded yet), add to `WorldSessionFilter`:
File: `src/Server/Avalon.World/Filters/WorldSessionFilter.cs`
```csharp
NetworkPacketType.<CMSG_...> => true,
```

For packets valid while in a map (character loaded and `character.Map >= 1`), add to `MapSessionFilter`:
File: `src/Server/Avalon.World/Filters/MapSessionFilter.cs`
```csharp
NetworkPacketType.<CMSG_...> => true,
```

**B) Implement the handler.**

File: `src/Server/Avalon.World/Handlers/<HandlerName>.cs`

- Extend `WorldPacketHandler<TPacket>` — `Execute` is synchronous and runs on the tick thread
- Decorate with `[PacketHandler(NetworkPacketType.<CMSG_...>)]`
- **No DI registration** — discovered by `WorldServer` constructor via reflection scan of `typeof(WorldServer).Assembly.GetTypes()`, instantiated via `ActivatorUtilities.CreateInstance`
- If constructor needs `IWorldServer`, pass it through — `WorldServer` detects this and passes `this` to avoid circular resolution
- **Do not block or long-await** inside `Execute`

```csharp
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.<Group>;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.<CMSG_...>)]
public class <HandlerName>(<InjectedService> service) : WorldPacketHandler<<PacketName>>
{
    public override void Execute(IWorldConnection connection, <PacketName> packet)
    {
        if (!connection.InGame) return; // guard if needed

        // Safe to access connection.Character, MapInstance, etc. here
        // All game state mutations are safe — we're on the tick thread
    }
}
```

**Async work from a tick-thread handler:**

When you need a DB query or other async operation and must act on the result inside the tick thread, use `EnqueueContinuation`. The callback is invoked on the next tick after the task completes — still on the tick thread, safe for game state:

```csharp
public override void Execute(IWorldConnection connection, <PacketName> packet)
{
    // Start async work — do not await
    Task<Foo> task = _repository.FindAsync(packet.Id);

    // Callback fires on the tick thread when task completes
    connection.EnqueueContinuation(task, result =>
    {
        if (result == null) { connection.Close(); return; }
        // mutate game state safely here
        connection.Send(S<ResponsePacket>.Create(result, PacketEncoder.Shared));
    });
}
```

For a `Task` (no return value), use the non-generic overload:
```csharp
connection.EnqueueContinuation(someTask, () => { /* runs on tick thread */ });
```

### Step 4 — Write the test

File: `tests/Avalon.Server.<Auth|World>.UnitTests/Handlers/<HandlerName>Should.cs`

- Use **xUnit** + **NSubstitute**
- Construct handler directly with `new <HandlerName>(NullLoggerFactory.Instance, ...)` — no DI container in tests
- Substitute all external dependencies
- Follow the naming convention: `Should_<verb>_<condition>`

```csharp
using Avalon.Server.<Auth|World>.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.<Auth|World>.UnitTests.Handlers;

public class <HandlerName>Should
{
    private readonly I<Dep> _dep = Substitute.For<I<Dep>>();
    private readonly I<Auth|World>Connection _connection = Substitute.For<I<Auth|World>Connection>();
    // A hand-written fake, not a substitute: NSubstitute cannot stub the session's span-based methods. A handler
    // never seals (the outbox does, as it frames the packet, #875), so a test reads the sent packet's plain payload.
    // World handlers: Avalon.World.Testing.Fakes; auth handlers: the Auth tests' own FakeAvalonCryptoSession.
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();
    private readonly <HandlerName> _handler;

    public <HandlerName>Should()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _handler = new <HandlerName>(NullLoggerFactory.Instance, _dep);
    }

    [Fact]
    public async Task Should_<verb>_<condition>()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

---

After generating all files, remind the user to run:
```bash
dotnet build --no-restore
dotnet test tests/Avalon.Server.<Auth|World>.UnitTests
```
