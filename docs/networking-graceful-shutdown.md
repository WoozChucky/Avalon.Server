# Networking — Graceful Shutdown

This document describes the intended connection lifecycle and graceful-shutdown protocol for the Avalon TCP servers (Auth and World).

## Architecture Note — Two Server Code Paths

There are **two separate TCP server code paths** in this codebase. Understanding the distinction is critical when working on networking features:

### Production path (Auth + World servers)
- **`ServerBase<T>`** (`src/Server/Avalon.Hosting/Networking/ServerBase.cs`) — extends `BackgroundService`, uses `TcpListener` + `BeginAcceptTcpClient`.
- **`AuthServer : ServerBase<AuthConnection>`** — the live auth server.
- **`WorldServer : ServerBase<WorldConnection>`** — the live world server.
- Connections implement **`IConnection`** (in `Avalon.Hosting.Networking`), which exposes:
  - `void Send(NetworkPacket)` — synchronous send via ring buffer.
  - `void Close(bool expected = true)` — terminate the connection.

### Standalone/client path (`AvalonTcpServer`)
- **`AvalonTcpServer`** / **`AvalonSslTcpServer`** (`src/Shared/Avalon.Network.Tcp/`) — `netstandard2.1` standalone server with its own `Socket` and `InternalServerLoop`.
- **Never instantiated** in the auth or world server projects. Only referenced by name in `LayerEnricher` log-filtering helpers.
- Used by the development `AvalonTcpClient` test harness.
- Connections implement **`IAvalonTcpConnection`** (in `Avalon.Network`), which exposes `Task SendAsync(NetworkPacket)`.

> **Rule of thumb**: if you are implementing a feature for the running game servers, edit `ServerBase<T>`, `AuthServer`, or `WorldServer`. Changes to `AvalonTcpServer` do **not** affect live server behaviour.

## Current State

- ✅ **TODO-001**: `AvalonTcpServer` now tracks connections in a `ConcurrentDictionary` and closes them all in `StopAsync`.
- ✅ **TODO-002**: `AvalonTcpServer.StopAsync` sends `SDisconnectPacket` (ServerShutdown) to each connection before closing. Also, `ServerBase.StopAsync` now calls `Listener.Stop()` first to release the listening port, and `OnClientAccepted` is hardened against shutdown races.
- ✅ **TODO-003**: `AuthServer.OnStoppingAsync` now sends `SDisconnectPacket` before calling `connection.Close()`.
- ✅ **TODO-004**: `WorldServer.OnStoppingAsync` now sends `SDisconnectPacket` before calling `connection.Close()`.
- ❌ **TODO-005**: `DelayedDisconnect` (duplicate-login kick) — still pending; see section below.

