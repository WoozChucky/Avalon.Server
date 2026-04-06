# Redis Cache Keys Reference

This document is the authoritative reference for every Redis key and pub/sub channel used across the Avalon services.
All string literals are centralized in `CacheKeys` (`src/Server/Avalon.Infrastructure/CacheKeys.cs`). Any rename or
addition must start there.

---

## Overview

Avalon uses Redis for three distinct purposes:

| Purpose | Mechanism | Examples |
|---|---|---|
| Ephemeral session tokens | `SET` / `GET` / `DEL` with TTL | World entry keys, session mutex |
| Cross-component presence signalling | Pub/Sub channels | disconnect request, account online event, world-select notification |
| Short-lived structured state | Redis Hash with per-field access | MFA flow (hash + expiry + accountId) |

---

## String Keys

String keys map a single value to a single Redis string. All carry a TTL to prevent stale state accumulation.

### `world:{worldId}:keys:{worldKeyBase64}`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldKey(ushort worldId, string worldKeyBase64)` |
| **Type** | String |
| **Owner (writer)** | Auth server — `CWorldSelectHandler` |
| **Consumer (reader/deleter)** | World server — `ExchangeWorldKeyHandler` |
| **Value** | Account ID as a decimal string |
| **TTL** | 5 minutes |

**Purpose:** One-time handoff token issued at world selection. The Auth server writes the key immediately after
verifying access; the World server looks it up once when the client presents it during the crypto-key exchange phase,
then deletes it. The key is single-use: a successful exchange removes it, preventing replay.

**Flow:**
```
Auth server (CWorldSelectHandler)
  → SET world:{worldId}:keys:{base64key}  value=accountId  TTL=5m

Game client connects to World server and sends CExchangeWorldKeyPacket(worldKey, pubKey)

World server (ExchangeWorldKeyHandler)
  → GET world:{worldId}:keys:{base64key}   ← returns accountId or nil
  → DEL world:{worldId}:keys:{base64key}   ← consume / invalidate
```

---

### `account:{accountId}:inWorld`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.AccountInWorld(long accountId)` |
| **Type** | String (mutex pattern) |
| **Owner (writer)** | Auth server — `CWorldSelectHandler` |
| **Consumer (deleter)** | World server — `ExchangeWorldKeyHandler` |
| **Value** | `"1"` (sentinel) |
| **TTL** | 5 minutes |

**Purpose:** Mutual exclusion lock that prevents an account from holding more than one active world-entry attempt
concurrently. Written with `SETNX` (SET if Not eXists): if the key already exists, the second world-select request is
rejected with `DuplicateSession`. The World server removes the key after a successful key exchange, freeing the slot.
The TTL ensures cleanup if the World server fails to process the handshake within the window.

**Flow:**
```
Auth server (CWorldSelectHandler)
  → SETNX account:{accountId}:inWorld  1  TTL=5m
     ← false → reject with DuplicateSession
     ← true  → proceed

World server (ExchangeWorldKeyHandler) — after valid key exchange
  → DEL account:{accountId}:inWorld
```

---

## Hash Keys

Redis Hashes allow multiple named fields under a single key. Avalon uses this for MFA state to keep related
fields atomic and inspectable.

### `auth:account:{accountId}:mfa`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.AccountMfa(long accountId)` |
| **Type** | Hash |
| **Owner (writer + reader)** | Auth server — `MFAHashService` |
| **Consumer (deleter)** | Auth server — `MFAHashService.CleanupHash` |
| **TTL** | 2 minutes (set on the whole key via `EXPIRE` after `EXEC`) |

**Hash fields:**

| Field name | Description |
|---|---|
| `hash` | Base32-encoded TOTP seed used as the MFA challenge token |
| `expiry` | ISO-8601 UTC timestamp recording when this entry expires |
| `accountId` | String representation of the account ID (used for reverse lookup) |

