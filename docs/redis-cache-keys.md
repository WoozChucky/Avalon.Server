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
| identity | the login and registration budgets (`auth:source:{source}:failedLogins`, `auth:username:{sha256}:failedLogins`, `auth:source:{source}:accountsCreated`), MFA state (`auth:account:{accountId}:mfa`, `auth:mfa:hash:{sha256}`), email change (`auth:emailChange:{sha256}`, `auth:account:{accountId}:emailChangePending`, `auth:account:{accountId}:emailChangeSends`, `auth:email:{sha256}:emailChangeSends`), the email-verification send budgets (`email-verification:*`), launcher codes (`auth:launcherCode:{hash}`), game tickets (`auth:gameTicket:{hash}`, `auth:gameTicketIssue:{familyId}`), the game-auth records (`game-auth:{environment}:*`), the Steam web link's transactions and nonces (`steam-web:transaction:{id}`, `steam-web:nonce:{sha256}`), the world places and queue (`world:{worldId}:places`, `world:{worldId}:places:marks`, `world:{worldId}:queue`, `world:{worldId}:queue:checkins`, `world:{worldId}:queue:arrivals`, `world-queue:account:{accountId}`, #967); reads `world:{worldId}:ready` | publishes `world:accounts:disconnect`, `world:accounts:status` and `world:game-context:revoke` |
| worlds | reads `world:{worldId}:ready`, `world:{worldId}:presence`, `presence:world:{worldId}:character:{characterId}`, `world:{worldId}:scripts`, and for the world list `world:{worldId}:places` and `world:{worldId}:queue:checkins` (#967) | publishes `world:{worldId}:maintenance` and `world:{worldId}:reload`; subscribes to `world:{worldId}:reload:result` |
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
| `api-identity` | read and write `auth:source:*`, `auth:username:*`, `auth:account:*`, `auth:mfa:hash:*`, `auth:emailChange:*`, `auth:email:*`, `auth:launcherCode:*`, `auth:gameTicket:*`, `auth:gameTicketIssue:*`, `email-verification:*`, `game-auth:*`, `steam-web:*`, `world:*:places`, `world:*:places:marks`, `world:*:queue`, `world:*:queue:checkins`, `world:*:queue:arrivals`, `world-queue:account:*`; read `world:*:ready` | `world:accounts:disconnect`, `world:accounts:status`, `world:game-context:revoke` | `get set setex psetex getdel del unlink exists expire pexpire incr decr hget hset hdel hvals hpexpire hmset hincrby zadd zrem zscore zrank zcard zcount zrangebyscore zremrangebyscore multi exec discard eval evalsha script\|load publish` |
| `api-worlds` | read `world:*:ready`, `world:*:presence`, `world:*:scripts`, `presence:world:*`, `world:*:places`, `world:*:queue:checkins` | `world:*:maintenance`, `world:*:reload`, `world:*:reload:result` | `get zcount publish subscribe unsubscribe` |
| `api-commerce` | read and write `commerce:*` | none | `eval evalsha script\|load exists get incr expire set` |

Every user also has the connection commands the client runs (`hello auth ping echo select client|setname
client|setinfo client|id`) and nothing else: no `KEYS`, `SCAN`, `INFO`, `CONFIG`, `FLUSH*` or `ACL`. The commands the
services' Lua scripts call are checked against the same rules. A key, channel or command added to a service needs its
user's rules widened in the same change, or the service gets `NOPERM`.

The world places and queue (#967) need the identity rules above before the identity release that writes them is
deployed: without them its place script fails with `NOPERM` and every join answers 503. Their scripts call `GET SET
DEL INCR HGET HSET HDEL HVALS HPEXPIRE ZADD ZREM ZSCORE ZRANK ZCARD ZRANGEBYSCORE ZREMRANGEBYSCORE` (`HPEXPIRE`, Redis 7.4 or later, homelab#202,
for an ended session's tombstone, called so that a refusal writes no tombstone rather than failing the script; the rest
homelab#199), and identity's own
`/game/worlds` counts read with `ZCOUNT`, as the worlds service's lists do; a `NOPERM` on those reads is not an error
the caller sees, only counts of 0 and a Warning at most every 10 s (`RedisWorldOccupancy`). The scripts also touch another world's line keys (`world:{id}:queue` and
`world:{id}:queue:checkins`) named from the waiting key, which the patterns above cover.

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

## World places and queue (#967)

The places each world has given out and the line of a full world ([World player limit](world-player-limit.md)).
Identity is their only writer, and only through the Lua scripts of `RedisWorldPlaceStore`, each one atomic step; the
scripts take "now" from identity's `TimeProvider` as an argument and never call Redis `TIME`; only a tombstone's 45 s is counted
by Redis itself, from when it is written (`HPEXPIRE`). An entry whose time has
passed does not count and is removed by the next script that runs for its world. The worlds service and identity's
`/game/worlds` read the counts with `ZCOUNT` over the entries still current (`RedisWorldOccupancy`).

**One Redis instance only.** Joining a line leaves the account's line in another world, which the script names from
the account's waiting key rather than from its declared keys (spelled as `CacheKeys` spells them), and an activation
removes the account's place in every configured world in one call. That holds on a single Redis instance, which every
environment runs, and would not on a Redis Cluster.

### `world:{worldId}:places`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldPlaces(ushort worldId)` |
| **Type** | Sorted set: member the account id, score the moment the place expires (Unix ms) |
| **Owner (writer)** | API, identity: `RedisWorldPlaceStore` (a ticket's issue, the session's redemption, activation, heartbeats and end) |
| **Consumer** | API, identity (the scripts, and `RedisWorldOccupancy` for `/game/worlds`); API, worlds: `RedisWorldOccupancy` (`WorldDto.OnlineCount`) |
| **TTL** | None on the key; each member expires by its score |

**Purpose:** The ledger. Every admitted account holds one place per world: in the world, at character select, holding
a join ticket (until 15 s after its expiry), or held after a drop (from part 2). Activation removes the account's place
in every other world, and a join ticket admitted in one world removes the account's places that only a ticket holds
in every other (#967).

### `world:{worldId}:places:marks`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldPlaceMarks(ushort worldId)` |
| **Type** | Hash, five fields per account at most. `{accountId}`: the kind of place, `h` (held after a drop), `t:{enqueuedAtMs}` (a turn taken from the line, not yet used), `j:{untilMs}:{ticket}` (a join ticket's hold until then, the last ticket to enter, by the id of the session it becomes, N format) or `r` (released by the account's ticket for another world while its own ticket's hold ran: counted until that hold ends, kept by no further ticket, renewed by that ticket's redemption), and no field for a place a session alone holds. `{accountId}:s`: the fencing token of the session that owns the place's lease (`{fence}`), or a tombstone of the session that ended there (`e:{fence}`), which Redis expires one lease after it is written (`HPEXPIRE`), kept when the place goes, so the session's late renewals cannot bring the place back. A ticket that took a place held after a drop marks it `j:{untilMs}:{ticket}:h`, so its withdrawal holds the place again. `{accountId}:f`: since when (Unix ms) no session has claimed the place (taken or kept by a ticket, or left by a session's end), deleted by the activation or a heartbeat of the session that owns it; past `Application:WorldPlaces:UnclaimedPlaceMinutes` a further ticket is not kept on the place. `{accountId}:l`: `1` while an account refused `WORLD_FULL` at its place's lapse is locked out of the world, expired by Redis 45 s after its place is free (`HPEXPIRE`), removed by an activation, left to expire when a place goes. `{accountId}:p`: the fencing token of a session redeemed and not yet activated, expired by Redis one lease after the redemption, removed by the activation, the owner's heartbeat, the session's end, or the place's removal; the place does not lapse while it lives and no lockout does. One field besides the accounts', `released`: the time (Unix ms) the world's held places were last released at a stop or a cutoff, at most once every 10 s |
| **Owner (writer)** | API, identity: `RedisWorldPlaceStore` |
| **Consumer** | API, identity: the scripts |
| **TTL** | None on the key. The `{accountId}` and `{accountId}:f` fields and a live `{accountId}:s` go with their place; a tombstone (`e:{fence}`) expires on its own, one lease after it was written, and outlives the place it was written for; so do a lockout `{accountId}:l` (45 s after the place is free) and a pending marker `{accountId}:p` (one lease) |

**Purpose:** Who holds each place, which a sorted set cannot say. It lets the scripts
count held places, count a turn that expired unused, answer a returning or promoted account as such, and keep a
place a join ticket holds through the end of the session the ticket replaces (a takeover or a reconnect), withdraw
only a failed ticket's own hold, spend at an activation only that session's own ticket's hold, and ignore the late renewal
or end of a session older than the owner, or of one that ended there (fencing tokens only grow for an account). The `{accountId}:s` values are
numbers or `e:` and a number, so the held-places count over `HVALS` (values equal to `h`) never counts them. A replay
only extends a place still there, while its ticket is unredeemed, and an end never re-creates one.

### `world:{worldId}:queue`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldQueue(ushort worldId)` |
| **Type** | Sorted set: member the account id, score its arrival number (the priority band, always 0, in the high bits) |
| **Owner (writer)** | API, identity: `RedisWorldPlaceStore` |
| **Consumer** | API, identity: the scripts |
| **TTL** | None; a waiter leaves it with its check-in deadline |

**Purpose:** A full world's line, in strict arrival order (from part 4).

### `world:{worldId}:queue:checkins`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldQueueCheckIns(ushort worldId)` |
| **Type** | Sorted set: member the account id, score the moment the waiter leaves the line unless it checks in again (Unix ms) |
| **Owner (writer)** | API, identity: `RedisWorldPlaceStore` |
| **Consumer** | API, identity (the scripts, and `RedisWorldOccupancy`); API, worlds: `RedisWorldOccupancy` (`WorldDto.QueueLength`) |
| **TTL** | None; each member expires by its score |

**Purpose:** Removes a waiter that stopped checking in, so the line never stalls. It stores the removal deadline, not
the last check-in, so a waiter told "every 15 s" keeps the 45 s it was given when the line moves it into the first ten.

### `world:{worldId}:queue:arrivals`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldQueueArrivals(ushort worldId)` |
| **Type** | String (an `INCR` counter) |
| **Owner (writer)** | API, identity: `RedisWorldPlaceStore` |
| **Consumer** | API, identity: the scripts |
| **TTL** | None |

**Purpose:** The arrival number that orders the line.

### `world-queue:account:{accountId}`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldQueueWaiting(long accountId)` (prefix `CacheKeys.WorldQueueWaitingPrefix`) |
| **Type** | String: `{worldId}:{enqueuedAtMs}` |
| **Owner (writer)** | API, identity: `RedisWorldPlaceStore` |
| **Consumer** | API, identity: the scripts |
| **TTL** | 24 hours, as garbage collection only; the key goes when the account leaves the line |

**Purpose:** One line per account: the world the account waits for and since when. Joining another world's line
leaves the first, and a turn's wait is measured from it.

---

## Pub/Sub Channels

Pub/Sub channels carry event notifications between services. There is no persistence — a message is only received by
subscribers active at the time of publish.

### `world:accounts:disconnect`

| Field | Value |
|---|---|
| **`CacheKeys` member** | `CacheKeys.WorldAccountsDisconnectChannel` |
| **Publisher** | Auth server — `GameLoginCompletion` (duplicate login path); the API's identity service on a credentials change or refresh-token reuse (`AccountService`, `AccountRefreshController`, `ClientAuthController`); `MFAService` on an MFA reset, on either server. A ban or a deactivation publishes on [`world:accounts:status`](#worldaccountsstatus) instead (#882), and falls back to the bare id here when that publish fails |
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
| `world:{id}:places` | Sorted set | per member (score) | API identity / `RedisWorldPlaceStore` | API identity; API worlds / `RedisWorldOccupancy` |
| `world:{id}:places:marks` | Hash | with its place | API identity / `RedisWorldPlaceStore` | API identity |
| `world:{id}:queue` | Sorted set | with its check-in | API identity / `RedisWorldPlaceStore` | API identity |
| `world:{id}:queue:checkins` | Sorted set | per member (score) | API identity / `RedisWorldPlaceStore` | API identity; API worlds / `RedisWorldOccupancy` |
| `world:{id}:queue:arrivals` | String | — | API identity / `RedisWorldPlaceStore` | API identity |
| `world-queue:account:{id}` | String | 24 h | API identity / `RedisWorldPlaceStore` | API identity |
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
