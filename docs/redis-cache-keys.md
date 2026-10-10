# Redis Cache Keys Reference

This document is the reference for the Redis keys and pub/sub channels used across the Avalon services. Most literals
are centralized in `CacheKeys` (`src/Server/Avalon.Infrastructure/CacheKeys.cs`), and any rename or addition should
start there. A few are still written where they are used: the game-context revocation channel
(`GameContextRevocations.Channel`, `world:game-context:revoke`), commerce's checkout budget
(`CheckoutBudget`, `commerce:{environment}:checkout:*`), identity's email-verification send budgets
(`AccountEmailVerificationService`, `email-verification:account:{accountId}` and `email-verification:source:{source}`)
and the World server's delete of the retired `account:{accountId}:inWorld` key (see [Retired keys](#retired-keys)).

The login budgets, the MFA keys and the email-change keys are described rule by rule in the
[auth server](auth-server.md#redis-keys) page.

---

## Overview

Avalon uses Redis for three distinct purposes:

| Purpose | Mechanism | Examples |
|---|---|---|
| Short-lived one-time tokens and budgets | `SET` / `GET` / `DEL` with TTL, `INCR` with an expiry | MFA reverse keys, launcher codes, game tickets, join tickets, login budgets |
| Cross-component signalling | Pub/Sub channels | disconnect request, account online event, maintenance and reload notices |
| Short-lived structured state | Redis Hash with per-field access | MFA flow (hash + expiry + accountId + attempts) |

---

## Keys by API service

The REST API runs as four services ([API services](api-services.md)), each using only its own keys; a process
connects to Redis only when one of its services needs it. While production runs all four in one process (#802), that
process uses all of them.

| Service | Keys it reads and writes | Channels |
|---|---|---|
| identity | the login and registration budgets (`auth:source:{source}:failedLogins`, `auth:username:{sha256}:failedLogins`, `auth:source:{source}:accountsCreated`), MFA state (`auth:account:{accountId}:mfa`, `auth:mfa:hash:{sha256}`), email change (`auth:emailChange:{sha256}`, `auth:account:{accountId}:emailChangePending`, `auth:account:{accountId}:emailChangeSends`, `auth:email:{sha256}:emailChangeSends`), the email-verification send budgets (`email-verification:*`), launcher codes (`auth:launcherCode:{hash}`), game tickets (`auth:gameTicket:{hash}`, `auth:gameTicketIssue:{familyId}`), the game-auth records (`game-auth:{environment}:*`), the Steam web link's transactions and nonces (`steam-web:transaction:{id}`, `steam-web:nonce:{sha256}`); reads `world:{worldId}:ready` | publishes `world:accounts:disconnect`, `world:accounts:status` and `world:game-context:revoke` |
| worlds | reads `world:{worldId}:ready`, `world:{worldId}:presence`, `presence:world:{worldId}:character:{characterId}` and `world:{worldId}:scripts` | publishes `world:{worldId}:maintenance` and `world:{worldId}:reload`; subscribes to `world:{worldId}:reload:result` |
| commerce | the checkout budget (`commerce:{environment}:checkout:*`) | none |
| distribution | none: it runs without `Application:Cache` and holds no Redis password | none |

The login budgets and the MFA keys are shared with the TCP auth server, which runs the same login policy, so a guess
over REST and one over TCP spend one budget ([REST API authentication](api-authentication.md)).

### Redis users per API service

Each API service can sign in as its own Redis ACL user (#803), limited to the keys, channels and commands above
(`Application:Cache:Username`, chart value `cache.username`; see
[Cache Configuration](configuration-reference.md#cache-configuration-cacheconfiguration)). The auth server and the world
servers sign in as the default user, with full access; the balance service uses no Redis. The users the homelab declares:

| User | Keys | Channels | Commands |
|---|---|---|---|
| `api-identity` | read and write `auth:source:*`, `auth:username:*`, `auth:account:*`, `auth:mfa:hash:*`, `auth:emailChange:*`, `auth:email:*`, `auth:launcherCode:*`, `auth:gameTicket:*`, `auth:gameTicketIssue:*`, `email-verification:*`, `game-auth:*`, `steam-web:*`; read `world:*:ready` | `world:accounts:disconnect`, `world:accounts:status`, `world:game-context:revoke` | `get set setex psetex getdel del unlink exists expire pexpire incr decr hget hset hmset hincrby multi exec discard eval evalsha script\|load publish` |
| `api-worlds` | read `world:*:ready`, `world:*:presence`, `world:*:scripts`, `presence:world:*` | `world:*:maintenance`, `world:*:reload`, `world:*:reload:result` | `get publish subscribe unsubscribe` |
| `api-commerce` | read and write `commerce:*` | none | `eval evalsha script\|load exists get incr expire set` |

Every user also has the connection commands the client runs (`hello auth ping echo select client|setname
client|setinfo client|id`) and nothing else: no `KEYS`, `SCAN`, `INFO`, `CONFIG`, `FLUSH*` or `ACL`. The commands the
services' Lua scripts call are checked against the same rules. A key, channel or command added to a service needs its
user's rules widened in the same change, or the service gets `NOPERM`.

---

## String Keys

String keys map a single value to a single Redis string. All carry a TTL to prevent stale state accumulation.

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

### `auth:gameTicket:{ticketHash}`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.GameTicket(string ticketHash)` (`RedisGameTicketStore.Key`) |
| **Type** | String (one-time token) |
| **Owner (writer)** | API, identity: `RedisGameTicketStore.IssueAsync` (`POST client/auth/game-ticket`, a signed-in launcher session) |
| **Consumer (deleter)** | API, identity: `GameAuthorizationService.RedeemHandoffAsync` (`POST client/auth/handoffs/redeem`) |
| **Value** | `accountId|launcherFamilyId|credentialsVersion|sessionEpoch|environment` |
| **TTL** | 60 seconds |

**Purpose:** The launcher's handoff to the game. The launcher asks for a ticket (at most ten a minute per launcher
session, counted on `auth:gameTicketIssue:{familyId}`) and hands it to the game it starts; the game redeems it once
for a game context, the credential the join tickets are issued against. The key is the SHA-256 of the ticket, and the
redemption deletes it in the same compare-and-exchange that claims the attempt. No TCP server reads it: the auth
server's ticket login (`CMSG_AUTH_GAME_TICKET`) was retired with the TLS join-ticket admission.

### `game-auth:{environment}:{kind}:{digest}`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.GameAuth(string environment, string kind, string digest)` |
| **Type** | String (JSON records) |
| **Owner (writer)** | API, identity: `GameAuthorizationService`, `AuthAttemptStore`, `PendingLinkStore` and `JoinTicketStore`, through `RedisGameContextStore` |
| **Consumer** | API, identity |

**Purpose:** The game admission's state: authentication attempts, store proofs, game contexts and their tokens,
account-link proposals, join-ticket issues and join tickets (`kind` is `attempt`, `proof`, `context`, `token`,
`pending-link`, `link-consent`, `link-code`, `join-issue` or `join-ticket`), each keyed by a digest or id rather than
the bearer secret and isolated by the trusted environment. Join tickets live at most
30 seconds and are redeemed once, by the world server the ticket names ([game server admission](steam-authentication-workloads.md)).

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
| **Consumer (deleter)** | Auth server and the API's identity service — `MFAHashService.TryConsumeAsync` and `CleanupHash` |
| **TTL** | 2 minutes (set on the whole key in the same transaction as the fields) |

**Hash fields:**

| Field name | Description |
|---|---|
| `hash` | The MFA challenge token: the Base32 encoding of 20 random bytes, returned with `MFA_REQUIRED` and sent back with the code |
| `expiry` | ISO-8601 UTC timestamp recording when this entry expires |
| `accountId` | String representation of the account ID |
| `attempts` | Codes tried against this hash, counted with `HINCRBY` only while the hash exists |

**Purpose:** Stores ephemeral MFA state during the two-factor login window. When a password check succeeds for an
account with MFA enabled, `MFAHashService` generates a hash (or returns an unexpired one issued at the same credentials
version) and writes the fields, the expiry and the reverse key `auth:mfa:hash:{sha256}` in one Redis transaction. The
client must submit the hash back with a valid TOTP code within the TTL window. The code that wins spends the hash
(`TryConsumeAsync`); `CleanupHash` deletes the record only while its `hash` field is still the hash being cleaned.

---

## Pub/Sub Channels

Pub/Sub channels carry event notifications between services. There is no persistence — a message is only received by
subscribers active at the time of publish.

### `world:accounts:disconnect`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldAccountsDisconnectChannel` |
| **Publisher** | Auth server — `GameLoginCompletion` (duplicate login path); the API's identity service on a credentials change or refresh-token reuse (`AccountService`, `AccountRefreshController`, `ClientAuthController`); `MFAService` on an MFA reset, on either server. A ban or a deactivation publishes on [`world:accounts:status`](#worldaccountsstatus) instead (#882) |
| **Subscriber** | World server — `WorldServer.CacheSubscribeAsync`; Auth server — `AuthServer` |
| **Message format** | Account ID as a decimal string |

**Purpose:** Asks every server to drop the account's sessions. The original use is the cross-server half of the
duplicate-login guard: an account that logs in again from a different connection. The World server closes every
connection with a matching `AccountId`; the Auth server closes that account's logged-in TCP connections. The message
is the bare account id, so it cannot say why.

**Flow:**
```
Auth server (GameLoginCompletion) — a login of an account whose row is still Online
  → PUBLISH world:accounts:disconnect  {accountId}

World server (WorldServer.DelayedDisconnect → CloseAccountSessions)
  ← message received
  → every IWorldConnection where AccountId == accountId
  → GracefulShutdownHelper.NotifyAndClose(connection, "Your session has ended. Please log in again.", Kicked)
```

---

### `world:accounts:status`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldAccountsStatusChannel` |
| **Publisher** | API, identity — `AccountService.UpdateStatusAsync`, after the ban or deactivation commits (best effort; when this publish fails, the bare id goes on `world:accounts:disconnect` instead) |
| **Subscriber** | World server — `WorldServer.CacheSubscribeAsync` (`CloseBannedOrDeactivated`); Auth server — `AuthServer.SubscribeToAccountDisconnectsAsync` (`CloseBannedOrDeactivated`) |
| **Message format** | `{accountId}|BANNED` or `{accountId}|DEACTIVATED` (`AccountStatusNotice`; an id that is not canonical, another status or a longer message is ignored) |

**Purpose:** The bare disconnect for a status change, with the reason (#882). A ban or a deactivation publishes here
instead of `world:accounts:disconnect`, so the servers can tell the player why: every connection of the account is
closed with `DisconnectReason.Banned` (7) and "Your account has been banned.", or `Deactivated` (8) and "Your account
has been deactivated.". The same commit moved the account's session epoch, and the API then publishes
`world:game-context:revoke` with an empty context id, which asks every world holding a session of the account for a
heartbeat at once; the heartbeat is refused. So a server that missed this notice (or one from before #882, which does
not subscribe) still ends the session at its next heartbeat, silently, and a lost hint at most 15 seconds later.

**Flow:**
```
API identity (AccountService.UpdateStatusAsync) — status, epoch + 1, token revocations committed
  → PUBLISH world:accounts:status  {accountId}|BANNED
  → PUBLISH world:game-context:revoke  {accountId}|00000000000000000000000000000000

World server (WorldServer.AccountStatusChanged → CloseBannedOrDeactivated)
  ← message received
  → every IWorldConnection where AccountId == accountId
  → GracefulShutdownHelper.NotifyAndClose(connection, "Your account has been banned.", Banned)
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

## Key Inventory Summary

| Key pattern | Type | TTL | Writer | Consumer |
|---|---|---|---|---|
| `auth:account:{id}:mfa` | Hash | 2 min | Auth, API identity / `MFAHashService` | Auth, API identity / `MFAHashService` |
| `auth:mfa:hash:{sha256}` | String | 2 min | Auth, API identity / `MFAHashService` | Auth, API identity / `MFAHashService` |
| `auth:launcherCode:{hash}` | String | 60 s | API identity / `LauncherAuthCodes` | API identity / `LauncherAuthCodes` |
| `auth:gameTicket:{hash}` | String | 60 s | API identity / `RedisGameTicketStore` | API identity / `GameAuthorizationService` |
| `game-auth:{env}:*` | String | per record | API identity / `RedisGameContextStore` | API identity |
| `world:accounts:disconnect` | Pub/Sub channel | — | Auth / `GameLoginCompletion`; API identity; `MFAService` | World / `WorldServer`; Auth / `AuthServer` |
| `world:accounts:status` | Pub/Sub channel | — | API identity / `AccountService` | World / `WorldServer`; Auth / `AuthServer` |
| `auth:accounts:online` | Pub/Sub channel | — | Auth / `GameLoginCompletion` | *(reserved)* |

---

## Retired keys

The TCP world select and world-key exchange were replaced by the TLS join-ticket admission, and their keys went with
them; `CacheKeys` no longer names any of them.

- `world:{worldId}:keys:{base64}`: the one-time world entry token the auth server wrote at world select and the world
  server spent at the key exchange.
- `account:{accountId}:inWorld`: the `SETNX` mutex that refused a second world select as a duplicate session for
  five minutes. Nothing sets or deletes it any more.
- `world:{worldId}:select`: a world-select notification that nothing ever subscribed to.

---

## Adding a New Key

1. Add a `const` or `static` method to `CacheKeys` in `src/Server/Avalon.Infrastructure/CacheKeys.cs`.
2. Document it in this file under the appropriate section (String Key, Hash Key, or Pub/Sub Channel).
3. Update the summary table above.
4. If the key is used across more than one service, document the publish/subscribe or write/read split clearly.
