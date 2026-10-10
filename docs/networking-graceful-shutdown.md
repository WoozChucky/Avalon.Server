# Networking — Graceful Shutdown

This document describes the connection lifecycle and graceful-shutdown protocol for the Avalon TCP servers (Auth and World).

## Server code path

Both TCP servers run on one code path:

- **`ServerBase<T>`** (`src/Server/Avalon.Hosting/Networking/ServerBase.cs`) — extends `BackgroundService`; one awaited
  `AcceptTcpClientAsync` loop, started by each server's `ExecuteAsync` through `StartListening` once the server is
  ready: the world server once the world is loaded (#665), the auth server once its certificate, start-up reset,
  disconnect subscription and connection listener are (#867). Only a stop ends it. `BoundEndPoint` is the endpoint the
  listener bound, null until it has: with `Hosting:Port` 0 the OS picks a free port, and tests read it back from there
  rather than reserving one and releasing it before the server binds it, which another process could take in between
  (#841).
- **`AuthServer : ServerBase<AuthConnection>`** — the auth server.
- **`WorldServer : ServerBase<WorldConnection>`** (`src/Server/Avalon.World/WorldServer.cs`) — the world server.
- Connections implement **`IConnection`** (in `Avalon.Hosting.Networking`), which exposes:
  - `void Send(OutboundPacket)` — takes the packet's payload reference and queues it: on the auth server onto its
    `ChannelOutbox` (at most `Hosting:SendBufferCapacity`, oldest dropped); on the world server onto the connection's
    `ConnectionSender`, written by its send thread, never dropped (past `Network:MaxPendingBytes` the connection is
    closed with `SlowConnection`, #875). A packet the outbox refuses is released at once.
  - `void Close(bool expected = true)` and `Task CloseAsync(bool expected = true)` — terminate the connection.

The standalone TCP server library that once sat beside it (`Avalon.Network.Tcp`, with its development test client) is
gone.

---

## Connection Lifecycle

```
Client                    AuthServer / WorldServer (ServerBase<T>)
  |                                 |
  |  TCP SYN                        |
  |-------------------------------->|  AcceptTcpClientAsync loop accepts it
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

---

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
| 1     | `ServerShutdown` | Server stopping gracefully                                     |
| 2     | `DuplicateLogin` | Second authentication for the same account                     |
| 3     | `Kicked`         | The account's sessions were ended (`world:accounts:disconnect`: a credentials or role change, a duplicate login on the auth server, a refresh-token reuse) |
| 4     | `SelectTimeout`  | A character select that never completed                        |
| 5     | `CharacterSaveFailed` | A Change Character whose logout save failed (#663)        |
| 6     | `Maintenance`    | A maintenance cutoff, or an entry refused by it                |
| 7     | `Banned`         | The account was banned (`world:accounts:status`, #882)         |
| 8     | `Deactivated`    | The account was deactivated (`world:accounts:status`, #882)    |
| 9     | `SlowConnection` | The connection could not keep up: past `Network:MaxPendingBytes` queued (#875). Its only packet, before the close, when no write is still in flight; one found past the cap while a write is in flight, or a write stalled past `Network:MaxWriteStall`, closes without it |

### Factory method

```csharp
OutboundPacket packet = SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown,
    PacketEncoder.Shared);
```

---

## Auth Server Shutdown

`AuthServer.OnStoppingAsync` unsubscribes from the account disconnect channel, then closes every connection at once
with `GracefulShutdownHelper.NotifyAndCloseAsync` (`ServerShutdown`) and awaits them all, since the notice is delivered
by the close.

---

## World Server Shutdown

### Graceful stop

`WorldServer.OnStoppingAsync` runs, in order:

1. Stop accepting: the listener is already stopped when it starts.
2. The restart drain (`WorldMaintenanceCoordinator.DrainForRestartAsync`, #768, see
   [world maintenance](world-maintenance.md)), while the tick still runs.
3. Stop the tick and join its thread (5 s).
4. `GracefulShutdownHelper.NotifyAndCloseAsync` (`ServerShutdown`) on every connection, all at once and awaited. The
   tick no longer writes to sockets (#875): each connection's send thread writes what is queued, the notice last, and
   each close waits up to 500 ms (`ConnectionSender.CloseBudget`) for it. A peer that stopped reading cannot hold the
   close open past that budget and its 100 ms grace.
5. The despawns the closes queued, run on this thread, which write the characters back.
6. The wait for character saves still in flight (`SaveDrainLimit`, 20 s, and the host's stop timeout).
7. The send threads stop last: they outlive the tick so that the notices and every reply the despawns sent go out.
   They are background threads, joined within one shared `WorldServer.SendThreadsStopLimit` (2 s); one that does not
   stop in time is logged and does not hold the process up.

### Forced kick notification

`DelayedDisconnect` runs when an account is published on `world:accounts:disconnect` (a duplicate login on the auth
server, a password, email or role change, an MFA reset or removal, a refresh-token reuse). It calls
`WorldServer.CloseAccountSessions`, which closes **every** connection of that account, each with
`SDisconnectPacket(Kicked)` and the neutral "Your session has ended. Please log in again.", since the message is the
bare account id and cannot say why; one connection that throws while closing is logged and the rest are still closed.
Only the world server's own duplicate-login kick at character select uses `DuplicateLogin` and "Your account has been
logged in from another location.". A ban or a deactivation is published on `world:accounts:status` instead (#882), with
the status: `AccountStatusChanged` calls `WorldServer.CloseBannedOrDeactivated`, which closes every connection of the
account the same way, with `Banned` and "Your account has been banned." or `Deactivated` and "Your account has been
deactivated.".

---

## `GracefulShutdownHelper`

`GracefulShutdownHelper.NotifyAndClose` (`src/Server/Avalon.Hosting/Networking/GracefulShutdownHelper.cs`) is the shared utility every kick and shutdown uses; `NotifyAndCloseAsync` does the same and returns once the connection has finished closing:

```csharp
public static void NotifyAndClose(IConnection connection, string reason, DisconnectReason reasonCode, ILogger? logger = null)
```

- Sends `SDisconnectPacket` — exceptions are caught, logged via `logger` (optional), and do **not** abort the close.
- Always calls `connection.Close()` regardless of send success.

Tests: `tests/Avalon.Server.Auth.UnitTests/Networking/GracefulShutdownHelperShould.cs`.

---

## Test Coverage

| Scenario                             | Expected                                         |
|--------------------------------------|--------------------------------------------------|
| `StopAsync` with 3 connections       | All 3 receive disconnect packet then are closed  |
| One `Send` throws                    | Other 2 still closed; exception logged           |
| `StopAsync` with 0 connections       | No-op; no exceptions                             |
| World stop, tick stopped, one connection | Its send thread writes the `ServerShutdown` notice; the send threads stop last (`WorldServerShutdownShould`) |
| Account disconnect, account connected | Every connection of the account sent the kick packet and closed |
| Account disconnect, no such account  | No-op; no exception                              |
