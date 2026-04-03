# Networking — Graceful Shutdown

This document describes the intended connection lifecycle and graceful-shutdown protocol for the Avalon TCP servers (Auth and World).

## Current State

Both `AuthServer` and `WorldServer` iterate their connection lists on stop and call `connection.Close()` directly. There is no application-level packet signalling clients about the shutdown, and `AvalonTcpServer.StopAsync` does not close accepted sockets at all.

Related TODOs: [TODO-001](../TODO.md#todo-001), [TODO-002](../TODO.md#todo-002), [TODO-003](../TODO.md#todo-003), [TODO-004](../TODO.md#todo-004), [TODO-005](../TODO.md#todo-005)

---

## Connection Lifecycle

```
Client                    AvalonTcpServer / AuthServer / WorldServer
  |                                 |
  |  TCP SYN                        |
  |-------------------------------->|  AcceptAsync → HandleNewConnection
  |  [Handshake / CRequestServerInfo] |
  |<-------------------------------> |
  |  [Auth / World handshake flow]  |
  |<-------------------------------> |
  |                                 |
  |  (Server shutdown OR kick)      |
  |       SDisconnectPacket         |
  |<---------------------------------|
  |  [client shows reconnect UI]    |
  |  TCP FIN                        |
  |<---------------------------------|  connection.Close()
```

## `SDisconnectPacket` Schema

To be defined in `Avalon.Network.Packets`:

| Field         | Type     | Description                              |
|---------------|----------|------------------------------------------|
| `ReasonCode`  | `ushort` | Machine-readable disconnect reason       |
| `Message`     | `string` | Human-readable reason shown to the player|

### Disconnect Reason Codes

| Code | Name               | Trigger                                                        |
|------|--------------------|----------------------------------------------------------------|
| 0    | `ServerShutdown`   | Server stopping gracefully (TODO-002, TODO-003, TODO-004)      |
| 1    | `DuplicateLogin`   | Second authentication for the same account (TODO-005)          |
| 2    | `AdminKick`        | Manual admin kick (future)                                     |
| 3    | `VersionMismatch`  | Client version rejected (related to TODO-010)                  |

---

## `AvalonTcpServer` Changes (TODO-001 + TODO-002)

### Connection tracking

Add a `ConcurrentDictionary<Guid, IAvalonTcpConnection>` to `AvalonTcpServer`:

- **Populate** inside `HandleNewConnection` after accepting the client.
- **Remove** inside the connection's `OnClosed` callback.

### `StopAsync` sequence

```
1. Set Running = false
2. Cancel Cts (stops the accept loop)
3. For each tracked connection:
   a. Send SDisconnectPacket(ServerShutdown)
   b. Await a short flush delay (200 ms max)
   c. Call connection.Close() — regardless of send success
4. Socket.Close() / Socket.Dispose()
5. Log "Server stopped"
```

Per-connection exceptions must be caught and logged individually; they must not abort the shutdown of remaining connections.

---

## Auth Server Changes (TODO-003)

`AuthServer.OnStoppingAsync` already iterates `Connections`. Add the send step before `Close`:

```csharp
foreach (IAuthConnection connection in Connections)
{
    connection.Send(SDisconnectPacket.Create(DisconnectReason.ServerShutdown, "Server is shutting down"));
    connection.Close();
}
```

---

## World Server Changes (TODO-004 + TODO-005)

### Graceful stop (TODO-004)

Same pattern in `WorldServer.OnStoppingAsync`.

### Forced kick notification (TODO-005)

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