Related TODOs: [TODO-001](../TODO.md#todo-001), [TODO-002](../TODO.md#todo-002), [TODO-003](../TODO.md#todo-003), [TODO-004](../TODO.md#todo-004), [TODO-005](../TODO.md#todo-005)

---

## Connection Lifecycle

```
Client                    AuthServer / WorldServer (ServerBase<T>)
  |                                 |
  |  TCP SYN                        |
  |-------------------------------->|  BeginAcceptTcpClient → OnClientAccepted
  |  [Handshake / CRequestServerInfo] |
  |<-------------------------------> |
  |  [Auth / World handshake flow]  |
  |<-------------------------------> |
  |                                 |
  |  (Server shutdown OR kick)      |
  |       SDisconnectPacket         |
  |<---------------------------------|  connection.Send(SDisconnectPacket)
  |  [client shows reconnect UI]    |
  |  TCP FIN                        |
  |<---------------------------------|  connection.Close()
```

## `SDisconnectPacket` Schema

Defined in `Avalon.Network.Packets.Generic.SDisconnectPacket`. Transmitted **unencrypted** (`NetworkPacketFlags.None`) so it is readable regardless of the crypto session state.

Packet type: `NetworkPacketType.SMSG_DISCONNECT = 0x3008`

| Field        | Type             | Description                               |
|--------------|------------------|-------------------------------------------|
| `Reason`     | `string`         | Human-readable reason shown to the player |
| `ReasonCode` | `DisconnectReason` | Machine-readable disconnect reason      |

### Disconnect Reason Codes (`DisconnectReason` enum)

| Value | Name             | Trigger                                                        |
|-------|------------------|----------------------------------------------------------------|
| 0     | `Unknown`        | Default/unspecified                                            |
| 1     | `ServerShutdown` | Server stopping gracefully (TODO-001–004)                      |
| 2     | `DuplicateLogin` | Second authentication for the same account (TODO-005)          |
| 3     | `Kicked`         | Manual admin kick (future)                                     |

### Factory method

```csharp
NetworkPacket packet = SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown);
```

---

## `AvalonTcpServer` Changes — DONE (TODO-001 + TODO-002)

> **Note**: `AvalonTcpServer` is **not** the production server for Auth or World. See the architecture note above.

### Connection tracking (TODO-001)

`AvalonTcpServer` tracks connections in `ConcurrentDictionary<Guid, IAvalonTcpConnection>`. `AvalonSslTcpServer.HandleNewConnection` calls `TrackConnection()` after accepting a client. The connection is removed when its `Disconnected` event fires.

### `StopAsync` sequence (TODO-002)

```
1. For each tracked connection:
   a. Send SDisconnectPacket(ServerShutdown)
   b. Await 200 ms drain
   c. Call connection.Close() — regardless of send success
2. Cts.Cancel() — stops the accept loop
3. Socket.Close() / Socket.Dispose()
4. Log "Server stopped"
```

Per-connection exceptions are caught and logged individually; they do not abort the shutdown of remaining connections.

---

## Auth Server Changes — DONE (TODO-003)

`AuthServer.OnStoppingAsync` iterates `Connections` and sends a disconnect packet before closing:

```csharp
foreach (IAuthConnection connection in Connections)
{
    connection.Send(SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown));
    connection.Close();
}
```

`IConnection.Send` is **synchronous** (ring buffer enqueue). No drain delay is needed here because `connection.Close()` flushes the send queue before terminating the TCP connection.

---

## World Server Changes — DONE (TODO-004) / Pending (TODO-005)

### Graceful stop (TODO-004) — DONE

Same pattern as Auth in `WorldServer.OnStoppingAsync`.

### Forced kick notification (TODO-005) — Pending

`DelayedDisconnect` is called via Redis pub/sub when a duplicate login is detected by the Auth server. The player is in-game and should see a UI reason.

```csharp
private void DelayedDisconnect(RedisChannel channel, RedisValue value)
{
    AccountId accountId = value.ToString();
    var connection = Connections.FirstOrDefault(c => c.AccountId == accountId);
    if (connection is null) return;

    connection.Send(SDisconnectPacket.Create(DisconnectReason.DuplicateLogin,
        "Your account has been logged in from another location."));
    connection.Close();
}
```

---

## Graceful Shutdown Helper (Optional Extraction)

If the three servers share the same shutdown shape, extract:

```csharp
public static class GracefulShutdownHelper
{
    public static void ShutdownAll<TConnection>(
        IEnumerable<TConnection> connections,
        Func<TConnection, NetworkPacket> disconnectPacketFactory,
        ILogger logger)
        where TConnection : IConnection
    {
        foreach (var conn in connections)
        {
            try { conn.Send(disconnectPacketFactory(conn)); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to send disconnect packet"); }
            try { conn.Close(); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to close connection"); }
        }
    }
}
```

---

## Test Strategy

| Scenario                             | Expected                                         |
|--------------------------------------|--------------------------------------------------|
| `StopAsync` with 3 connections       | All 3 receive disconnect packet then are closed  |
| One `Send` throws                    | Other 2 still closed; exception logged           |
| `StopAsync` with 0 connections       | No-op; no exceptions                             |
| `DelayedDisconnect` matching account | Kick packet sent; `Close()` called               |
| `DelayedDisconnect` no account found | No-op; no exception                              |
