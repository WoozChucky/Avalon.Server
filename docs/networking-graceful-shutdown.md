# Networking — Graceful Shutdown

This document describes the connection lifecycle and graceful-shutdown protocol for the Avalon TCP servers (Auth and World).

## Server code path

Both TCP servers run on one code path:

- **`ServerBase<T>`** (`src/Server/Avalon.Hosting/Networking/ServerBase.cs`) — extends `BackgroundService`; one awaited
  `AcceptTcpClientAsync` loop, started at host start, or for the world server by `StartListening` once the world is
  ready (#665). Only a stop ends it.
- **`AuthServer : ServerBase<AuthConnection>`** — the auth server.
- **`WorldServer : ServerBase<WorldConnection>`** (`src/Server/Avalon.World/WorldServer.cs`) — the world server.
- Connections implement **`IConnection`** (in `Avalon.Hosting.Networking`), which exposes:
  - `void Send(NetworkPacket)` — a synchronous enqueue onto the connection's bounded outbox (`ChannelOutbox`; a world
    connection's is a `TickDrivenOutbox`), at most `Hosting:SendBufferCapacity` packets, the oldest dropped when full.
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
| 3     | `Kicked`         | The account's sessions were ended (`world:accounts:disconnect`: a ban, a credentials or role change, a duplicate login on the auth server, a refresh-token reuse) |
| 4     | `SelectTimeout`  | A character select that never completed                        |
| 5     | `CharacterSaveFailed` | A Change Character whose logout save failed (#663)        |
| 6     | `Maintenance`    | A maintenance cutoff, or an entry refused by it                |

### Factory method

```csharp
NetworkPacket packet = SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown);
```

---

## Auth Server Shutdown

`AuthServer.OnStoppingAsync` unsubscribes from the account disconnect channel, then closes every connection at once
with `GracefulShutdownHelper.NotifyAndCloseAsync` (`ServerShutdown`) and awaits them all, since the notice is delivered
by the close.

---

## World Server Shutdown

### Graceful stop

`WorldServer.OnStoppingAsync` first runs the restart drain (`WorldMaintenanceCoordinator.DrainForRestartAsync`, #768,
see [world maintenance](world-maintenance.md)), then closes every connection with
`GracefulShutdownHelper.NotifyAndCloseAsync` (`ServerShutdown`), despawns and saves.

### Forced kick notification

`DelayedDisconnect` runs when an account is published on `world:accounts:disconnect` (a duplicate login on the auth
server, a ban, a password, email or role change, an MFA reset or removal, a refresh-token reuse). It calls
`WorldServer.CloseAccountSessions`, which closes **every** connection of that account, each with
`SDisconnectPacket(Kicked)` and the neutral "Your session has ended. Please log in again.", since the message is the
bare account id and cannot say why; one connection that throws while closing is logged and the rest are still closed.
Only the world server's own duplicate-login kick at character select uses `DuplicateLogin` and "Your account has been
logged in from another location.".

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
| Account disconnect, account connected | Every connection of the account sent the kick packet and closed |
| Account disconnect, no such account  | No-op; no exception                              |
