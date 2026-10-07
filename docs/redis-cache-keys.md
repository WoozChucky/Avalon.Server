# Redis Cache Keys Reference

This document is the reference for the Redis keys and pub/sub channels used across the Avalon services. Most literals
are centralized in `CacheKeys` (`src/Server/Avalon.Infrastructure/CacheKeys.cs`), and any rename or addition should
start there. A few are still written where they are used: the game-context revocation channel
(`GameContextRevocations.Channel`, `world:game-context:revoke`), commerce's checkout budget
(`CheckoutBudget`, `commerce:{environment}:checkout:*`), identity's email-verification send budgets
(`AccountEmailVerificationService`, `email-verification:account:{accountId}` and `email-verification:source:{source}`)
and the World server's delete of `account:{accountId}:inWorld`.

---

## Overview

Avalon uses Redis for three distinct purposes:

| Purpose | Mechanism | Examples |
|---|---|---|
| Ephemeral session tokens | `SET` / `GET` / `DEL` with TTL | World entry keys, session mutex |
| Cross-component presence signalling | Pub/Sub channels | disconnect request, account online event, world-select notification |
| Short-lived structured state | Redis Hash with per-field access | MFA flow (hash + expiry + accountId) |

---

## Keys by API service

The REST API runs as four services ([API services](api-services.md)), each using only its own keys; a process
connects to Redis only when one of its services needs it. While production runs all four in one process (#802), that
process uses all of them.

| Service | Keys it reads and writes | Channels |
|---|---|---|
| identity | the login and registration budgets (`auth:source:{source}:failedLogins`, `auth:username:{sha256}:failedLogins`, `auth:source:{source}:accountsCreated`), MFA state (`auth:account:{accountId}:mfa`, `auth:mfa:hash:{hash}`), email change (`auth:emailChange:{sha256}`, `auth:account:{accountId}:emailChangePending`, `auth:account:{accountId}:emailChangeSends`, `auth:email:{sha256}:emailChangeSends`), the email-verification send budgets (`email-verification:*`), launcher codes (`auth:launcherCode:{hash}`), game tickets (`auth:gameTicket:{hash}`, `auth:gameTicketIssue:{familyId}`), the game-auth records (`game-auth:{environment}:*`); reads `world:{worldId}:ready` | publishes `world:accounts:disconnect` and `world:game-context:revoke` |
| worlds | reads `world:{worldId}:ready`, `world:{worldId}:presence`, `presence:world:{worldId}:character:{characterId}` and `world:{worldId}:scripts` | publishes `world:{worldId}:maintenance` and `world:{worldId}:reload`; subscribes to `world:{worldId}:reload:result` |
| commerce | the checkout budget (`commerce:{environment}:checkout:*`) | none |
| distribution | none: it runs without `Application:Cache` and holds no Redis password | none |

The login budgets and the MFA keys are shared with the TCP auth server, which runs the same login policy, so a guess
over REST and one over TCP spend one budget ([REST API authentication](api-authentication.md)).

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

> **Retired.** No server writes or reads this key since the TLS join-ticket admission replaced the TCP world-key
> handoff (`CWorldSelectHandler` and `ExchangeWorldKeyHandler` are gone; `CacheKeys.WorldKey` has no caller). The
> description below is the old flow.

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

> **Retired with the world key above.** Nothing sets it any more; the World server still deletes the literal key when
> a connection closes (`WorldServer.ClearInWorldFlagAsync`). The description below is the old flow.

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
| **Owner (writer + reader)** | Auth server and the API's identity service — `MFAHashService` |
| **Consumer (deleter)** | Auth server and the API's identity service — `MFAHashService.CleanupHash` |
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
| **Publisher** | Auth server — `GameLoginCompletion` (duplicate login path); the API's identity service on a credentials change, a ban or refresh-token reuse (`AccountService`, `AccountRefreshController`, `ClientAuthController`); `MFAService` on an MFA reset, on either server |
| **Subscriber** | World server — `WorldServer.CacheSubscribeAsync`; Auth server — `AuthServer` |
| **Message format** | Account ID as a decimal string |

**Purpose:** Asks every server to drop the account's sessions. The original use is the cross-server half of the
duplicate-login guard: an account that logs in again from a different connection. The World server searches its
active connections for a matching `AccountId` and closes them; the Auth server closes that account's logged-in TCP
connections. The message is the bare account id, so it cannot say why.

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
| **Publisher** | Auth server — `GameLoginCompletion` (successful login path) |
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
| **Publisher** | None since the TCP world select was retired (`CWorldSelectHandler` is gone); the description below is the old one |
| **Subscriber** | *(currently none — reserved for future cross-shard coordination)* |
| **Message format** | `account:{accountId}:worldKey:{worldKeyBase64}` |

**Purpose:** Notifies the target world shard that an account has been issued a world entry key and is about to connect.
The message embeds both the account identity and the key so a receiving shard could pre-warm its connection state.
Currently published but not consumed; designed for future multi-instance world coordination where a load balancer or
shard registry needs to know which shard should expect the arriving client.

### `auth:launcherCode:{codeHash}`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.LauncherAuthCode(string codeHash)` |
| **Type** | String (one-time token) |
| **Owner (writer)** | API, identity: `LauncherAuthCodes.IssueAsync` (`POST client/auth/code`) |
| **Consumer (deleter)** | API, identity: `LauncherAuthCodes.RedeemAsync` (`POST client/auth/token`) |
| **Value** | `accountId|credentialsVersion|challenge|redirectPort` |
| **TTL** | 60 seconds |

**Purpose:** Launcher sign-in (#591, RFC 8252 with PKCE).
- The signed-in website asks for a code bound to the launcher's PKCE challenge and loopback port, and hands it to the launcher.
- The key is the SHA-256 (hex) of the code, so reading the cache yields no usable code.
- Redeeming is `GET` then `DEL`, and only the call whose `DEL` removed the key goes on. Two racing exchanges get one grant, and a wrong verifier spends the code too.
- The exchange is refused if the account's credentials version has moved since the code was issued.

---

## Key Inventory Summary

| Key pattern | Type | TTL | Writer | Consumer |
|---|---|---|---|---|
| `world:{id}:keys:{base64}` | String | 5 min | *(retired)* | *(retired)* |
| `account:{id}:inWorld` | String | 5 min | *(retired)* | World deletes it on close |
| `auth:account:{id}:mfa` | Hash | 2 min | Auth, API identity / `MFAHashService` | Auth, API identity / `MFAHashService` |
| `auth:launcherCode:{hash}` | String | 60 s | API identity / `LauncherAuthCodes` | API identity / `LauncherAuthCodes` |
| `world:accounts:disconnect` | Pub/Sub channel | — | Auth / `GameLoginCompletion`; API identity; `MFAService` | World / `WorldServer`; Auth / `AuthServer` |
| `auth:accounts:online` | Pub/Sub channel | — | Auth / `GameLoginCompletion` | *(reserved)* |
| `world:{id}:select` | Pub/Sub channel | — | *(none)* | *(reserved)* |

---

## Adding a New Key

1. Add a `const` or `static` method to `CacheKeys` in `src/Server/Avalon.Infrastructure/CacheKeys.cs`.
2. Document it in this file under the appropriate section (String Key, Hash Key, or Pub/Sub Channel).
3. Update the summary table above.
4. If the key is used across more than one service, document the publish/subscribe or write/read split clearly.