**Purpose:** Stores ephemeral MFA state during the two-factor login window. When a password check succeeds for an
account with MFA enabled, `MFAHashService` generates (or returns an unexpired existing) hash and writes all three
fields atomically inside a Redis transaction. The client must submit the hash back with a valid TOTP code within the
TTL window. After verification (or timeout), `CleanupHash` removes the entry.

> **Scan pattern** for locating all active MFA entries: `CacheKeys.AccountMfaGlobPattern` = `"auth:account:*:mfa"`.
> Note: uses the Redis `KEYS` command — only acceptable at this scale; replace with `SCAN` if key cardinality grows.

---

## Pub/Sub Channels

Pub/Sub channels carry event notifications between services. There is no persistence — a message is only received by
subscribers active at the time of publish.

### `world:accounts:disconnect`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldAccountsDisconnectChannel` |
| **Publisher** | Auth server — `CAuthHandler` (duplicate login path) |
| **Subscriber** | World server — `WorldServer.CacheSubscribeAsync` |
| **Message format** | Account ID as a decimal string |

**Purpose:** Signals all World server instances that a previously authenticated account has logged in again from a
different connection. The World server searches its active connections for a matching `AccountId` and closes it
gracefully (sends `SDisconnectPacket` with reason `DuplicateLogin` before closing the socket). This is the cross-server
half of the duplicate-login guard; the Auth server handles the Auth-side connection directly.

**Flow:**
```
Auth server (CAuthHandler) — on successful login where account.Online == true
  → PUBLISH world:accounts:disconnect  {accountId}

World server (WorldServer, DelayedDisconnect)
  ← message received
  → find IWorldConnection where AccountId == accountId
  → GracefulShutdownHelper.NotifyAndClose(connection, "Your account has been logged in from another location.", DuplicateLogin)
```

---

### `auth:accounts:online`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.AuthAccountsOnlineChannel` |
| **Publisher** | Auth server — `CAuthHandler` (successful login path) |
| **Subscriber** | *(currently none — reserved for future components)* |
| **Message format** | Account ID as a decimal string |

**Purpose:** Broadcasts that an account has completed authentication and is now marked online. Currently published but
not subscribed to by any component; reserved for future use cases such as an admin dashboard, presence service, or
API-layer invalidation of cached account state.

---

### `world:{worldId}:select`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldSelectChannel(ushort worldId)` |
| **Publisher** | Auth server — `CWorldSelectHandler` |
| **Subscriber** | *(currently none — reserved for future cross-shard coordination)* |
| **Message format** | `account:{accountId}:worldKey:{worldKeyBase64}` |

**Purpose:** Notifies the target world shard that an account has been issued a world entry key and is about to connect.
The message embeds both the account identity and the key so a receiving shard could pre-warm its connection state.
Currently published but not consumed; designed for future multi-instance world coordination where a load balancer or
shard registry needs to know which shard should expect the arriving client.

---

## Key Inventory Summary

| Key pattern | Type | TTL | Writer | Consumer |
|---|---|---|---|---|
| `world:{id}:keys:{base64}` | String | 5 min | Auth / `CWorldSelectHandler` | World / `ExchangeWorldKeyHandler` |
| `account:{id}:inWorld` | String | 5 min | Auth / `CWorldSelectHandler` | World / `ExchangeWorldKeyHandler` |
| `auth:account:{id}:mfa` | Hash | 2 min | Auth / `MFAHashService` | Auth / `MFAHashService` |
| `world:accounts:disconnect` | Pub/Sub channel | — | Auth / `CAuthHandler` | World / `WorldServer` |
| `auth:accounts:online` | Pub/Sub channel | — | Auth / `CAuthHandler` | *(reserved)* |
| `world:{id}:select` | Pub/Sub channel | — | Auth / `CWorldSelectHandler` | *(reserved)* |

---

## Adding a New Key

1. Add a `const` or `static` method to `CacheKeys` in `src/Server/Avalon.Infrastructure/CacheKeys.cs`.
2. Document it in this file under the appropriate section (String Key, Hash Key, or Pub/Sub Channel).
3. Update the summary table above.
4. If the key is used across more than one service, document the publish/subscribe or write/read split clearly.
